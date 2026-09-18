using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Structures;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class CurrencyCallSiteTests
    {
        [TestMethod]
        public void AuctionBuyoutDoesNotDeliverOrRemoveListingAfterFailedCharge()
        {
            using var context = new WeaponAmmoContext(characterId: 42);
            context.Client.Player.Credits[CurencyType.Credits] = 100;
            using (var database = context.Open())
            {
                database.CharacterEntries.Single(entry => entry.Id == 42).Credit = 100;
                database.SaveChanges();
            }
            var item = context.AddUnownedLoot(1);
            context.AddAuction(item, sellerId: 99, price: 50);
            var saves = context.SaveAttempts;
            context.BeforeSave = _ =>
            {
                if (context.SaveAttempts == saves + 1)
                    throw new DbUpdateException("Injected auction charge failure.");
            };
            var manager = CreateAuctionManager(context);

            manager.RequestAuctionBuyout(context.Client, new RequestAuctionBuyoutPacket
            {
                ItemId = checked((uint)item.EntityId),
                Price = 50
            });

            using var verify = context.Open();
            Assert.AreEqual(100,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 42).Credit);
            Assert.IsNotNull(verify.AuctionEntries.AsNoTracking()
                .SingleOrDefault(entry => entry.ItemId == item.Id));
            var inventory = verify.CharacterInventoryEntries.AsNoTracking()
                .Single(entry => entry.ItemId == item.Id);
            Assert.AreEqual(99u, inventory.CharacterId);
            Assert.AreEqual((uint)InventoryType.AuctionInventory, inventory.InventoryType);
            Assert.AreEqual(0, context.Client.Player.Inventory.InboxItems.Count);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client)
                .Select(packet => packet.Message)
                .OfType<CallMethodMessage>()
                .Select(message => message.Packet)
                .OfType<AuctionBuyoutSuccessPacket>()
                .Count());
        }

        [TestMethod]
        public void AuctionBuyoutRollsBackBuyerSellerDeliveryAndListingWhenDeliverySaveFails()
        {
            using var context = new WeaponAmmoContext(characterId: 42);
            context.Client.Player.Credits[CurencyType.Credits] = 100;
            using (var database = context.Open())
            {
                database.CharacterEntries.Single(entry => entry.Id == 42).Credit = 100;
                database.SaveChanges();
            }
            var item = context.AddUnownedLoot(1);
            context.AddAuction(item, sellerId: 99, price: 50);
            var saves = context.SaveAttempts;
            context.BeforeSave = _ =>
            {
                if (context.SaveAttempts == saves + 3)
                    throw new DbUpdateException("Injected auction inbox delivery failure.");
            };

            CreateAuctionManager(context).RequestAuctionBuyout(context.Client,
                new RequestAuctionBuyoutPacket
                {
                    ItemId = checked((uint)item.EntityId),
                    Price = 50
                });

            using var verify = context.Open();
            Assert.AreEqual(100,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 42).Credit);
            Assert.AreEqual(0,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 99).Credit);
            Assert.IsNotNull(verify.AuctionEntries.AsNoTracking()
                .SingleOrDefault(entry => entry.ItemId == item.Id));
            var inventory = verify.CharacterInventoryEntries.AsNoTracking()
                .Single(entry => entry.ItemId == item.Id);
            Assert.AreEqual(99u, inventory.CharacterId);
            Assert.AreEqual((uint)InventoryType.AuctionInventory, inventory.InventoryType);
            Assert.AreEqual(100, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(99u, item.OwnerId);
            Assert.AreEqual(0, context.Client.Player.Inventory.InboxItems.Count);
        }

        [TestMethod]
        public void ConcurrentAuctionBuyoutRetriesTransferExactlyOnce()
        {
            using var context = new WeaponAmmoContext(characterId: 42);
            context.Client.Player.Credits[CurencyType.Credits] = 100;
            using (var database = context.Open())
            {
                database.CharacterEntries.Single(entry => entry.Id == 42).Credit = 100;
                database.SaveChanges();
            }
            var item = context.AddUnownedLoot(1);
            context.AddAuction(item, sellerId: 99, price: 50);
            var manager = CreateAuctionManager(context);
            var packet = new RequestAuctionBuyoutPacket
            {
                ItemId = checked((uint)item.EntityId),
                Price = 50
            };

            Task.WaitAll(
                Task.Run(() => manager.RequestAuctionBuyout(context.Client, packet)),
                Task.Run(() => manager.RequestAuctionBuyout(context.Client, packet)));

            var auctionPackets = WorldTestContext.Drain(context.Client)
                .Select(queued => queued.Message)
                .OfType<CallMethodMessage>()
                .Select(message => message.Packet)
                .ToList();
            Assert.AreEqual(1,
                auctionPackets.OfType<AuctionBuyoutSuccessPacket>().Count(),
                string.Join(", ", auctionPackets
                    .OfType<AuctionBuyoutFailedPacket>()
                    .Select(failure => failure.PlayerMessageId)));
            using var verify = context.Open();
            Assert.AreEqual(50,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 42).Credit);
            Assert.AreEqual(50,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 99).Credit);
            Assert.AreEqual(0, verify.AuctionEntries.AsNoTracking()
                .Count(entry => entry.ItemId == item.Id));
            var inventory = verify.CharacterInventoryEntries.AsNoTracking()
                .Single(entry => entry.ItemId == item.Id);
            Assert.AreEqual(42u, inventory.CharacterId);
            Assert.AreEqual((uint)InventoryType.InboxInventory, inventory.InventoryType);
            Assert.AreEqual(50, context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(1, context.Client.Player.Inventory.InboxItems
                .Count(entityId => entityId == item.EntityId));
            Assert.AreEqual(1,
                auctionPackets.OfType<AuctionBuyoutSuccessPacket>().Count());
        }

        [TestMethod]
        public void AuctionBuyoutRejectsBuyerWhoIsAlsoTheSellerWithoutChangingState()
        {
            using var context = new WeaponAmmoContext(characterId: 42);
            context.Client.Player.Credits[CurencyType.Credits] = 100;
            using (var database = context.Open())
            {
                database.CharacterEntries.Single(entry => entry.Id == 42).Credit = 100;
                database.SaveChanges();
            }
            var item = context.AddUnownedLoot(1);
            context.AddAuction(item, sellerId: 99, price: 50);
            using (var database = context.Open())
            {
                database.AuctionEntries.Single(entry => entry.ItemId == item.Id).SellerId = 42;
                var inventory = database.CharacterInventoryEntries
                    .Single(entry => entry.ItemId == item.Id);
                inventory.AccountId = context.Client.AccountEntry.Id;
                inventory.CharacterId = 42;
                database.SaveChanges();
            }
            item.OwnerId = 42;

            CreateAuctionManager(context).RequestAuctionBuyout(context.Client,
                new RequestAuctionBuyoutPacket
                {
                    ItemId = checked((uint)item.EntityId),
                    Price = 50
                });

            using var verify = context.Open();
            Assert.AreEqual(100,
                verify.CharacterEntries.AsNoTracking().Single(entry => entry.Id == 42).Credit);
            Assert.IsNotNull(verify.AuctionEntries.AsNoTracking()
                .SingleOrDefault(entry => entry.ItemId == item.Id));
            Assert.AreEqual((uint)InventoryType.AuctionInventory,
                verify.CharacterInventoryEntries.AsNoTracking()
                    .Single(entry => entry.ItemId == item.Id).InventoryType);
            Assert.AreEqual(0, context.Client.Player.Inventory.InboxItems.Count);
        }

        private static AuctionHouseManager CreateAuctionManager(WeaponAmmoContext context) =>
            (AuctionHouseManager)typeof(AuctionHouseManager)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(Rasa.Repositories.UnitOfWork.IGameUnitOfWorkFactory) }, null)
                .Invoke(new object[] { context });
    }
}
