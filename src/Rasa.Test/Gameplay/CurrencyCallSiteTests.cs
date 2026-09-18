using System.Linq;
using System.Reflection;
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
            var manager = (AuctionHouseManager)typeof(AuctionHouseManager)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(Rasa.Repositories.UnitOfWork.IGameUnitOfWorkFactory) }, null)
                .Invoke(new object[] { context });

            manager.RequestAuctionBuyout(context.Client, new RequestAuctionBuyoutPacket
            {
                ItemId = item.Id,
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
    }
}
