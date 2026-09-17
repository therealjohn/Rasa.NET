using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Inventory.Client;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Repositories;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class GameplayPersistenceTests
    {
        [TestMethod]
        [DataRow("loot", InventoryType.Personal, 50u)]
        [DataRow("fire", InventoryType.WeaponDrawerInventory, 0u)]
        [DataRow("reload", InventoryType.WeaponDrawerInventory, 0u)]
        [DataRow("reload", InventoryType.Personal, 50u)]
        public void DuplicateDurableSlotsRejectWithoutGrantsAndAllowRetryAfterRepair(
            string operation, InventoryType type, uint slot)
        {
            using var context = new LootContext();
            var reserve = context.AddAmmo(30);
            var incoming = context.Storage.AddPersonalWeapon(2);
            incoming.ItemTemplate.InventoryCategory = (InventoryCategory)1;
            var duplicate = context.AddAmmo(5, 51);
            using (var database = context.Storage.Open())
            {
                var row = database.CharacterInventoryEntries.Single(entry => entry.ItemId == duplicate.Id);
                row.InventoryType = (uint)type;
                row.SlotId = slot;
                database.SaveChanges();
            }
            var manager = new ManifestationManager(context.Storage);
            var request = Prepare(context, manager, operation);
            context.Drain();
            var saves = context.Storage.SaveAttempts;

            request();

            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            AssertUnchanged(context, reserve, incoming);
            Assert.AreEqual(5u, context.Storage.Read(duplicate).StackSize);
            using (var database = context.Storage.Open())
            {
                var row = database.CharacterInventoryEntries.Single(entry => entry.ItemId == duplicate.Id);
                row.InventoryType = (uint)InventoryType.Personal;
                row.SlotId = 51;
                database.SaveChanges();
            }
            Prepare(context, manager, operation)();
            Assert.IsTrue(context.Drain().Count > 0);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ConnectionLossDuringAnUnrelatedFailureDoesNotMaskTheOriginalError(bool invalidOperation)
        {
            using var context = new LootContext();
            var reserve = context.AddAmmo(30);
            Exception injected = invalidOperation
                ? new InvalidOperationException("Original application error.")
                : new NullReferenceException("Original application error.");
            context.Storage.AfterSave = database =>
            {
                database.Database.GetDbConnection().Close();
                ThrowAtPersistenceBoundary(injected);
            };
            var request = Prepare(context, new ManifestationManager(context.Storage), "loot");

            var actual = invalidOperation
                ? Assert.ThrowsExactly<InvalidOperationException>(request)
                : (Exception)Assert.ThrowsExactly<NullReferenceException>(request);

            Assert.AreSame(injected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            Assert.AreEqual(30u, context.Storage.Read(reserve).StackSize);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(0, context.Drain().Count);
            context.Storage.AfterSave = null;
            request();
            Assert.AreEqual(33u, context.Storage.Read(reserve).StackSize);
            Assert.AreEqual(107, context.ReadCredits());
        }

        [TestMethod]
        [DataRow("loot", false, false)]
        [DataRow("loot", true, false)]
        [DataRow("loot", false, true)]
        [DataRow("loot", true, true)]
        [DataRow("fire", false, false)]
        [DataRow("fire", true, false)]
        [DataRow("fire", false, true)]
        [DataRow("fire", true, true)]
        [DataRow("reload", false, false)]
        [DataRow("reload", true, false)]
        [DataRow("reload", false, true)]
        [DataRow("reload", true, true)]
        [DataRow("arm", false, false)]
        [DataRow("arm", true, false)]
        [DataRow("arm", false, true)]
        [DataRow("arm", true, true)]
        [DataRow("equip", false, false)]
        [DataRow("equip", true, false)]
        [DataRow("equip", false, true)]
        [DataRow("equip", true, true)]
        [DataRow("select", false, true)]
        [DataRow("select", true, true)]
        [DataRow("set", false, true)]
        [DataRow("set", true, true)]
        [DataRow("swap", false, true)]
        [DataRow("swap", true, true)]
        [DataRow("train", false, true)]
        [DataRow("train", true, true)]
        public void UnrelatedQueryAndSaveFailuresPropagateWithIdentityAndStack(string operation, bool invalidOperation, bool query)
        {
            using var context = new LootContext();
            var reserve = context.AddAmmo(30);
            var incoming = context.Storage.AddPersonalWeapon(2);
            incoming.ItemTemplate.InventoryCategory = (InventoryCategory)1;
            var manager = new ManifestationManager(context.Storage);
            var request = Prepare(context, manager, operation);
            context.Drain();
            Exception injected = invalidOperation
                ? new InvalidOperationException("Unrelated persistence bug.")
                : new NullReferenceException("Unrelated persistence bug.");
            if (query)
                context.Storage.BeforeQuery = () => ThrowAtPersistenceBoundary(injected);
            else
                context.Storage.AfterSave = _ => ThrowAtPersistenceBoundary(injected);

            var actual = invalidOperation
                ? Assert.ThrowsExactly<InvalidOperationException>(request)
                : (Exception)Assert.ThrowsExactly<NullReferenceException>(request);

            Assert.AreSame(injected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            context.Storage.BeforeQuery = null;
            context.Storage.AfterSave = null;
            AssertUnchanged(context, reserve, incoming);
            Prepare(context, manager, operation)();
            Assert.IsTrue(context.Drain().Count > 0);
        }

        [TestMethod]
        [DataRow("loot", "database")]
        [DataRow("loot", "update")]
        [DataRow("loot", "missing")]
        [DataRow("loot", "overflow")]
        [DataRow("loot", "capability")]
        [DataRow("fire", "database")]
        [DataRow("fire", "update")]
        [DataRow("fire", "missing")]
        [DataRow("fire", "overflow")]
        [DataRow("fire", "capability")]
        [DataRow("select", "database")]
        [DataRow("select", "update")]
        [DataRow("select", "missing")]
        [DataRow("select", "overflow")]
        [DataRow("select", "capability")]
        public void SupportedPersistenceFailuresRejectAndAllowRetry(string operation, string failure)
        {
            using var context = new LootContext();
            var reserve = context.AddAmmo(30);
            var incoming = context.Storage.AddPersonalWeapon(2);
            incoming.ItemTemplate.InventoryCategory = (InventoryCategory)1;
            var manager = new ManifestationManager(context.Storage);
            Exception injected = failure switch
            {
                "database" => new SqliteException("Injected provider failure.", 5),
                "update" => new DbUpdateException("Injected update failure."),
                "missing" => new EntityNotFoundException("CharacterEntry", "Id", context.Client.Player.Id),
                "overflow" => new OverflowException("Injected checked arithmetic failure."),
                "capability" => new NotSupportedException("Injected unavailable transaction capability."),
                _ => throw new AssertFailedException($"Unknown failure {failure}.")
            };
            var request = Prepare(context, manager, operation);
            context.Drain();
            context.Storage.AfterSave = _ => throw injected;

            request();

            context.Storage.AfterSave = null;
            AssertUnchanged(context, reserve, incoming);
            request();
            Assert.IsTrue(context.Drain().Count > 0);
        }

        private static void ThrowAtPersistenceBoundary(Exception error) => throw error;

        private static Action Prepare(LootContext context, ManifestationManager manager, string operation)
        {
            switch (operation)
            {
                case "loot": return () => new LootDispenserManager(context.Storage).RequestLootAllFromCorpse(
                    context.Client, new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });
                case "fire": return () => manager.PlayerTryFireWeapon(context.Client);
                case "reload":
                    manager.RequestWeaponReload(context.Client, true);
                    var action = context.Map.PerformRecovery.Single();
                    return () => manager.WeaponReload(action);
                case "arm": return () => manager.RequestArmWeapon(context.Client, 0);
                case "equip": return () => new InventoryManager(context.Storage).RequestEquipWeapon(context.Client,
                    new RequestEquipWeaponPacket { InventoryType = InventoryType.Personal, SrcSlot = 0, DestSlot = 0 });
                case "select": return () => manager.RequestArmAbility(context.Client, 24);
                case "set": return () => manager.RequestSetAbilitySlot(context.Client,
                    new Rasa.Packets.MapChannel.Client.RequestSetAbilitySlotPacket { SlotId = 24, AbilityId = 0, AbilityLevel = 0 });
                case "swap": return () => manager.RequestSwapAbilitySlots(context.Client,
                    new Rasa.Packets.MapChannel.Client.RequestSwapAbilitySlotsPacket { FromSlot = 0, ToSlot = 24 });
                case "train": return () => manager.LevelSkills(context.Client,
                    new Rasa.Packets.MapChannel.Client.LevelSkillsPacket
                    { ListLenght = 1, SkillIds = new[] { 165 }, SkillLevels = new[] { 1 } });
                default: throw new AssertFailedException($"Unknown operation {operation}.");
            }
        }

        private static void AssertUnchanged(LootContext context, Item reserve, Item incoming)
        {
            Assert.AreEqual(0, context.Drain().Count);
            Assert.AreEqual(7u, context.Storage.Weapon.CurrentAmmo);
            Assert.AreEqual(7u, context.Storage.Read(context.Storage.Weapon).AmmoCount);
            Assert.AreEqual(30u, reserve.StackSize);
            Assert.AreEqual(30u, context.Storage.Read(reserve).StackSize);
            Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(incoming.EntityId, context.Client.Player.Inventory.PersonalInventory[0]);
            Assert.AreSame(context.Storage.Weapon, new InventoryManager(context.Storage).CurrentWeapon(context.Client));
            Assert.AreEqual(0, context.Client.Player.CurrentAbilityDrawer);
            Assert.AreEqual(0, context.Client.Player.Abilities.Count);
            Assert.AreEqual(0, context.Client.Player.Skills.Count);
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.IsTrue(context.Loot.IsLootable);
        }
    }
}
