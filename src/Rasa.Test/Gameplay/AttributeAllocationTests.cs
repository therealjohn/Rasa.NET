extern alias RasaGame;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class AttributeAllocationTests
    {
        [TestMethod]
        public void ValidAllocationPersistsBeforePublishingStatsAndRemainingPoints()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(3);
            manager.UpdateStatsValues(client, true);
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);
            database.BeforeSave = _ =>
            {
                Assert.AreEqual(0, client.Player.SpentBody);
                Assert.AreEqual(0, client.Player.SpentMind);
                Assert.AreEqual(0, client.Player.SpentSpirit);
                Assert.AreEqual(14, client.Player.Attributes[Attributes.Body].Current);
                Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            };

            manager.AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 2, Mind = 1, Spirit = 1 });

            var saved = database.Read(client.Player.Id);
            Assert.AreEqual(2, saved.Body);
            Assert.AreEqual(1, saved.Mind);
            Assert.AreEqual(1, saved.Spirit);
            Assert.AreEqual(1, database.SaveAttempts);
            Assert.AreEqual(2, client.Player.SpentBody);
            Assert.AreEqual(1, client.Player.SpentMind);
            Assert.AreEqual(1, client.Player.SpentSpirit);
            var packets = WorldTestContext.Drain(client).Select(packet => packet.Message)
                .OfType<CallMethodMessage>().ToList();
            Assert.IsTrue(packets.All(packet => packet.EntityId == client.Player.EntityId));
            var stats = packets.Select(packet => packet.Packet).OfType<AttributeInfoPacket>().Single();
            Assert.AreEqual(16, stats.ActorAttributes[Attributes.Body].Current);
            Assert.AreEqual(15, stats.ActorAttributes[Attributes.Mind].Current);
            Assert.AreEqual(15, stats.ActorAttributes[Attributes.Spirit].Current);
            var points = packets.Select(packet => packet.Packet).OfType<AvailableAllocationPointsPacket>().Single();
            Assert.AreEqual(2, points.AvailableAttributePoints);
            Assert.AreEqual(9, points.AvailableSkillPoints);
        }

        [TestMethod]
        [DataRow(-1, 0, 0)]
        [DataRow(0, -1, 0)]
        [DataRow(0, 0, -1)]
        [DataRow(3, 2, 2)]
        [DataRow(int.MaxValue, int.MaxValue, 2)]
        [DataRow(int.MinValue, 0, 0)]
        public void InvalidAllocationDoesNotMutatePersistOrPublish(int body, int mind, int spirit)
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(3);
            manager.UpdateStatsValues(client, true);
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);

            manager.AllocateAttributePoints(client, new AllocateAttributePointsPacket
            {
                Body = body, Mind = mind, Spirit = spirit
            });

            Assert.AreEqual(0, database.OpenAttempts);
            Assert.AreEqual(0, client.Player.SpentBody);
            Assert.AreEqual(0, client.Player.SpentMind);
            Assert.AreEqual(0, client.Player.SpentSpirit);
            Assert.AreEqual(14, client.Player.Attributes[Attributes.Body].Current);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            var saved = database.Read(client.Player.Id);
            Assert.AreEqual(0, saved.Body);
            Assert.AreEqual(0, saved.Mind);
            Assert.AreEqual(0, saved.Spirit);
        }

        [TestMethod]
        [DataRow(ClientState.Loading)]
        [DataRow(ClientState.CharacterSelection)]
        [DataRow(ClientState.Teleporting)]
        [DataRow(ClientState.Disconnected)]
        public void OutOfWorldAllocationDoesNotSpendPoints(ClientState state)
        {
            using var context = new ProgressionTestContext();
            var client = context.CreateClient(3);
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            client.State = state;

            new ManifestationManager(null).AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 1 });

            Assert.AreEqual(0, client.Player.SpentBody);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
        }

        [TestMethod]
        public void AllocationRequiresCurrentWorldMembership()
        {
            using var context = new ProgressionTestContext();
            var client = context.CreateClient(3);

            new ManifestationManager(null).AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 1 });

            Assert.AreEqual(0, client.Player.SpentBody);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
        }

        [TestMethod]
        public void AllocationCannotOverflowAlreadySpentPoints()
        {
            using var context = new ProgressionTestContext();
            var client = context.CreateClient(3);
            client.Player.SpentBody = 3;
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);

            new ManifestationManager(null).AllocateAttributePoints(client,
                new AllocateAttributePointsPacket { Body = int.MaxValue });

            Assert.AreEqual(3, client.Player.SpentBody);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
        }

        [TestMethod]
        public void FailedAllocationSaveLeavesRuntimeAndReopenedCharacterUnchanged()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(3);
            manager.UpdateStatsValues(client, true);
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);
            database.BeforeSave = _ => throw new DbUpdateException("Fixture allocation save failure.");

            manager.AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 1 });

            Assert.AreEqual(1, database.SaveAttempts);
            Assert.AreEqual(0, client.Player.SpentBody);
            Assert.AreEqual(14, client.Player.Attributes[Attributes.Body].Current);
            Assert.AreEqual(6, manager.GetAvailableAttributePoints(client.Player));
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            Assert.AreEqual(0, database.Read(client.Player.Id).Body);
        }

        [TestMethod]
        public void QueuedAttributePacketsKeepTheStatsPairedWithTheirAllocationPoints()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(3);
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);

            manager.AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 1 });
            manager.AllocateAttributePoints(client, new AllocateAttributePointsPacket { Body = 1 });

            var packets = WorldTestContext.Drain(client).Select(packet => packet.Message)
                .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
            CollectionAssert.AreEqual(new[] { 15, 16 }, packets.OfType<AttributeInfoPacket>()
                .Select(packet => packet.ActorAttributes[Attributes.Body].Current).ToArray());
            CollectionAssert.AreEqual(new[] { 5, 4 }, packets.OfType<AvailableAllocationPointsPacket>()
                .Select(packet => packet.AvailableAttributePoints).ToArray());
        }
    }
}
