using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.LootDispenser.Server;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public partial class LootTests
    {
        [TestMethod]
        public void ConsumedLootIsDetachedAndDuplicateClaimsPublishNothing()
        {
            using var context = new LootContext();
            var manager = new LootDispenserManager(context.Storage);
            var request = new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId };
            Assert.IsTrue(manager.RequestLootAllFromCorpse(context.Client, request));
            var packets = context.Drain();

            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.AreEqual(0UL, context.Corpse.LootDispenserObjectEntityId);
            Assert.AreEqual(0, context.Loot.LootItems.Count);
            Assert.AreEqual(0, context.Loot.Credits);
            Assert.AreEqual(1, packets.OfType<Rasa.Packets.MapChannel.Server.DestroyPhysicalEntityPacket>().Count());
            Assert.IsFalse(manager.RequestLootAllFromCorpse(context.Client, request));
            Assert.AreEqual(0, context.Drain().Count);
            Assert.AreEqual(107, context.ReadCredits());
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void CorpseRemovalOrOwnerDepartureReclaimsUnclaimedLoot(bool departure)
        {
            using var context = new LootContext();
            if (departure)
            {
                CellManager.Instance.RemoveFromWorld(context.Client);
                CellManager.Instance.AddToWorld(context.Client);
            }
            else
                CellManager.Instance.RemoveCreatureFromWorld(context.Map, context.Corpse);
            context.Drain();

            Assert.AreEqual(0, context.Map.LootDispensers.Count);
            Assert.AreEqual(0, context.Loot.LootItems.Count);
            Assert.AreEqual(0UL, context.Corpse.LootDispenserObjectEntityId);
            Assert.IsFalse(new LootDispenserManager(context.Storage).RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId }));
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void MixedLootCommitsInventoryAndCreditsBeforePublishingTheGrant()
        {
            using var context = new LootContext();
            var manager = new LootDispenserManager(context.Storage);
            context.Storage.BeforeSave = _ =>
            {
                Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
                Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[50]);
                Assert.IsFalse(context.Loot.FullyLooted);
                Assert.AreEqual(3u, context.Loot.LootItems.Single().ItemQuantity);
                Assert.AreEqual(0, context.Drain().Count);
            };

            manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.AreEqual(107, context.ReadCredits());
            Assert.AreEqual(107, context.Client.Player.Credits[CurencyType.Credits]);
            var item = EntityManager.Instance.GetItem(context.Client.Player.Inventory.PersonalInventory[50]);
            Assert.IsNotNull(item);
            Assert.AreEqual(3u, context.Storage.Read(item).StackSize);
            Assert.AreEqual(42u, context.Storage.ReadInventory().Single(row => row.ItemId == item.Id).CharacterId);
            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.IsFalse(context.Loot.IsLootable);
        }

        [TestMethod]
        [DataRow(2f, 0f, 0f, true)]
        [DataRow(0f, 2f, 0f, true)]
        [DataRow(0f, 0f, -2f, true)]
        [DataRow(2.001f, 0f, 0f, false)]
        [DataRow(0f, 2.001f, 0f, false)]
        [DataRow(1.5f, 0f, 1.5f, false)]
        [DataRow(float.NaN, 0f, 0f, false)]
        [DataRow(0f, float.PositiveInfinity, 0f, false)]
        [DataRow(0f, 0f, float.NegativeInfinity, false)]
        public void OpeningUsesFiniteThreeDimensionalTwoMetreDistance(float x, float y, float z, bool allowed)
        {
            using var context = new LootContext();
            context.Corpse.Position = new Vector3(x, y, z);

            Assert.AreEqual(allowed, LootDispenserManager.Instance.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId }));
            Assert.AreEqual(allowed ? 4 : 0, context.Drain().Count);
            Assert.AreEqual(100, context.ReadCredits());
        }

        [TestMethod]
        public void OwnerCanRefreshKnownCorpseMetadataWithoutClaimingAnything()
        {
            using var context = new LootContext();

            LootDispenserManager.Instance.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId });

            var packets = context.Drain();
            CollectionAssert.AreEqual(new[]
            {
                typeof(AttachInfoPacket), typeof(LootInfoPacket), typeof(OverallQualityPacket), typeof(CanLootItemsPacket)
            }, packets.Select(packet => packet.GetType()).ToArray());
            Assert.AreEqual(100, context.ReadCredits());
            Assert.AreEqual(0, context.Storage.SaveAttempts);
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(3u, context.Loot.LootItems.Single().ItemQuantity);
        }
    }
}
