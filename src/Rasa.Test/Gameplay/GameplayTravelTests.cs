extern alias RasaGame;

using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.MapChannel.Server.PerformRecovery;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Test.World;
    using ClientState = RasaGame::Rasa.Data.ClientState;

    [TestClass]
    [DoNotParallelize]
    public class GameplayTravelTests
    {
        [TestMethod]
        [DataRow(WaypointType.Waypoint, "lightning")]
        [DataRow(WaypointType.Waypoint, "lightning-rank2")]
        [DataRow(WaypointType.Waypoint, "reload")]
        [DataRow(WaypointType.Waypoint, "autofire")]
        [DataRow(WaypointType.Waypoint, "missile")]
        [DataRow(WaypointType.LocalTeleporter, "lightning")]
        [DataRow(WaypointType.LocalTeleporter, "lightning-rank2")]
        [DataRow(WaypointType.LocalTeleporter, "reload")]
        [DataRow(WaypointType.LocalTeleporter, "autofire")]
        [DataRow(WaypointType.LocalTeleporter, "missile")]
        [DataRow(WaypointType.Dropship, "lightning")]
        [DataRow(WaypointType.Dropship, "lightning-rank2")]
        [DataRow(WaypointType.Dropship, "reload")]
        [DataRow(WaypointType.Dropship, "autofire")]
        [DataRow(WaypointType.Dropship, "missile")]
        public void TravelAdmissionCancelsCombatBeforeRelocationAndBatchedAcknowledgement(WaypointType type, string work)
        {
            using var context = new AbilityTestContext();
            context.Storage.World.AddClass(EntityClasses.UsableCrSpawnerHumDropshipV01);
            context.Learn(165, 1);
            context.Learn(49, work == "lightning-rank2" ? 2 : 1);
            var target = context.Target();
            var secondary = work == "lightning-rank2" ? context.Target(11) : null;
            var reserve = context.Storage.AddAmmo(30);
            context.Cast(action: ActionId.AaRecruitSprint);
            QueueWork(context, target, work);
            var pending = context.Map.PerformRecovery.SingleOrDefault();
            var missile = context.Map.QueuedMissiles.SingleOrDefault();
            var expectedAmmo = work == "missile" ? 6u : 7u;
            var worldLifetime = context.Client.Player.AbilityLifetime;
            var manager = CreateTravel(context.Storage, type);
            context.Now += 100;
            context.Drain();
            try
            {
                manager.SelectWaypoint(context.Client, new SelectWaypointPacket { MapInstanceId = 1, WaypointId = 20 });

                Assert.IsNotNull(context.Client.PendingTransfer);
                Assert.IsNull(context.Client.Player.PendingAbility);
                Assert.IsNull(context.Client.Player.PendingWeaponAction);
                Assert.IsNull(context.Client.Player.Sprint);
                Assert.AreEqual(0, context.Map.PerformRecovery.Count);
                Assert.AreEqual(0, context.Map.SprintEffects.Count);
                Assert.AreEqual(1d, context.Client.Player.MovementSpeed);
                Assert.AreEqual(999, context.Client.Player.Attributes[Attributes.Chi].Current);
                if (type == WaypointType.Dropship)
                {
                    Assert.AreEqual(Vector3.Zero, context.Client.Player.Position);
                    var ship = manager.Dropships[context.Client.PendingTransfer.DropshipId];
                    for (var step = 0; step < 6; step++)
                        manager.DropshipsWorker(context.Map, Math.Max(0, ship.PhaseTimeleft));
                    Assert.IsTrue(context.Client.PendingTransfer.HasDeparted);
                    // This same-map dropship still uses the actual map-load acknowledgement.
                    context.Map.ClientList.Add(context.Client);
                    CellManager.Instance.AddToWorld(context.Client);
                    Assert.IsTrue(manager.CompleteMapLoadTransfer(context.Client));
                    context.Client.State = ClientState.Ingame;
                }
                else
                {
                    // No action, missile or effect worker runs between selection and acknowledgement.
                    manager.TeleportAcknowledge(context.Client);
                    Assert.AreEqual(worldLifetime, context.Client.Player.AbilityLifetime);
                }
                Assert.IsNull(context.Client.PendingTransfer);
                Assert.AreEqual(ClientState.Ingame, context.Client.State);
                var destination = new Vector3(20, type == WaypointType.Dropship ? 0 : 1, 0);
                Assert.AreEqual(destination, context.Client.Player.Position);
                Assert.AreEqual(destination, context.Client.Movement.Position);
                using (var database = context.Storage.Open())
                {
                    var character = database.CharacterEntries.Single();
                    Assert.AreEqual((double)destination.X, character.CoordX);
                    Assert.AreEqual((double)destination.Y, character.CoordY);
                    Assert.AreEqual((double)destination.Z, character.CoordZ);
                }
                context.Drain();

                context.Advance(1500);
                if (pending != null)
                    new ActorActionManager(context.Manager).PerformRecovery(context.Map, pending);
                if (missile != null)
                    MissileManager.Instance.MissileTrigger(context.Map, missile);
                MissileManager.Instance.DoWork(context.Map, 1500);
                context.Manager.AutoFireTimerDoWork(1500);
                GameEffectManager.Instance.DoWork(context.Map, 1500);

                Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
                if (secondary != null)
                    Assert.AreEqual(10000, secondary.Attributes[Attributes.Health].Current);
                Assert.AreEqual(1000, context.Client.Player.Attributes[Attributes.Power].Current);
                Assert.AreEqual(999, context.Client.Player.Attributes[Attributes.Chi].Current);
                Assert.AreEqual(expectedAmmo, context.Storage.Weapon.CurrentAmmo);
                Assert.AreEqual(expectedAmmo, context.Storage.Read(context.Storage.Weapon).AmmoCount);
                Assert.AreEqual(30u, context.Storage.Read(reserve).StackSize);
                var packets = context.Drain();
                Assert.AreEqual(0, packets.OfType<LightningRecovery>().Count());
                Assert.AreEqual(0, packets.OfType<WeaponAttackRecovery>().Count());
                Assert.AreEqual(0, packets.OfType<PerformRecoveryPacket>().Count());
                Assert.AreEqual(0, packets.OfType<WeaponAmmoInfoPacket>().Count());
            }
            finally
            {
                manager.CleanupClientDropships(context.Client);
                context.Client.PendingTransfer = null;
            }
        }

        [TestMethod]
        [DataRow(WaypointType.Waypoint, "lightning")]
        [DataRow(WaypointType.Waypoint, "reload")]
        [DataRow(WaypointType.LocalTeleporter, "lightning")]
        [DataRow(WaypointType.LocalTeleporter, "reload")]
        [DataRow(WaypointType.Dropship, "lightning")]
        [DataRow(WaypointType.Dropship, "reload")]
        public void RejectedTravelLeavesPendingWorkSprintAndAutofireIntact(WaypointType type, string work)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Learn(49, 1);
            var target = context.Target();
            context.Storage.AddAmmo(30);
            context.Cast(action: ActionId.AaRecruitSprint);
            QueueWork(context, target, work);
            context.Manager.RegisterAutoFire(context.Client);
            context.Manager.AutoFireKeepAlive(context.Client, 1000);
            var pending = context.Map.PerformRecovery.Single();
            var sprint = context.Client.Player.Sprint;
            var lifetime = context.Client.Player.AbilityLifetime;
            var manager = CreateTravel(context.Storage, type);
            ((WaypointInfo)manager.Teleporters[20].ObjectData).Contested = true;
            var saves = context.Storage.SaveAttempts;
            context.Drain();

            manager.SelectWaypoint(context.Client, new SelectWaypointPacket { MapInstanceId = 1, WaypointId = 20 });

            Assert.IsNull(context.Client.PendingTransfer);
            Assert.AreEqual(ClientState.Ingame, context.Client.State);
            Assert.AreEqual(Vector3.Zero, context.Client.Player.Position);
            Assert.AreEqual(lifetime, context.Client.Player.AbilityLifetime);
            Assert.AreSame(pending, context.Map.PerformRecovery.Single());
            Assert.AreSame(sprint, context.Client.Player.Sprint);
            Assert.AreEqual(1.2d, context.Client.Player.MovementSpeed);
            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            Assert.AreEqual(1, context.Drain().OfType<TeleportFailedPacket>().Count());
            context.Advance(1500);
            Assert.AreEqual(work == "lightning" ? 9820 : 10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(work == "lightning" ? 975 : 1000, context.Client.Player.Attributes[Attributes.Power].Current);
            var before = context.Storage.Weapon.CurrentAmmo;
            context.Manager.AutoFireTimerDoWork(800);
            Assert.AreEqual(before - 1, context.Storage.Weapon.CurrentAmmo);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SameMapTravelPreservesNearbyCorpseEligibility(bool automatic)
        {
            using var context = new LootContext();
            var manager = CreateTravel(context.Storage, WaypointType.Waypoint, new Vector3(1, 0, 0));
            var lifetime = context.Client.Player.AbilityLifetime;

            manager.SelectWaypoint(context.Client, new SelectWaypointPacket { MapInstanceId = 1, WaypointId = 20 });
            manager.TeleportAcknowledge(context.Client);

            Assert.AreEqual(lifetime, context.Client.Player.AbilityLifetime);
            Assert.AreEqual(1, context.Map.LootDispensers.Count);
            Assert.IsTrue(new LootDispenserManager(context.Storage).RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId, AutoLootOnly = automatic }));
            Assert.AreEqual(107, context.ReadCredits());
        }

        private static DynamicObjectManager CreateTravel(WeaponAmmoContext storage, WaypointType type, Vector3? destination = null)
        {
            var manager = WaypointTravelTests.CreateManager(storage.World, new CharacterManager(storage).UpdateCharacter);
            WaypointTravelTests.AddWaypoint(manager, storage.World.Map, 10, Vector3.Zero, type: type);
            WaypointTravelTests.AddWaypoint(manager, storage.World.Map, 20, destination ?? new Vector3(20, 0, 0), type: type);
            storage.Client.Player.GainedWaypoints.Add(new CharacterTeleporterEntry(storage.Client.Player.Id, 20, (byte)type));
            return manager;
        }

        private static void QueueWork(AbilityTestContext context, Creature target, string work)
        {
            switch (work)
            {
                case "lightning": context.Cast(target: target.EntityId); break;
                case "lightning-rank2": context.Cast(2, target.EntityId); break;
                case "reload": context.Manager.RequestWeaponReload(context.Client, true); break;
                case "autofire":
                    context.Client.Player.Target = target.EntityId;
                    context.Manager.RegisterAutoFire(context.Client);
                    context.Manager.AutoFireKeepAlive(context.Client, 1000);
                    break;
                case "missile":
                    context.Client.Player.Target = target.EntityId;
                    Assert.IsTrue(context.Manager.PlayerTryFireWeapon(context.Client));
                    break;
                default: Assert.Fail($"Unknown work {work}."); break;
            }
        }
    }
}
