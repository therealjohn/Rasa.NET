using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.LootDispenser.Server;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class LootConsolidationTests
    {
        [TestMethod]
        [DataRow(2f, true)]
        [DataRow(2.001f, false)]
        [DataRow(float.NaN, false)]
        public void ManualCorpseOpeningUsesFiniteThreeDimensionalConfiguredDistance(float distance, bool allowed)
        {
            using var context = new LootFixture();
            context.Corpse.Position = new Vector3(distance, 0, 0);

            context.Manager.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId });

            var packets = context.Drain();
            Assert.AreEqual(allowed ? 1 : 0, packets.OfType<LootCorpsePacket>().Count());
            Assert.AreEqual(allowed ? context.Client.Player.EntityId : 0UL, context.Loot.CurrentLooter);
        }

        [TestMethod]
        public void ClaimRevalidatesRangeAndCommitsItemsAndCreditsBeforePublication()
        {
            using var context = new LootFixture();
            context.Manager.RequestCorpseLooting(context.Client,
                new RequestCorpseLootingPacket { EntityId = context.Loot.EntityId });
            context.Drain();
            context.Corpse.Position = new Vector3(3, 0, 0);

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.AreEqual(0, context.Client.Player.Inventory.PersonalInventory.Count(id => id == context.Item.EntityId));
            Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(0, context.Drain().Count);

            context.Corpse.Position = Vector3.Zero;
            context.Storage.BeforeSave = _ =>
            {
                Assert.AreEqual(0, context.Client.Player.Inventory.PersonalInventory.Count(id => id == context.Item.EntityId));
                Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
                Assert.AreEqual(0, context.Drain().Count);
            };
            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.AreEqual(1, context.Client.Player.Inventory.PersonalInventory.Count(id => id == context.Item.EntityId));
            Assert.AreEqual(107, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.IsTrue(context.Drain().OfType<GotLootPacket>().Any());
            using var verify = context.Storage.Open();
            Assert.AreEqual(107, verify.CharacterEntries.AsNoTracking().Single().Credit);
            Assert.AreEqual(1, verify.CharacterInventoryEntries.AsNoTracking()
                .Count(row => row.ItemId == context.Item.Id));
        }

        [TestMethod]
        public void PersistenceFailureRollsBackTheWholeLootClaimAndAllowsRetry()
        {
            using var context = new LootFixture();
            context.Storage.AfterSave = _ => throw new DbUpdateException("Injected loot failure.");

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(0, context.Client.Player.Inventory.PersonalInventory.Count(id => id == context.Item.EntityId));
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(0, context.Drain().Count);
            using (var verify = context.Storage.Open())
            {
                Assert.AreEqual(100, verify.CharacterEntries.AsNoTracking().Single().Credit);
                Assert.AreEqual(0, verify.CharacterInventoryEntries.AsNoTracking()
                    .Count(row => row.ItemId == context.Item.Id));
            }

            context.Storage.AfterSave = null;
            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });
            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.AreEqual(107, context.Client.Player.Credits[CurencyType.Credits]);
        }

        [TestMethod]
        public void OwnerDepartureReclaimsUnclaimedLootAndDispenserIdsAreNeverRecycled()
        {
            using var context = new LootFixture();
            var dispenserId = context.Loot.EntityId;
            var itemId = context.Item.EntityId;

            context.Manager.RemoveForOwner(context.Map, context.Client);
            var later = new LootDispenser();

            Assert.IsFalse(context.Map.LootDispensers.ContainsKey(dispenserId));
            Assert.IsNull(EntityManager.Instance.GetItem(itemId));
            Assert.IsNull(context.Storage.Read(context.Item));
            Assert.AreNotEqual(dispenserId, later.EntityId);
        }

        [TestMethod]
        public void LootMergesStacksAndPersistsTheRemainderInOneClaim()
        {
            using var context = new LootFixture();
            EntityClassManager.Instance.GetItemClassInfo(context.Item).StackSize = 5;
            var existing = context.Storage.AddAmmo(4);

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.AreEqual(5u, existing.StackSize);
            Assert.AreEqual(2u, context.Item.StackSize);
            Assert.AreEqual(2, context.Storage.ReadInventory()
                .Count(row => row.InventoryType == (uint)InventoryType.Personal));
            Assert.AreEqual(5u, context.Storage.Read(existing).StackSize);
            Assert.AreEqual(2u, context.Storage.Read(context.Item).StackSize);
        }

        [TestMethod]
        public void RepeatedLootTemplatesShareAPlannedStack()
        {
            using var context = new LootFixture();
            EntityClassManager.Instance.GetItemClassInfo(context.Item).StackSize = 5;
            context.Item.StackSize = 2;
            context.Loot.LootItems[0] = new LootItem(
                context.Item, context.Client.Player.EntityId, 0);
            using (var database = context.Storage.Open())
            {
                database.ItemEntries.Single(entry => entry.ItemId == context.Item.Id).StackSize = 2;
                database.SaveChanges();
            }
            var second = context.Storage.AddUnownedLoot(2);
            context.Loot.LootItems.Add(new LootItem(
                second, context.Client.Player.EntityId, 0));

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.AreEqual(1, context.Storage.ReadInventory()
                .Count(row => row.InventoryType == (uint)InventoryType.Personal));
            Assert.AreEqual(4u, context.Storage.Read(context.Item).StackSize);
            Assert.IsNull(context.Storage.Read(second));
        }

        [TestMethod]
        public void AutoLootThresholdLeavesBetterItemsButPaysCreditsOnlyOnce()
        {
            using var context = new LootFixture();
            context.Item.ItemTemplate.QualityId = (int)LootQuality.Normal;

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket
                {
                    EntityId = context.Loot.EntityId,
                    AutoLootOnly = true
                });

            Assert.IsFalse(context.Item.OwnerId == context.Client.Player.Id);
            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(107, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(0, context.Loot.Credits);

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });
            Assert.IsTrue(context.Loot.FullyLooted);
            Assert.AreEqual(107, context.Client.Player.Credits[CurencyType.Credits]);
        }

        [TestMethod]
        public void FullInventoryCategoryRejectsItemsAndCreditsAsOneBatch()
        {
            using var context = new LootFixture();
            EntityClassManager.Instance.GetItemClassInfo(context.Item).StackSize = 1;
            for (uint slot = 50; slot < 100; slot++)
                context.Storage.AddAmmo(1, slot);

            context.Manager.RequestLootAllFromCorpse(context.Client,
                new RequestLootAllFromCorpsePacket { EntityId = context.Loot.EntityId });

            Assert.IsFalse(context.Loot.FullyLooted);
            Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(7, context.Loot.Credits);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void MapDepartureReclaimsOwnedCorpseLootBeforeTransfer()
        {
            using var context = new LootFixture();
            var destination = new MapChannel
            {
                MapInfo = new MapInfo(1221, "destination", 1, 0),
                ClientList = new List<Rasa.Game.Client>(),
                PlayerLimit = 128
            };
            var manager = new MapChannelManager(context.Storage);
            manager.MapChannelArray[destination.MapInfo.MapContextId] = destination;

            Assert.IsTrue(manager.ChangeMap(
                context.Client,
                destination.MapInfo.MapContextId,
                Vector3.Zero,
                0));

            Assert.IsFalse(context.Map.LootDispensers.ContainsKey(context.Loot.EntityId));
            Assert.IsNull(EntityManager.Instance.GetItem(context.Item.EntityId));
            Assert.IsNull(context.Storage.Read(context.Item));
        }

        private sealed class LootFixture : System.IDisposable
        {
            internal WeaponAmmoContext Storage { get; } = new(characterId: 42);
            internal Rasa.Game.Client Client => Storage.Client;
            internal MapChannel Map => Storage.World.Map;
            internal LootDispenserManager Manager { get; }
            internal Creature Corpse { get; }
            internal LootDispenser Loot { get; }
            internal Item Item { get; }

            internal LootFixture()
            {
                Client.Player.Credits[CurencyType.Credits] = 100;
                Client.Player.Position = Vector3.Zero;
                Client.Player.Attributes[Attributes.Health] =
                    new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
                using (var database = Storage.Open())
                {
                    database.CharacterEntries.Single().Credit = 100;
                    database.SaveChanges();
                }
                Item = Storage.AddUnownedLoot(3);
                Corpse = new Creature
                {
                    EntityClass = EntityClasses.HumanBaseMale,
                    MapContextId = Map.MapInfo.MapContextId,
                    Position = Vector3.Zero,
                    State = CharacterState.Dead,
                    Faction = Factions.Bane,
                    AppearanceData = new(),
                    Attributes = new Dictionary<Attributes, ActorAttributes>
                    {
                        [Attributes.Health] = new(Attributes.Health, 100, 100, 0, 0, 0),
                        [Attributes.Armor] = new(Attributes.Armor, 0, 0, 0, 0, 0)
                    }
                };
                CellManager.Instance.AddToWorld(Map, Corpse);
                Loot = new LootDispenser
                {
                    Owner = Client.Player.EntityId,
                    AttachedTo = Corpse.EntityId,
                    IsLootable = true,
                    Credits = 7,
                    UnitOfWorkFactory = Storage
                };
                Loot.LootItems.Add(new LootItem(Item, Client.Player.EntityId, 0));
                Corpse.CorpseLootEntityId = Loot.EntityId;
                Map.LootDispensers.Add(Loot.EntityId, Loot);
                Manager = new LootDispenserManager(Storage, _ => 2);
                Drain();
            }

            internal List<Rasa.Packets.PythonPacket> Drain() => WorldTestContext.Drain(Client)
                .Select(packet => packet.Message).OfType<CallMethodMessage>()
                .Select(message => message.Packet).ToList();

            public void Dispose()
            {
                Manager.RemoveForOwner(Map, Client);
                CellManager.Instance.RemoveCreatureFromWorld(Map, Corpse);
                Storage.Dispose();
            }
        }
    }
}
