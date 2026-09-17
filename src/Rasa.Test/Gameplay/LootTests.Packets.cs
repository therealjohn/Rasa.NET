using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Memory;
    using Rasa.Packets;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.LootDispenser.Client;
    using Rasa.Packets.LootDispenser.Server;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;

    public partial class LootTests
    {
        private static byte[] Bytes(PythonPacket packet)
        {
            using var stream = new MemoryStream();
            using var binary = new BinaryWriter(stream, Encoding.UTF8, true);
            using var writer = new PythonWriter(binary);
            ((ServerPythonPacket)packet).Write(writer);
            return stream.ToArray();
        }

        [TestMethod]
        public void MetadataAndReceiptPacketsKeepTheExistingWireLayoutsAndSnapshotTheirValues()
        {
            using var context = new LootContext();
            var source = new LootDispenser(context.Loot) { AttachedTo = 200, Owner = 100, LootQuality = (LootQuality)2 };
            var item = source.LootItems.Single();
            item.EntityId = 300;
            item.ActorId = 100;
            var packets = new PythonPacket[]
            {
                new AttachInfoPacket(source.AttachedTo), new LootInfoPacket(source.LootItems),
                new OverallQualityPacket(source.LootQuality), new CanLootItemsPacket(true, source.LootItems),
                new TakenInfoPacket(100, source.LootItems), new ActorGotLootPacket(source), new GotLootPacket(source),
                new CanLootItemsPacket(false, source.LootItems)
            };
            var expected = new[]
            {
                "812FC800000000000000",
                "81612F2C01000000000000841D1C132F640000000000000000",
                "8112",
                "8201612F2C010000000000008101",
                "81612F2C0100000000000001",
                "822FC80000000000000071812F2C01000000000000",
                "832FC80000000000000071831E4B0C132F2C0100000000000017",
                "820000"
            };
            item.ItemQuantity = 99;
            item.ItemClassId = 6048;
            source.AttachedTo = 999;
            source.Credits = 999;
            source.LootItems.Clear();
            for (var index = 0; index < packets.Length; index++)
                CollectionAssert.AreEqual(Convert.FromHexString(expected[index]), Bytes(packets[index]), packets[index].GetType().Name);
        }

        [TestMethod]
        public void ClaimPacketsPreserveReceiptsAfterRuntimeLootHasBeenReclaimed()
        {
            using var context = new LootContext();
            var item = context.Loot.LootItems.Single();
            var id = item.EntityId;
            Assert.IsTrue(Claim(context));
            var packets = context.Drain();
            Assert.AreEqual(0, context.Loot.LootItems.Count);
            Assert.AreEqual(id, packets.OfType<TakenInfoPacket>().Single().LootItems.Single().EntityId);
            var receipt = packets.OfType<GotLootPacket>().Single();
            Assert.AreEqual(7, receipt.Loot.Credits);
            Assert.AreEqual(3u, receipt.Loot.LootItems.Single().ItemQuantity);
            var types = packets.Select(packet => packet.GetType()).ToList();
            Assert.IsTrue(types.IndexOf(typeof(UpdateCreditsPacket)) < types.IndexOf(typeof(ActorGotLootPacket)));
            Assert.IsTrue(types.IndexOf(typeof(ActorGotLootPacket)) < types.IndexOf(typeof(TakenInfoPacket)));
            Assert.IsTrue(types.IndexOf(typeof(TakenInfoPacket)) < types.IndexOf(typeof(CanLootItemsPacket)));
            Assert.IsTrue(types.IndexOf(typeof(CanLootItemsPacket)) < types.IndexOf(typeof(GotLootPacket)));
            Assert.AreEqual(typeof(DestroyPhysicalEntityPacket), types.Last());
            foreach (var packet in packets)
                Assert.IsTrue(Bytes(packet).Length > 0);
        }

        [TestMethod]
        public void KnownRequestTupleLayoutsDecodeWithoutInventedFields()
        {
            using var openReader = new PythonReader(new BinaryReader(new MemoryStream(
                Convert.FromHexString("812F4D00000000000000"))));
            var open = new RequestCorpseLootingPacket();
            open.Read(openReader);
            Assert.AreEqual(77UL, open.EntityId);
            Assert.AreEqual(GameOpcode.RequestCorpseLooting, open.Opcode);
            using var allReader = new PythonReader(new BinaryReader(new MemoryStream(
                Convert.FromHexString("822F4D0000000000000000"))));
            var all = new RequestLootAllFromCorpsePacket();
            all.Read(allReader);
            Assert.AreEqual(77UL, all.EntityId);
            Assert.IsFalse(all.AutoLootOnly);
            Assert.AreEqual(GameOpcode.RequestLootAllFromCorpse, all.Opcode);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ManualAndAutomaticRequestsPreserveWholeBatchLootingUntilFilteringExists(bool automatic)
        {
            using var context = new LootContext();
            var manager = new LootDispenserManager(context.Storage);
            var request = new RequestLootAllFromCorpsePacket
            {
                EntityId = context.Loot.EntityId,
                AutoLootOnly = automatic
            };
            Assert.IsTrue(manager.RequestLootAllFromCorpse(context.Client, request));
            Assert.AreEqual(107, context.ReadCredits());
            var item = EntityManager.Instance.GetItem(context.Client.Player.Inventory.PersonalInventory[50]);
            Assert.AreEqual(3u, context.Storage.Read(item).StackSize);
            Assert.IsTrue(context.Drain().Count > 0);

            var saves = context.Storage.SaveAttempts;
            Assert.IsFalse(manager.RequestLootAllFromCorpse(context.Client, request));
            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            Assert.AreEqual(107, context.ReadCredits());
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MalformedTupleArityIsRejected(bool claim)
        {
            using var reader = new PythonReader(new BinaryReader(new MemoryStream(
                Convert.FromHexString("832F4D0000000000000000"))));
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                if (claim)
                    new RequestLootAllFromCorpsePacket().Read(reader);
                else
                    new RequestCorpseLootingPacket().Read(reader);
            });
        }
    }
}
