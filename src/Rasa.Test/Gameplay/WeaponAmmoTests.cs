extern alias RasaGame;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Managers;
    using Rasa.Data;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Packets.Inventory.Client;
    using Rasa.Structures;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class WeaponAmmoTests
    {
        [TestMethod]
        public void AutofireAdmittedBeforeDeathCannotResumeAfterRevival()
        {
            using var context = new WeaponAmmoContext();
            long now = 10000;
            var manager = new ManifestationManager(context, null, () => now);
            manager.RegisterAutoFire(context.Client);
            manager.AutoFireKeepAlive(context.Client, 1000);
            context.Client.Player.State = CharacterState.Dead;
            context.Client.Player.State = CharacterState.Normal;
            now += 800;

            manager.AutoFireTimerDoWork(800);

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, context.World.Map.QueuedMissiles.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            manager.RegisterAutoFire(context.Client);
            manager.AutoFireKeepAlive(context.Client, 1000);
            manager.AutoFireTimerDoWork(800);
            Assert.AreEqual(6u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        [DataRow("source-death")]
        [DataRow("target-death")]
        [DataRow("source-reentry")]
        [DataRow("target-reentry")]
        [DataRow("empty-map")]
        public void MissilesCannotCrossSourceOrTargetLifetimes(string transition)
        {
            using var context = new AbilityTestContext();
            var target = context.Target();
            context.Client.Player.Target = target.EntityId;
            var experience = context.Client.Player.Experience;
            Assert.IsTrue(context.Manager.PlayerTryFireWeapon(context.Client));
            Assert.AreEqual(1, context.Map.QueuedMissiles.Count);
            if (transition == "source-death" || transition == "target-death")
            {
                Actor actor = transition == "source-death" ? context.Client.Player : target;
                actor.State = CharacterState.Dead;
                actor.State = CharacterState.Normal;
            }
            else if (transition == "target-reentry")
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.Map, target);
                CellManager.Instance.AddToWorld(context.Map, target);
            }
            else
            {
                CellManager.Instance.RemoveFromWorld(context.Client);
                context.Map.ClientList.Remove(context.Client);
                if (transition == "empty-map")
                {
                    var maps = new MapChannelManager(context.Storage);
                    maps.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
                    maps.MapChannelWorker(10000);
                    Assert.AreEqual(1, context.Map.QueuedMissiles.Count);
                }
                context.Map.ClientList.Add(context.Client);
                CellManager.Instance.AddToWorld(context.Client);
            }
            context.Drain();

            MissileManager.Instance.DoWork(context.Map, 1000);

            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(experience, context.Client.Player.Experience);
            Assert.AreEqual(0, context.Map.QueuedMissiles.Count);
            Assert.AreEqual(0, context.Drain().Count);
            Assert.AreEqual(6u, context.Storage.Read(context.Storage.Weapon).AmmoCount);
            Assert.IsFalse(context.Manager.PlayerTryFireWeapon(context.Client));
            context.Now += 800;
            Assert.IsTrue(context.Manager.PlayerTryFireWeapon(context.Client));
            MissileManager.Instance.DoWork(context.Map, 1000);
            Assert.AreEqual(9945, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(5u, context.Storage.Read(context.Storage.Weapon).AmmoCount);
        }

        [TestMethod]
        [DataRow("reload")]
        [DataRow("draw")]
        [DataRow("stow")]
        public void WeaponRecoveryAfterDeathAndRevivalCannotCommitOrPublish(string operation)
        {
            using var context = new WeaponAmmoContext();
            var reserve = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            if (operation == "reload")
                manager.RequestWeaponReload(context.Client, true);
            else if (operation == "draw")
            {
                context.Client.Player.WeaponReady = false;
                manager.RequestWeaponDraw(context.Client);
            }
            else
                manager.RequestWeaponStow(context.Client);
            var pending = context.World.Map.PerformRecovery.Single();
            context.Client.Player.State = CharacterState.Dead;
            context.Client.Player.State = CharacterState.Normal;
            WorldTestContext.Drain(context.Client);

            var actions = new ActorActionManager(manager);
            actions.DoWork(context.World.Map, 1500);
            actions.PerformRecovery(context.World.Map, pending);

            Assert.IsNull(context.Client.Player.PendingWeaponAction);
            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(30u, reserve.StackSize);
            Assert.AreEqual(30u, context.Read(reserve).StackSize);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void ConcurrentManualRequestsCannotBypassRefireOrSpendTheSameRoundTwice()
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context, null, () => 10000);
            var fired = 0;

            System.Threading.Tasks.Parallel.For(0, 12, _ =>
            {
                if (manager.PlayerTryFireWeapon(context.Client))
                    System.Threading.Interlocked.Increment(ref fired);
            });

            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, context.SaveAttempts);
            Assert.AreEqual(6u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void DelegatingCharacterTransactionRollsBackRepositorySavesAndPropagatesFailure()
        {
            using var context = new WeaponAmmoContext();
            using var parent = context.CreateChar();
            using var provider = new ServiceCollection().BuildServiceProvider();
            using var scope = provider.CreateScope();
            using var transaction = new Rasa.Repositories.UnitOfWork.DelegatingCharUnitOfWork(parent, scope);
            var failed = false;
            try
            {
                transaction.ExecuteTransaction(() =>
                {
                    transaction.Items.UpdateAmmo(new Item(0, 0, 0, 0) { Id = context.Weapon.Id, CurrentAmmo = 1 });
                    throw new System.InvalidOperationException("Fixture operation failure after repository save.");
                });
            }
            catch (System.InvalidOperationException)
            {
                failed = true;
            }

            Assert.IsTrue(failed);
            Assert.AreEqual(1, context.SaveAttempts);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void ReloadOwnershipUsesCharacterIdNotTheAccountSelectionSlot()
        {
            using var context = new WeaponAmmoContext(characterId: 42);
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);

            manager.RequestWeaponReload(context.Client, true);
            manager.WeaponReload(context.World.Map.PerformRecovery.Single());

            Assert.AreEqual(20u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(17u, context.Read(ammo).StackSize);
        }

        [TestMethod]
        public void MissingForeignAndUnmatchedReserveStacksDoNotHideValidAmmunition()
        {
            using var context = new WeaponAmmoContext();
            context.Client.Player.Inventory.PersonalInventory[50] = ulong.MaxValue;
            var foreign = context.AddAmmo(30, 51);
            foreign.OwnerId = 99;
            var unmatched = context.AddAmmo(30, 52);
            unmatched.ItemTemplate.Class = (EntityClasses)6048;
            var valid = context.AddAmmo(30, 53);
            var manager = new ManifestationManager(context);

            manager.RequestWeaponReload(context.Client, true);
            manager.WeaponReload(context.World.Map.PerformRecovery.Single());

            Assert.AreEqual(30u, context.Read(foreign).StackSize);
            Assert.AreEqual(30u, context.Read(unmatched).StackSize);
            Assert.AreEqual(17u, context.Read(valid).StackSize);
            Assert.AreEqual(20u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void QueuedEquipmentAndAmmoPacketsStayPairedAcrossRapidWeaponSwitches()
        {
            using var context = new WeaponAmmoContext();
            var other = context.AddWeapon(2, 1);
            var manager = new ManifestationManager(context);

            manager.RequestArmWeapon(context.Client, 1);
            manager.RequestArmWeapon(context.Client, 0);

            var packets = WorldTestContext.Drain(context.Client).Select(p => p.Message).OfType<CallMethodMessage>().Select(p => p.Packet).ToList();
            CollectionAssert.AreEqual(new[] { other.EntityId, context.Weapon.EntityId },
                packets.OfType<EquipmentInfoPacket>().Select(p => p.EquipmentInfo[13]).ToArray());
            CollectionAssert.AreEqual(new[] { 2u, 7u }, packets.OfType<WeaponAmmoInfoPacket>().Select(p => p.AmmoInfo).ToArray());
        }

        [TestMethod]
        public void WeaponRecoveryCannotRunFromAnotherMapWorker()
        {
            using var context = new WeaponAmmoContext();
            using var other = new WorldTestContext();
            context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();

            new ActorActionManager(manager).PerformRecovery(other.Map, action);

            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreSame(action, context.World.Map.PerformRecovery.Single());
        }

        [TestMethod]
        public void EmptyWeaponReloadsOnceAfterItsTemplateDelayThenFires()
        {
            using var context = new WeaponAmmoContext(0);
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            var actions = new ActorActionManager(manager);
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            Assert.AreEqual(1, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(1500L, context.World.Map.PerformRecovery.Single().WaitTime);

            actions.DoWork(context.World.Map, 1499);
            Assert.AreEqual(0u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(0, context.SaveAttempts);
            actions.DoWork(context.World.Map, 1);

            Assert.AreEqual(20u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(10u, ammo.StackSize);
            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));
            Assert.AreEqual(19u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void DrawAndStowRecoverOnlyOnceAndCannotBypassWeaponActionDelay()
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context);
            var actions = new ActorActionManager(manager);
            context.Client.Player.WeaponReady = false;
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            manager.RequestWeaponDraw(context.Client);
            var draw = context.World.Map.PerformRecovery.Single();
            Assert.AreEqual(ActionId.WeaponDraw, draw.ActionId);
            Assert.IsTrue(context.Client.Player.WeaponReady);
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            actions.DoWork(context.World.Map, 499);
            Assert.AreEqual(0, context.SaveAttempts);
            actions.DoWork(context.World.Map, 1);
            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));
            WorldTestContext.Drain(context.Client);
            manager.RequestWeaponStow(context.Client);
            manager.RequestWeaponStow(context.Client);
            var stow = context.World.Map.PerformRecovery.Single();
            actions.PerformRecovery(context.World.Map, draw);
            Assert.IsFalse(context.Client.Player.WeaponReady);
            actions.DoWork(context.World.Map, 500);
            actions.PerformRecovery(context.World.Map, stow);
            var packets = WorldTestContext.Drain(context.Client).Select(p => p.Message).OfType<CallMethodMessage>().Select(p => p.Packet).ToList();
            Assert.AreEqual(1, packets.OfType<PerformRecoveryPacket>().Count());
            Assert.AreEqual(6u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void ArmAwayAndBackInvalidatesTheOriginalReloadAndPublishesSelectedAmmo()
        {
            using var context = new WeaponAmmoContext();
            var other = context.AddWeapon(2, 1);
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);

            manager.RequestArmWeapon(context.Client, 1);
            Assert.AreSame(other, new InventoryManager(context).CurrentWeapon(context.Client));
            var selected = WorldTestContext.Drain(context.Client).Select(p => p.Message).OfType<CallMethodMessage>().ToList();
            Assert.AreEqual(2u, selected.Where(p => p.EntityId == other.EntityId).Select(p => p.Packet)
                .OfType<WeaponAmmoInfoPacket>().Single().AmmoInfo);
            manager.RequestArmWeapon(context.Client, 0);
            manager.WeaponReload(action);

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(30u, ammo.StackSize);
            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(context.Weapon.Id, new InventoryManager(context).CurrentWeapon(context.Relog()).Id);
        }

        [TestMethod]
        public void DrawerSwapUpdatesOwnershipAndActiveWeaponWithoutCompletingOldReload()
        {
            using var context = new WeaponAmmoContext();
            var other = context.AddWeapon(2, 1);
            context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            var inventory = new InventoryManager(context);

            inventory.WeaponDrawerInventory_MoveItem(context.Client, new WeaponDrawerInventory_MoveItemPacket { SrcSlot = 0, DestSlot = 1 });
            manager.WeaponReload(action);

            Assert.AreSame(other, inventory.CurrentWeapon(context.Client));
            Assert.AreEqual(0u, other.OwnerSlotId);
            Assert.AreEqual(1u, context.Weapon.OwnerSlotId);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0u, context.ReadInventory().Single(item => item.ItemId == other.Id).SlotId);
            Assert.AreEqual(other.Id, inventory.CurrentWeapon(context.Relog()).Id);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void FailedEquipOrArmLeavesSelectionInventoryAndPendingReloadUnchanged(bool equip)
        {
            using var context = new WeaponAmmoContext();
            if (equip)
                context.AddPersonalWeapon(2);
            else
                context.AddWeapon(2, 1);
            context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var pending = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);
            context.BeforeSave = _ => throw new DbUpdateException("Fixture equipment save failure.");

            if (equip)
                new InventoryManager(context).RequestEquipWeapon(context.Client, new RequestEquipWeaponPacket
                {
                    InventoryType = InventoryType.Personal, SrcSlot = 0, DestSlot = 0
                });
            else
                manager.RequestArmWeapon(context.Client, 1);

            Assert.AreSame(context.Weapon, new InventoryManager(context).CurrentWeapon(context.Client));
            Assert.AreSame(pending, context.World.Map.PerformRecovery.Single());
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            Assert.AreEqual(0u, context.ReadInventory().Single(item => item.ItemId == context.Weapon.Id).SlotId);
            context.BeforeSave = null;
            manager.WeaponReload(pending);
            Assert.AreEqual(20u, context.Weapon.CurrentAmmo);
        }

        [TestMethod]
        public void UnequippingCancelsReloadAndDoesNotLeaveAnEquippedEntity()
        {
            using var context = new WeaponAmmoContext();
            context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var pending = context.World.Map.PerformRecovery.Single();
            var inventory = new InventoryManager(context);

            inventory.RequestEquipWeapon(context.Client, new RequestEquipWeaponPacket
            {
                InventoryType = InventoryType.Personal, SrcSlot = 0, DestSlot = 0
            });
            manager.WeaponReload(pending);

            Assert.IsNull(inventory.CurrentWeapon(context.Client));
            Assert.AreEqual(0UL, context.Client.Player.Inventory.EquippedInventory[13]);
            Assert.IsFalse(context.Client.Player.WeaponReady);
            Assert.AreEqual(context.Weapon.EntityId, context.Client.Player.Inventory.PersonalInventory[0]);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        [DataRow(5u)]
        [DataRow(uint.MaxValue)]
        public void InvalidDrawerIndicesDoNotChangeSelectionOrPublishPackets(uint slot)
        {
            using var context = new WeaponAmmoContext();
            var inventory = new InventoryManager(context);
            var manager = new ManifestationManager(context);

            manager.RequestArmWeapon(context.Client, slot);
            inventory.RequestEquipWeapon(context.Client, new RequestEquipWeaponPacket
            {
                InventoryType = InventoryType.Personal, SrcSlot = 0, DestSlot = slot
            });
            inventory.WeaponDrawerInventory_MoveItem(context.Client, new WeaponDrawerInventory_MoveItemPacket { SrcSlot = slot, DestSlot = 0 });

            Assert.AreSame(context.Weapon, inventory.CurrentWeapon(context.Client));
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void MovingReserveToAnotherConsumableSlotDuringReloadUsesItsCurrentSlot()
        {
            using var context = new WeaponAmmoContext();
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            new InventoryManager(context).PersonalInventory_MoveItem(context.Client, new PersonalInventory_MoveItemPacket
            {
                SrcSlot = 50, DestSlot = 51
            });
            WorldTestContext.Drain(context.Client);

            manager.WeaponReload(action);

            Assert.AreEqual(20u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(17u, ammo.StackSize);
            Assert.AreEqual(51u, ammo.OwnerSlotId);
            Assert.AreEqual(51u, context.ReadInventory().Single(item => item.ItemId == ammo.Id).SlotId);
        }

        [TestMethod]
        public void ShotCannotOverwriteAChangedPersistedClip()
        {
            using var context = new WeaponAmmoContext();
            using (var db = context.Open())
                new Rasa.Repositories.Char.Items.ItemRepository(db).UpdateAmmo(new Item(0, 0, 0, 0)
                {
                    Id = context.Weapon.Id, CurrentAmmo = 3
                });

            Assert.IsFalse(new ManifestationManager(context).PlayerTryFireWeapon(context.Client));

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(3u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void InterruptedReloadIsDiscardedEvenWhenActorIsBusy()
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context);
            context.AddAmmo(30);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            context.Client.Player.CurrentAction = 123;
            ActorManager.Instance.RequestActionInterrupt(context.Client, new Rasa.Packets.MapChannel.Client.RequestActionInterruptPacket
            {
                ActionId = action.ActionId, ActionArgId = action.ActionArgId
            });
            WorldTestContext.Drain(context.Client);

            new ActorActionManager(manager).DoWork(context.World.Map, 0);

            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            Assert.AreEqual(123, context.Client.Player.CurrentAction);
        }

        [TestMethod]
        public void ReservesRemovedDuringReloadCannotProduceASuccessPacket()
        {
            using var context = new WeaponAmmoContext();
            context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            context.Client.Player.Inventory.PersonalInventory[50] = 0;
            WorldTestContext.Drain(context.Client);

            manager.WeaponReload(action);

            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        [DataRow(0u)]
        [DataRow(1u)]
        public void EquipPersistsBeforeChangingSlotsAndPublishesAmmoForTheCorrectWeapon(uint destination)
        {
            using var context = new WeaponAmmoContext();
            var incoming = context.AddPersonalWeapon(2);
            var inventory = new InventoryManager(context);
            context.BeforeSave = _ =>
            {
                Assert.AreEqual(incoming.EntityId, context.Client.Player.Inventory.PersonalInventory[0]);
                Assert.AreEqual(context.Weapon.EntityId, context.Client.Player.Inventory.EquippedInventory[13]);
                Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            };

            inventory.RequestEquipWeapon(context.Client, new RequestEquipWeaponPacket
            {
                InventoryType = InventoryType.Personal, SrcSlot = 0, DestSlot = destination
            });

            var expectedActive = destination == 0 ? incoming : context.Weapon;
            Assert.AreSame(expectedActive, inventory.CurrentWeapon(context.Client));
            Assert.AreEqual(destination, incoming.OwnerSlotId);
            Assert.AreEqual(incoming.EntityId, context.Client.Player.Inventory.WeaponDrawer[(int)destination]);
            var saved = context.ReadInventory().Single(slot => slot.ItemId == incoming.Id);
            Assert.AreEqual((uint)InventoryType.WeaponDrawerInventory, saved.InventoryType);
            Assert.AreEqual(destination, saved.SlotId);
            var packets = WorldTestContext.Drain(context.Client).Select(p => p.Message).OfType<CallMethodMessage>().ToList();
            Assert.IsTrue(packets.Any(p => p.EntityId == incoming.EntityId && p.Packet is WeaponAmmoInfoPacket info && info.AmmoInfo == 2));
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        public void FailureAtAnyReloadSaveRollsBackStacksSlotsAndClipAndRetryConsumesOnce(int failAt)
        {
            using var context = new WeaponAmmoContext();
            var first = context.AddAmmo(3);
            var second = context.AddAmmo(30, 51);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var failedAction = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);
            context.BeforeSave = _ =>
            {
                if (context.SaveAttempts == failAt)
                    throw new DbUpdateException("Fixture reload save failure.");
            };
            context.AfterSave = _ =>
            {
                if (failAt == 5 && context.SaveAttempts == 4)
                    throw new DbUpdateException("Fixture failure after the final write, before commit.");
            };

            manager.WeaponReload(failedAction);

            Assert.AreEqual(System.Math.Min(failAt, 4), context.SaveAttempts);
            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(3u, first.StackSize);
            Assert.AreEqual(30u, second.StackSize);
            Assert.AreEqual(first.EntityId, context.Client.Player.Inventory.PersonalInventory[50]);
            Assert.AreEqual(second.EntityId, context.Client.Player.Inventory.PersonalInventory[51]);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(3u, context.Read(first).StackSize);
            Assert.AreEqual(30u, context.Read(second).StackSize);
            Assert.AreEqual(3, context.ReadInventory().Count);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            context.BeforeSave = null;
            context.AfterSave = null;
            manager.RequestWeaponReload(context.Client, true);
            var retry = context.World.Map.PerformRecovery.Single();
            manager.WeaponReload(retry);
            manager.WeaponReload(failedAction);

            Assert.AreEqual(20u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0u, context.Read(first).StackSize);
            Assert.AreEqual(20u, context.Read(second).StackSize);
            Assert.AreEqual(2, context.ReadInventory().Count);
            var relogged = context.Relog();
            Assert.AreEqual(20u, new InventoryManager(context).CurrentWeapon(relogged).CurrentAmmo);
            Assert.AreEqual(0UL, relogged.Player.Inventory.PersonalInventory[50]);
            Assert.AreEqual(20u, EntityManager.Instance.GetItem(relogged.Player.Inventory.PersonalInventory[51]).StackSize);
        }

        [TestMethod]
        [DataRow(20u, 30u)]
        [DataRow(7u, 0u)]
        [DataRow(0u, 0u)]
        public void FullClipAndNoReserveDoNotScheduleOrChangeAmmo(uint clip, uint reserve)
        {
            using var context = new WeaponAmmoContext(clip);
            if (reserve > 0)
                context.AddAmmo(reserve);
            var manager = new ManifestationManager(context);

            manager.RequestWeaponReload(context.Client, true);
            manager.RequestWeaponReload(context.Client, true);

            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(clip, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void ReloadRechecksReserveRatherThanGrantingTheScheduledTotal()
        {
            using var context = new WeaponAmmoContext();
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            using (var db = context.Open())
                new Rasa.Repositories.Char.Items.ItemRepository(db).UpdateItemStackSize(
                    new Item(0, 2, 0, 0) { Id = ammo.Id });
            ammo.StackSize = 2;

            manager.WeaponReload(action);

            Assert.AreEqual(9u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(9u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0u, context.Read(ammo).StackSize);
        }

        [TestMethod]
        public void RelogRestoresActiveWeaponRatherThanLastDrawerItemAndPublishesItsSavedAmmo()
        {
            using var context = new WeaponAmmoContext();
            context.AddWeapon(2, 1);
            Assert.IsTrue(new ManifestationManager(context).PlayerTryFireWeapon(context.Client));

            var client = context.Relog();

            var weapon = new InventoryManager(context).CurrentWeapon(client);
            Assert.IsNotNull(weapon);
            Assert.AreEqual(context.Weapon.Id, weapon.Id);
            Assert.AreEqual(6u, weapon.CurrentAmmo);
            var packets = WorldTestContext.Drain(client).Select(p => p.Message).OfType<CallMethodMessage>().ToList();
            Assert.AreEqual(6u, packets.Where(p => p.EntityId == weapon.EntityId).Select(p => p.Packet)
                .OfType<WeaponAmmoInfoPacket>().Last().AmmoInfo);
            Assert.IsTrue(packets.FindIndex(p => p.Packet is WeaponDrawerSlotPacket) >
                packets.FindIndex(p => p.EntityId == weapon.EntityId && p.Packet is WeaponAmmoInfoPacket));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void StaleOrCrossMapTargetIsRejectedBeforeSpendingAmmo(bool stale)
        {
            using var context = new WeaponAmmoContext();
            using var otherWorld = new WorldTestContext();
            var target = otherWorld.CreateClient();
            CellManager.Instance.AddToWorld(target);
            context.Client.Player.Target = stale ? ulong.MaxValue : target.Player.EntityId;
            var manager = new ManifestationManager(context);

            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            MissileManager.Instance.MissileLaunch(context.World.Map,
                new ActionData(context.Client.Player, ActionId.WeaponAttack, 133, context.Client.Player.Target, 0), 55);

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, context.World.Map.QueuedMissiles.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void TargetLeavingAfterLaunchCannotBeDamagedOnAnotherMap()
        {
            using var context = new WeaponAmmoContext();
            using var otherWorld = new WorldTestContext();
            var target = context.World.CreateClient();
            target.Player.Attributes[Attributes.Health] = new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
            target.Player.Attributes[Attributes.Armor] = new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0);
            CellManager.Instance.AddToWorld(target);
            context.Client.Player.Target = target.Player.EntityId;
            Assert.IsTrue(new ManifestationManager(context).PlayerTryFireWeapon(context.Client));
            CellManager.Instance.RemoveFromWorld(target);
            target.Player.MapChannel = otherWorld.Map;
            otherWorld.Map.ClientList.Add(target);
            CellManager.Instance.AddToWorld(target);

            MissileManager.Instance.DoWork(context.World.Map, 1000);

            Assert.AreEqual(100, target.Player.Attributes[Attributes.Health].Current);
        }

        [TestMethod]
        [DataRow("loading")]
        [DataRow("dead")]
        [DataRow("dying")]
        [DataRow("zeroHealth")]
        [DataRow("foreign")]
        [DataRow("missingClass")]
        [DataRow("missingWeaponData")]
        [DataRow("missingItem")]
        [DataRow("missingMapMembership")]
        public void InvalidWeaponCommandsDoNotPersistScheduleOrPublish(string invalidation)
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context);
            var client = context.Client;
            switch (invalidation)
            {
                case "loading": client.State = ClientState.Loading; break;
                case "dead": client.Player.State = CharacterState.Dead; break;
                case "dying": client.Player.State = CharacterState.Dying; break;
                case "zeroHealth": client.Player.Attributes[Attributes.Health].Current = 0; break;
                case "foreign": context.Weapon.OwnerId = 99; break;
                case "missingClass": EntityClassManager.Instance.LoadedEntityClasses.Remove(context.Weapon.ItemTemplate.Class); break;
                case "missingWeaponData": context.Weapon.ItemTemplate.WeaponInfo = null; break;
                case "missingItem": EntityManager.Instance.Items.Remove(context.Weapon.EntityId); break;
                case "missingMapMembership": context.World.Map.ClientList.Remove(client); break;
            }

            Assert.IsFalse(manager.PlayerTryFireWeapon(client));
            manager.RequestWeaponDraw(client);
            manager.RequestWeaponStow(client);
            manager.RequestWeaponReload(client, true);
            manager.RequestArmWeapon(client, 0);
            manager.RegisterAutoFire(client);
            manager.StartAutoFire(client, 0);

            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
        }

        [TestMethod]
        public void ManualAndDuplicateAutoFireShareTheTemplateRefireDeadline()
        {
            using var context = new WeaponAmmoContext();
            long now = 10000;
            var manager = new ManifestationManager(context, null, () => now);
            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            manager.RegisterAutoFire(context.Client);
            manager.RegisterAutoFire(context.Client);
            manager.AutoFireKeepAlive(context.Client, 1000);
            now += 799;
            manager.AutoFireTimerDoWork(800);
            Assert.AreEqual(6u, context.Weapon.CurrentAmmo);
            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));
            now++;
            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));
            manager.AutoFireTimerDoWork(800);
            Assert.AreEqual(5u, context.Weapon.CurrentAmmo);
            now += 800;
            manager.AutoFireTimerDoWork(800);
            Assert.AreEqual(4u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(4u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(3, context.World.Map.QueuedMissiles.Count);
        }

        [TestMethod]
        public void DuplicateReloadRequestsAndRecoveryCannotConsumeTwice()
        {
            using var context = new WeaponAmmoContext();
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);
            manager.WeaponReload(action);
            var saves = context.SaveAttempts;
            WorldTestContext.Drain(context.Client);

            manager.WeaponReload(action);

            Assert.AreEqual(20u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(17u, context.Read(ammo).StackSize);
            Assert.AreEqual(saves, context.SaveAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
        }

        [TestMethod]
        [DataRow("interrupt")]
        [DataRow("switch")]
        [DataRow("remove")]
        [DataRow("leave")]
        [DataRow("leaveReturn")]
        [DataRow("dead")]
        public void InvalidatedReloadCannotConsumeReserves(string invalidation)
        {
            using var context = new WeaponAmmoContext();
            var ammo = context.AddAmmo(30);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);
            switch (invalidation)
            {
                case "interrupt": action.IsInrerrupted = true; break;
                case "switch":
                    var other = context.AddWeapon(2, 1);
                    context.Client.Player.ActiveWeapon = 1;
                    context.Client.Player.Inventory.EquippedInventory[13] = other.EntityId;
                    break;
                case "remove": context.Client.Player.Inventory.WeaponDrawer[0] = 0; break;
                case "leave": CellManager.Instance.RemoveFromWorld(context.Client); break;
                case "leaveReturn":
                    CellManager.Instance.RemoveFromWorld(context.Client);
                    CellManager.Instance.AddToWorld(context.Client);
                    break;
                case "dead": context.Client.Player.State = CharacterState.Dead; break;
            }
            WorldTestContext.Drain(context.Client);

            manager.WeaponReload(action);

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(30u, ammo.StackSize);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(30u, context.Read(ammo).StackSize);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void ReloadAccumulatesStacksOnTopOfLoadedClipAndCommitsBeforePackets()
        {
            using var context = new WeaponAmmoContext();
            var first = context.AddAmmo(3);
            var second = context.AddAmmo(4, 51);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            Assert.AreEqual(1500L, action.WaitTime);
            Assert.AreEqual(14u, action.Args);
            WorldTestContext.Drain(context.Client);
            context.BeforeSave = db =>
            {
                Assert.IsNotNull(db.Database.CurrentTransaction);
                Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
                Assert.AreEqual(3u, first.StackSize);
                Assert.AreEqual(4u, second.StackSize);
                Assert.AreEqual(first.EntityId, context.Client.Player.Inventory.PersonalInventory[50]);
                Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            };

            manager.WeaponReload(action);

            Assert.AreEqual(14u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(14u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0u, context.Read(first).StackSize);
            Assert.AreEqual(0u, context.Read(second).StackSize);
            Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[50]);
            Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[51]);
            Assert.AreEqual(1, context.ReadInventory().Count);
            var packets = WorldTestContext.Drain(context.Client).Select(p => p.Message)
                .OfType<CallMethodMessage>().Select(p => p.Packet).ToList();
            Assert.AreEqual(14u, packets.OfType<WeaponAmmoInfoPacket>().Single().AmmoInfo);
            Assert.AreEqual(14u, packets.OfType<PerformRecoveryPacket>().Single().Arg);
            Assert.IsTrue(packets.FindIndex(p => p is WeaponAmmoInfoPacket) <
                packets.FindIndex(p => p is PerformRecoveryPacket));
        }

        [TestMethod]
        public void ShotSavesBeforePublishingAndFailedSaveLeavesClipUnchanged()
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context);
            context.BeforeSave = _ =>
            {
                Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
                Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
                throw new DbUpdateException("Fixture shot save failure.");
            };

            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0, context.World.Map.QueuedMissiles.Count);
            Assert.IsFalse(WorldTestContext.Drain(context.Client).Select(p => p.Message)
                .OfType<CallMethodMessage>().Any(p => p.Packet is WeaponAmmoInfoPacket));
            context.BeforeSave = null;

            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));

            Assert.AreEqual(6u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(6u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(1, context.World.Map.QueuedMissiles.Count);
            Assert.AreEqual(6u, WorldTestContext.Drain(context.Client).Select(p => p.Message)
                .OfType<CallMethodMessage>().Select(p => p.Packet).OfType<WeaponAmmoInfoPacket>().Single().AmmoInfo);
        }
    }
}
