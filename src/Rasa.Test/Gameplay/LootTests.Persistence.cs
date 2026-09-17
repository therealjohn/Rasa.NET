using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Structures;

    public partial class LootTests
    {
        private static bool Claim(LootContext context) => new LootDispenserManager(context.Storage)
            .RequestLootAllFromCorpse(context.Client, new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

        [TestMethod]
        [DataRow(2u, true)]
        [DataRow(51u, false)]
        public void EachTemplateUsesItsCategoryAndAFullCategoryRejectsTheWholeBatch(uint weapons, bool fits)
        {
            using var context = new LootContext();
            // ItemTemplatePreloader: 145 -> equipment category 1; 28 -> consumable category 2.
            context.Storage.Weapon.ItemTemplate.InventoryCategory = (InventoryCategory)1;
            context.Loot.LootItems.Add(new LootItem(145, 6048, weapons, context.Client.Player.EntityId, 0));
            Assert.AreEqual(fits, Claim(context));
            if (fits)
            {
                var inventory = context.Client.Player.Inventory.PersonalInventory;
                Assert.AreEqual(145u, context.Storage.Read(EntityManager.Instance.GetItem(inventory[0])).ItemTemplateId);
                Assert.AreEqual(145u, context.Storage.Read(EntityManager.Instance.GetItem(inventory[1])).ItemTemplateId);
                Assert.AreEqual(1u, context.Storage.Read(EntityManager.Instance.GetItem(inventory[0])).StackSize);
                Assert.AreEqual(28u, context.Storage.Read(EntityManager.Instance.GetItem(inventory[50])).ItemTemplateId);
                Assert.AreEqual(107, context.ReadCredits());
            }
            else
            {
                Assert.AreEqual(100, context.ReadCredits());
                Assert.AreEqual(0, context.Storage.SaveAttempts);
                Assert.AreEqual(2, context.Loot.LootItems.Count);
                Assert.AreEqual(0, context.Drain().Count);
                Assert.IsTrue(context.Client.Player.Inventory.PersonalInventory.All(id => id == 0));
            }
        }

        [TestMethod]
        public void StackMergingSplittingAndRepeatedTemplatesRetainEveryItemOnRelog()
        {
            using var context = new LootContext(50003);
            var existing = context.AddAmmo(49998);
            context.Loot.LootItems.Add(new LootItem(28, 3147, 2, context.Client.Player.EntityId, 0));

            Assert.IsTrue(Claim(context));
            Assert.AreEqual(50000u, context.Storage.Read(existing).StackSize);
            var slots = context.Client.Player.Inventory.PersonalInventory.Skip(50).Take(3).ToArray();
            CollectionAssert.AreEqual(new[] { 50000u, 50000u, 3u },
                slots.Select(id => context.Storage.Read(EntityManager.Instance.GetItem(id)).StackSize).ToArray());
            var relog = context.Storage.Relog();
            CollectionAssert.AreEqual(new[] { 50000u, 50000u, 3u },
                relog.Player.Inventory.PersonalInventory.Skip(50).Take(3)
                    .Select(id => EntityManager.Instance.GetItem(id).StackSize).ToArray());
            Assert.AreEqual(107, context.ReadCredits());
        }

        [TestMethod]
        public void FullCategoryRejectsTheEntireBatchAndCreditsUntilItAllFits()
        {
            using var context = new LootContext(50001);
            for (uint slot = 50; slot < 99; slot++)
                context.AddAmmo(50000, slot);
            var originalRows = context.Storage.ReadInventory().Count;

            Assert.IsFalse(Claim(context));
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(originalRows, context.Storage.ReadInventory().Count);
            Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[99]);
            Assert.AreEqual(0, context.Drain().Count);
            Assert.AreEqual(50001u, context.Loot.LootItems.Single().ItemQuantity);

            var item = EntityManager.Instance.GetItem(context.Client.Player.Inventory.PersonalInventory[50]);
            using (var database = context.Storage.Open())
            {
                var row = database.ItemEntries.Single(row => row.ItemId == item.Id);
                row.StackSize = 49999;
                database.SaveChanges();
            }
            item.StackSize = 49999;
            Assert.IsTrue(Claim(context));
            Assert.AreEqual(50000u, context.Storage.Read(item).StackSize);
            Assert.AreEqual(50000u, context.Storage.Read(EntityManager.Instance.GetItem(
                context.Client.Player.Inventory.PersonalInventory[99])).StackSize);
            Assert.AreEqual(107, context.ReadCredits());
        }

        [TestMethod]
        [DataRow(1, false)]
        [DataRow(2, false)]
        [DataRow(3, false)]
        [DataRow(4, false)]
        [DataRow(1, true)]
        [DataRow(2, true)]
        [DataRow(3, true)]
        [DataRow(4, true)]
        public void FailureAtEverySaveRollsBackItemsSlotsStacksAndCreditsAndAllowsOneRetry(int stage, bool after)
        {
            using var context = new LootContext(5);
            var existing = context.AddAmmo(49998);
            var entities = EntityManager.Instance.RegisteredEntities.Keys.ToArray();
            var items = EntityManager.Instance.Items.Keys.ToArray();
            var rows = context.Storage.ReadInventory().Count;
            Action<Rasa.Context.Char.SqliteCharContext> fail = _ =>
            {
                Assert.AreEqual(49998u, existing.StackSize);
                Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
                Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[51]);
                Assert.IsFalse(context.Loot.FullyLooted);
                Assert.AreEqual(0, context.Drain().Count);
                if (context.Storage.SaveAttempts == stage)
                    throw new DbUpdateException($"Injected {(after ? "after" : "before")} save {stage}.");
            };
            if (after)
                context.Storage.AfterSave = fail;
            else
                context.Storage.BeforeSave = fail;

            Assert.IsFalse(Claim(context));
            Assert.AreEqual(stage, context.Storage.SaveAttempts);
            Assert.AreEqual(49998u, context.Storage.Read(existing).StackSize);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(rows, context.Storage.ReadInventory().Count);
            using (var database = context.Storage.Open())
                Assert.AreEqual(2, database.ItemEntries.Count());
            CollectionAssert.AreEquivalent(entities, EntityManager.Instance.RegisteredEntities.Keys.ToArray());
            CollectionAssert.AreEquivalent(items, EntityManager.Instance.Items.Keys.ToArray());
            Assert.AreEqual(5u, context.Loot.LootItems.Single().ItemQuantity);
            Assert.AreEqual(7, context.Loot.Credits);
            Assert.IsTrue(context.Loot.IsLootable);
            Assert.AreEqual(0, context.Drain().Count);

            context.Storage.BeforeSave = null;
            context.Storage.AfterSave = null;
            Assert.IsTrue(Claim(context));
            Assert.IsFalse(Claim(context));
            Assert.AreEqual(50000u, context.Storage.Read(existing).StackSize);
            Assert.AreEqual(3u, context.Storage.Read(EntityManager.Instance.GetItem(
                context.Client.Player.Inventory.PersonalInventory[51])).StackSize);
            Assert.AreEqual(107, context.ReadCredits());
            Assert.AreEqual(rows + 1, context.Storage.ReadInventory().Count);
        }

        [TestMethod]
        public void ConnectionLossBeforeCommitDoesNotPublishAndCanRetryFromReopenedStorage()
        {
            using var context = new LootContext(5);
            var existing = context.AddAmmo(49998);
            context.Storage.AfterSave = database =>
            {
                if (context.Storage.SaveAttempts == 4)
                    database.Database.GetDbConnection().Close();
            };

            Assert.IsFalse(Claim(context));
            Assert.AreEqual(4, context.Storage.SaveAttempts);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(49998u, context.Storage.Read(existing).StackSize);
            Assert.AreEqual(2, context.Storage.ReadInventory().Count);
            Assert.AreEqual(0, context.Drain().Count);
            Assert.IsFalse(context.Loot.FullyLooted);
            context.Storage.AfterSave = null;
            Assert.IsTrue(Claim(context));
            Assert.AreEqual(107, context.ReadCredits());
        }

        [TestMethod]
        [DataRow("CreditsChanged")]
        [DataRow("CharacterOwner")]
        [DataRow("CharacterMissing")]
        [DataRow("EmptySlotOccupied")]
        [DataRow("StackChanged")]
        [DataRow("TemplateChanged")]
        [DataRow("SlotMoved")]
        [DataRow("ForeignOwner")]
        [DataRow("WrongRuntimeSlot")]
        [DataRow("DuplicateRuntimeItem")]
        [DataRow("OverMaximum")]
        [DataRow("MissingRuntimeItem")]
        [DataRow("MissingDurableItem")]
        [DataRow("WrongRuntimeCategory")]
        public void StaleDurableOrRuntimeInventoryIdentityFailsBeforeAnyWrites(string mismatch)
        {
            using var context = new LootContext();
            var item = context.AddAmmo(4);
            using (var database = context.Storage.Open())
            {
                var character = database.CharacterEntries.Single();
                var entry = database.ItemEntries.Single(row => row.ItemId == item.Id);
                var slot = database.CharacterInventoryEntries.Single(row => row.ItemId == item.Id);
                switch (mismatch)
                {
                    case "CreditsChanged": character.Credit = 200; break;
                    case "CharacterOwner": context.Client.AccountEntry.Id = 99; break;
                    case "CharacterMissing": database.CharacterEntries.Remove(character); break;
                    case "EmptySlotOccupied": context.Client.Player.Inventory.PersonalInventory[50] = 0; break;
                    case "StackChanged": entry.StackSize = 9; break;
                    case "TemplateChanged": entry.ItemTemplateId = 145; break;
                    case "SlotMoved": slot.SlotId = 51; break;
                    case "ForeignOwner": item.OwnerId = 99; break;
                    case "WrongRuntimeSlot": item.OwnerSlotId = 51; break;
                    case "DuplicateRuntimeItem": context.Client.Player.Inventory.PersonalInventory[51] = item.EntityId; break;
                    case "OverMaximum": item.StackSize = entry.StackSize = 50001; break;
                    case "MissingRuntimeItem": EntityManager.Instance.UnregisterItem(item.EntityId); break;
                    case "MissingDurableItem": database.ItemEntries.Remove(entry); break;
                    case "WrongRuntimeCategory": item.ItemTemplate.InventoryCategory = (InventoryCategory)1; break;
                }
                database.SaveChanges();
            }
            var before = context.Storage.ReadInventory().Select(row => (row.ItemId, row.SlotId)).ToArray();

            Assert.IsFalse(Claim(context));
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            CollectionAssert.AreEqual(before, context.Storage.ReadInventory().Select(row => (row.ItemId, row.SlotId)).ToArray());
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(3u, context.Loot.LootItems.Single().ItemQuantity);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        [DataRow("CreditOverflow")]
        [DataRow("NegativeCredits")]
        [DataRow("UnknownTemplate")]
        [DataRow("WrongClass")]
        [DataRow("InvalidCategory")]
        [DataRow("ZeroMaximum")]
        [DataRow("ZeroQuantity")]
        [DataRow("ForeignItemOwner")]
        [DataRow("PartyItem")]
        [DataRow("DuplicateLootEntry")]
        [DataRow("MismatchedTemplateIdentity")]
        [DataRow("MissingItemInfo")]
        public void InvalidLootDataNeverGrantsPartialItemsOrCredits(string invalid)
        {
            using var context = new LootContext();
            var loot = context.Loot.LootItems.Single();
            switch (invalid)
            {
                case "CreditOverflow": context.Loot.Credits = int.MaxValue; break;
                case "NegativeCredits": context.Loot.Credits = -1; break;
                case "UnknownTemplate": loot.ItemTemplateId = uint.MaxValue; break;
                case "WrongClass": loot.ItemClassId = 6048; break;
                case "InvalidCategory": ItemManager.Instance.GetItemTemplateById(28).InventoryCategory = 0; break;
                case "ZeroMaximum": EntityClassManager.Instance.LoadedEntityClasses[(EntityClasses)3147].ItemClassInfo.StackSize = 0; break;
                case "ZeroQuantity": loot.ItemQuantity = 0; break;
                case "ForeignItemOwner": loot.ActorId = 99; break;
                case "PartyItem": loot.PartyId = 1; break;
                case "DuplicateLootEntry": context.Loot.LootItems.Add(loot); break;
                case "MismatchedTemplateIdentity": ItemManager.Instance.GetItemTemplateById(28).ItemTemplateId = 145; break;
                case "MissingItemInfo": ItemManager.Instance.GetItemTemplateById(28).ItemInfo = null; break;
            }

            Assert.IsFalse(Claim(context));
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(0, context.Drain().Count);
            Assert.IsFalse(context.Loot.FullyLooted);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(7)]
        public void EmptyAndCreditOnlyCorpsesHaveOneTerminalConsumption(int credits)
        {
            using var context = new LootContext(0, credits);
            Assert.IsTrue(Claim(context));
            Assert.IsFalse(Claim(context));
            Assert.AreEqual(100 + credits, context.ReadCredits());
            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.AreEqual(1, context.Drain().OfType<Rasa.Packets.ClientMethod.Server.GotLootPacket>().Count());
        }
    }
}
