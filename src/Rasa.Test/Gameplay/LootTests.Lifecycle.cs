extern alias RasaGame;

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.LootDispenser.Server;
    using Rasa.Structures;
    using Rasa.Test.World;

    public partial class LootTests
    {
        [TestMethod]
        [DataRow("OtherOwner")]
        [DataRow("Disconnected")]
        [DataRow("Loading")]
        [DataRow("LoggingOut")]
        [DataRow("RemovedPlayer")]
        [DataRow("DeadPlayer")]
        [DataRow("DyingPlayer")]
        [DataRow("ZeroHealth")]
        [DataRow("MissingHealth")]
        [DataRow("OtherContext")]
        [DataRow("MissingClientMembership")]
        [DataRow("MissingPlayerCell")]
        [DataRow("UnregisteredPlayer")]
        [DataRow("UnregisteredCorpse")]
        [DataRow("Expired")]
        [DataRow("AliveCorpse")]
        [DataRow("HealedCorpse")]
        [DataRow("MissingCorpseCell")]
        [DataRow("MissingCorpseCells")]
        [DataRow("DetachedCorpse")]
        [DataRow("Disabled")]
        [DataRow("OldPlayerLifetime")]
        [DataRow("OldCorpseLifetime")]
        [DataRow("UnknownId")]
        [DataRow("MissingLoot")]
        [DataRow("InvalidPlayerPosition")]
        public void OpeningAndClaimingRejectInactiveForeignOrStaleTargetsWithoutPackets(string invalid)
        {
            using var context = new LootContext();
            var manager = new LootDispenserManager(context.Storage);
            var entityId = context.Loot.EntityId;
            switch (invalid)
            {
                case "OtherOwner": context.Loot.Owner++; break;
                case "Disconnected": context.Client.State = ClientState.Disconnected; break;
                case "Loading": context.Client.State = ClientState.Loading; break;
                case "LoggingOut": context.Client.Player.LogoutActive = true; break;
                case "RemovedPlayer": context.Client.Player.RemoveFromMap = true; break;
                case "DeadPlayer": context.Client.Player.State = CharacterState.Dead; break;
                case "DyingPlayer": context.Client.Player.State = CharacterState.Dying; break;
                case "ZeroHealth": context.Client.Player.Attributes[Attributes.Health].Current = 0; break;
                case "MissingHealth": context.Client.Player.Attributes.Remove(Attributes.Health); break;
                case "OtherContext": context.Client.Player.MapContextId = 1148; break;
                case "MissingClientMembership": context.Map.ClientList.Remove(context.Client); break;
                case "MissingPlayerCell": context.Map.MapCellInfo.Cells[context.Client.Player.Cells[2, 2]].ClientList.Clear(); break;
                case "UnregisteredPlayer": EntityManager.Instance.UnregisterEntity(context.Client.Player.EntityId); break;
                case "UnregisteredCorpse": EntityManager.Instance.UnregisterEntity(context.Corpse.EntityId); break;
                case "Expired": context.Corpse.Controller.DeadTime = 20000; break;
                case "AliveCorpse": context.Corpse.State = CharacterState.Normal; break;
                case "HealedCorpse": context.Corpse.Attributes[Attributes.Health].Current = 1; break;
                case "MissingCorpseCell": context.Map.MapCellInfo.Cells[context.Corpse.Cells[2, 2]].CreatureList.Clear(); break;
                case "MissingCorpseCells": context.Corpse.Cells = null; break;
                case "DetachedCorpse": context.Corpse.LootDispenserObjectEntityId = 0; break;
                case "Disabled": context.Loot.IsLootable = false; break;
                case "OldPlayerLifetime": context.Client.Player.InvalidateAbilityLifetime(); break;
                case "OldCorpseLifetime": context.Corpse.InvalidateAbilityLifetime(); break;
                case "UnknownId": entityId = ulong.MaxValue; break;
                case "MissingLoot": context.Map.LootDispensers.Remove(entityId); break;
                case "InvalidPlayerPosition": context.Client.Player.Position = new Vector3(0, float.NaN, 0); break;
            }
            try
            {
                Assert.IsFalse(manager.RequestCorpseLooting(context.Client, new RequestCorpseLootingPacket { EntityId = entityId }));
                Assert.IsFalse(manager.RequestLootAllFromCorpse(context.Client, new RequestLootAllFromCorpsePacket { EntityId = entityId }));
                Assert.AreEqual(0, context.Storage.SaveAttempts);
                Assert.AreEqual(100, context.ReadCredits());
                Assert.AreEqual(0, context.Drain().Count);
                Assert.AreEqual(3u, context.Loot.LootItems.Single().ItemQuantity);
            }
            finally
            {
                if (invalid == "UnregisteredCorpse")
                    EntityManager.Instance.RegisterEntity(context.Corpse.EntityId, EntityType.Creature);
                if (invalid == "MissingLoot")
                    context.Map.LootDispensers.Add(entityId, context.Loot);
            }
        }

        [TestMethod]
        [DataRow(0d)]
        [DataRow(-1d)]
        [DataRow(double.NaN)]
        [DataRow(double.PositiveInfinity)]
        public void InvalidConfiguredDistanceFailsClosedAtBothEntryPoints(double limit)
        {
            using var context = new LootContext();
            var manager = new LootDispenserManager(context.Storage, _ => limit);
            Assert.IsFalse(manager.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId }));
            Assert.IsFalse(manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId }));
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void CurrentConfiguredDistanceIsRecheckedAtClaimNotJustAtOpening()
        {
            using var context = new LootContext();
            context.Corpse.Position = new Vector3(0, 3, 0);
            var limit = 3d;
            var manager = new LootDispenserManager(context.Storage, _ => limit);
            Assert.IsTrue(manager.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId }));
            context.Drain();
            limit = 2;
            Assert.IsFalse(manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId }));
            Assert.AreEqual(0, context.Drain().Count);
            Assert.AreEqual(100, context.ReadCredits());
            limit = 3;
            Assert.IsTrue(manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId }));
        }

        [TestMethod]
        public void MovingOutOfRangeAfterOpeningPreventsClaimUntilThePlayerReturns()
        {
            using var context = new LootContext();
            Assert.IsTrue(LootDispenserManager.Instance.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId }));
            context.Drain();
            context.Client.Player.Position = new Vector3(0, 2.001f, 0);
            Assert.IsFalse(Claim(context));
            Assert.AreEqual(0, context.Drain().Count);
            context.Client.Player.Position = new Vector3(0, 2, 0);
            Assert.IsTrue(Claim(context));
        }

        [TestMethod]
        public void ConcurrentClaimsCommitOnlyOneGrant()
        {
            using var context = new LootContext();
            var succeeded = 0;
            Parallel.For(0, 16, _ =>
            {
                if (Claim(context))
                    Interlocked.Increment(ref succeeded);
            });
            Assert.AreEqual(1, succeeded);
            Assert.AreEqual(3, context.Storage.SaveAttempts);
            Assert.AreEqual(107, context.ReadCredits());
            Assert.AreEqual(1, context.Drain().OfType<TakenInfoPacket>().Count());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RemovalAndDepartureWaitForAnAdmittedClaimToCommit(bool departure)
        {
            using var context = new LootContext();
            using var saving = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var removing = new ManualResetEventSlim();
            context.Storage.BeforeSave = _ =>
            {
                if (context.Storage.SaveAttempts != 1)
                    return;
                saving.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Claim test did not release its save gate.");
            };
            var claim = Task.Run(() => Claim(context));
            Task removal = null;
            try
            {
                Assert.IsTrue(saving.Wait(TimeSpan.FromSeconds(10)));
                removal = Task.Run(() =>
                {
                    removing.Set();
                    if (departure)
                        CellManager.Instance.RemoveFromWorld(context.Client);
                    else
                        CellManager.Instance.RemoveCreatureFromWorld(context.Map, context.Corpse);
                });
                Assert.IsTrue(removing.Wait(TimeSpan.FromSeconds(10)));
                Assert.IsFalse(removal.Wait(75), "Lifecycle removal must not run inside the admitted transaction.");
            }
            finally
            {
                release.Set();
                Assert.IsTrue(claim.Wait(TimeSpan.FromSeconds(10)));
                if (removal != null)
                    Assert.IsTrue(removal.Wait(TimeSpan.FromSeconds(10)));
            }
            Assert.IsTrue(claim.Result);
            Assert.AreEqual(107, context.ReadCredits());
            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.IsFalse(Claim(context));
        }

        [TestMethod]
        public void DisconnectCleanupRemovesLootEvenWithoutCellMembership()
        {
            using var context = new LootContext();
            context.Map.MapCellInfo.Cells[context.Client.Player.Cells[2, 2]].ClientList.Clear();
            context.Client.State = ClientState.Disconnected;
            ManifestationManager.Instance.RemovePlayerCharacter(context.Client);
            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.AreEqual(0, context.Loot.LootItems.Count);
            Assert.AreEqual(0, context.Drain().Count);
            Assert.IsFalse(Claim(context));
        }

        [TestMethod]
        public void DelayedDispenserIdsNeverIdentifyALaterCorpsesLoot()
        {
            using var context = new LootContext();
            var id = context.Loot.EntityId;
            Assert.IsTrue(Claim(context));
            using var other = new LootContext();
            Assert.AreNotEqual(id, other.Loot.EntityId);
            Assert.IsFalse(new LootDispenserManager(other.Storage).RequestLootAllFromCorpse(other.Client,
                new RequestLootAllFromCorpsePacket { EntityId = id }));
            Assert.AreEqual(100, other.ReadCredits());
        }

        [TestMethod]
        public void ExpiryReclaimsTheDispenserBeforeDestroyingTheCorpse()
        {
            using var context = new LootContext();
            BehaviorManager.Instance.MapChannelThink(context.Map, 20000);
            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.IsFalse(EntityManager.Instance.Creatures.ContainsKey(context.Corpse.EntityId));
            var packets = context.Drain();
            Assert.IsFalse(packets.OfType<CanLootItemsPacket>().Single().CanLootItems);
            var destroyed = packets.OfType<Rasa.Packets.MapChannel.Server.DestroyPhysicalEntityPacket>().ToList();
            Assert.AreEqual(2, destroyed.Count);
            CollectionAssert.AreEqual(new[] { context.Loot.EntityId, context.Corpse.EntityId },
                destroyed.Select(packet => packet.EntityId).ToArray());
            Assert.AreEqual(0, context.Loot.LootItems.Count);
            Assert.IsFalse(Claim(context));
            Assert.AreEqual(100, context.ReadCredits());
        }

        [TestMethod]
        public void LeavingAndReturningToAnIdenticalMapContextDoesNotRestoreLootRights()
        {
            using var context = new LootContext();
            using var otherMap = new WorldTestContext();
            context.Client.Player.MapChannel = otherMap.Map;
            otherMap.Map.ClientList.Add(context.Client);
            context.Client.Player.MapChannel = context.Map;
            Assert.IsFalse(Claim(context));
            Assert.AreEqual(100, context.ReadCredits());
        }

        [TestMethod]
        public void ARemovedAndReusedCreatureIdCannotRedirectTheOldLoot()
        {
            using var context = new LootContext();
            var replacement = new Creature();
            var allocated = replacement.EntityId;
            replacement.EntityId = context.Corpse.EntityId;
            EntityManager.Instance.Creatures[replacement.EntityId] = replacement;
            try
            {
                Assert.IsFalse(Claim(context));
                Assert.AreEqual(0, context.Drain().Count);
            }
            finally
            {
                EntityManager.Instance.Creatures[context.Corpse.EntityId] = context.Corpse;
                EntityManager.Instance.FreeEntity(allocated);
            }
        }

        [TestMethod]
        public void NoSecondDispenserCanRerollTheSameCorpse()
        {
            using var context = new LootContext();
            Assert.IsNull(LootDispenserManager.Instance.Create(context.Client, context.Corpse));
            Assert.IsTrue(Claim(context));
            Assert.IsNull(LootDispenserManager.Instance.Create(context.Client, context.Corpse));
        }
    }
}
