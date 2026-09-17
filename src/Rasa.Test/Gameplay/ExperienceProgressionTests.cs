extern alias RasaGame;

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Communicator.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Structures.Char;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class ExperienceProgressionTests
    {
        [TestMethod]
        public void MultipleLevelsPersistTogetherBeforeOwnerAndObserverUpdates()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(3);
            var observer = context.CreateClient();
            var distant = context.CreateClient();
            distant.Player.Position = new System.Numerics.Vector3(1000, 0, 0);
            client.Player.Experience = 10500;
            manager.UpdateStatsValues(client, true);
            client.Player.Attributes[Attributes.Health].Current = 100;
            foreach (var member in context.World.Map.ClientList)
                CellManager.Instance.AddToWorld(member);
            context.World.Map.MapCellInfo.Cells[client.Player.Cells[0, 0]].ClientList.Add(observer);
            foreach (var member in context.World.Map.ClientList)
                WorldTestContext.Drain(member);
            database.Seed(client);
            database.BeforeSave = savedContext =>
            {
                Assert.AreEqual(10500U, client.Player.Experience);
                Assert.AreEqual((byte)3, client.Player.Level);
                Assert.AreEqual(14, client.Player.Attributes[Attributes.Body].Current);
                Assert.AreEqual(100, client.Player.Attributes[Attributes.Health].Current);
                Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
                Assert.AreEqual(0, WorldTestContext.Drain(observer).Count);
                var pending = savedContext.ChangeTracker.Entries<CharacterEntry>().Single().Entity;
                Assert.AreEqual(43000U, pending.Experience);
                Assert.AreEqual((byte)5, pending.Level);
            };

            manager.GainExperience(client, 32500);

            var saved = database.Read(client.Player.Id);
            Assert.AreEqual(43000U, saved.Experience);
            Assert.AreEqual((byte)5, saved.Level);
            Assert.AreEqual(1, database.SaveAttempts);
            Assert.AreEqual(43000U, client.Player.Experience);
            Assert.AreEqual((byte)5, client.Player.Level);
            var ownerCalls = WorldTestContext.Drain(client).Select(packet => packet.Message)
                .OfType<CallMethodMessage>().ToList();
            var ownerPackets = ownerCalls.Select(packet => packet.Packet).ToList();
            var xp = ownerPackets.OfType<ExperienceChangedPacket>().Single().XPInfo;
            Assert.AreEqual(43000U, xp.Total);
            Assert.AreEqual(32500U, xp.Gained);
            Assert.AreEqual(32500U, xp.BaseGained);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, ownerPackets.OfType<LevelUpPacket>().Select(packet => packet.Level).ToArray());
            var messages = ownerPackets.OfType<DisplayClientMessagePacket>().ToArray();
            CollectionAssert.AreEqual(new[] { "4", "5" }, messages.Select(packet => packet.Args["level"]).ToArray());
            CollectionAssert.AreEqual(new[] { "3", "3" }, messages.Select(packet => packet.Args["attributePts"]).ToArray());
            CollectionAssert.AreEqual(new[] { "2", "4" }, messages.Select(packet => packet.Args["skillPts"]).ToArray());
            Assert.AreEqual(12, ownerPackets.OfType<AvailableAllocationPointsPacket>().Single().AvailableAttributePoints);
            Assert.AreEqual(15, ownerPackets.OfType<AvailableAllocationPointsPacket>().Single().AvailableSkillPoints);
            Assert.AreEqual(405, ownerPackets.OfType<AttributeInfoPacket>().Single().ActorAttributes[Attributes.Health].Current);

            var observerCalls = WorldTestContext.Drain(observer).Select(packet => packet.Message).OfType<CallMethodMessage>().ToList();
            Assert.IsTrue(observerCalls.All(packet => packet.EntityId == client.Player.EntityId));
            Assert.AreEqual(2, observerCalls.Count);
            Assert.AreEqual((byte)5, observerCalls.Select(packet => packet.Packet).OfType<LevelPacket>().Single().Level);
            var stats = observerCalls.Select(packet => packet.Packet).OfType<AttributeInfoPacket>().Single();
            Assert.AreEqual(18, stats.ActorAttributes[Attributes.Body].Current);
            Assert.AreEqual(405, stats.ActorAttributes[Attributes.Health].Current);
            Assert.AreEqual(0, WorldTestContext.Drain(distant).Count);
        }

        [TestMethod]
        [DataRow((byte)1, 2999U, 0U, (byte)1, 2999U)]
        [DataRow((byte)1, 2999U, 1U, (byte)2, 3000U)]
        [DataRow((byte)49, 92917500U, 10492499U, (byte)49, 103409999U)]
        [DataRow((byte)49, 92917500U, 10492500U, (byte)50, 103410000U)]
        [DataRow((byte)49, 92917500U, 10492510U, (byte)50, 103410010U)]
        [DataRow((byte)50, 103410000U, uint.MaxValue, (byte)50, 103410000U)]
        [DataRow((byte)1, 0U, uint.MaxValue, (byte)50, uint.MaxValue)]
        public void ExperienceBoundariesPreserveThresholdsAndLevelFiftyCap(byte level, uint initialXp,
            uint award, byte expectedLevel, uint expectedXp)
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(level);
            client.Player.Experience = initialXp;
            manager.UpdateStatsValues(client, true);
            client.Player.Attributes[Attributes.Health].Current = 1;
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);

            manager.GainExperience(client, award);

            Assert.AreEqual(expectedLevel, client.Player.Level);
            Assert.AreEqual(expectedXp, client.Player.Experience);
            var saved = database.Read(client.Player.Id);
            Assert.AreEqual(expectedLevel, saved.Level);
            Assert.AreEqual(expectedXp, saved.Experience);
            Assert.AreEqual(level == 50 ? 0 : 1, database.OpenAttempts);
            Assert.AreEqual(level == 50 ? 0 : 1, database.SaveAttempts);
            var packets = WorldTestContext.Drain(client).Select(packet => packet.Message)
                .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
            Assert.AreEqual(level == 50 ? 0 : 1, packets.OfType<ExperienceChangedPacket>().Count());
            Assert.AreEqual(expectedLevel - level, packets.OfType<LevelUpPacket>().Count());
            if (expectedLevel == level)
            {
                Assert.AreEqual(1, client.Player.Attributes[Attributes.Health].Current);
                Assert.IsFalse(packets.OfType<AttributeInfoPacket>().Any());
            }
            else
            {
                Assert.AreEqual(client.Player.Attributes[Attributes.Health].CurrentMax,
                    client.Player.Attributes[Attributes.Health].Current);
            }
        }

        [TestMethod]
        [DataRow((byte)14, 886500U, 4)]
        [DataRow((byte)29, 9837000U, 4)]
        [DataRow((byte)49, 103410000U, 6)]
        public void LevelUpTextReportsOnlyNewPointsAtTierBoundaries(byte level, uint xp, int newSkillPoints)
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient(level);
            client.Player.SpentBody = 3;
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);

            manager.GainExperience(client, xp);

            var packets = WorldTestContext.Drain(client).Select(packet => packet.Message)
                .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
            var message = packets.OfType<DisplayClientMessagePacket>().Single();
            Assert.AreEqual("3", message.Args["attributePts"]);
            Assert.AreEqual(newSkillPoints.ToString(), message.Args["skillPts"]);
            Assert.AreEqual(level * 3 - 3, packets.OfType<AvailableAllocationPointsPacket>().Single().AvailableAttributePoints);
        }

        [TestMethod]
        public void ExperienceOverflowDoesNotWrapPersistOrPublish()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient();
            client.Player.Experience = uint.MaxValue - 1;
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            database.Seed(client);

            manager.GainExperience(client, 2);

            Assert.AreEqual(uint.MaxValue - 1, client.Player.Experience);
            Assert.AreEqual((byte)1, client.Player.Level);
            Assert.AreEqual(0, database.OpenAttempts);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            Assert.AreEqual(uint.MaxValue - 1, database.Read(client.Player.Id).Experience);
        }

        [TestMethod]
        public void FailedExperienceSaveDoesNotPublishOrAdvanceRuntime()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient();
            var observer = context.CreateClient();
            manager.UpdateStatsValues(client, true);
            CellManager.Instance.AddToWorld(client);
            CellManager.Instance.AddToWorld(observer);
            WorldTestContext.Drain(client);
            WorldTestContext.Drain(observer);
            database.Seed(client);
            database.BeforeSave = _ => throw new DbUpdateException("Fixture experience save failure.");

            manager.GainExperience(client, 10500);

            Assert.AreEqual(1, database.SaveAttempts);
            Assert.AreEqual(0U, client.Player.Experience);
            Assert.AreEqual((byte)1, client.Player.Level);
            Assert.AreEqual(10, client.Player.Attributes[Attributes.Body].Current);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
            Assert.AreEqual(0, WorldTestContext.Drain(observer).Count);
            var saved = database.Read(client.Player.Id);
            Assert.AreEqual(0U, saved.Experience);
            Assert.AreEqual((byte)1, saved.Level);
        }

        [TestMethod]
        [DataRow(ClientState.Loading)]
        [DataRow(ClientState.Teleporting)]
        [DataRow(ClientState.Disconnected)]
        public void ExperienceCannotBeAwardedOutsideTheActiveWorld(ClientState state)
        {
            using var context = new ProgressionTestContext();
            var client = context.CreateClient();
            CellManager.Instance.AddToWorld(client);
            WorldTestContext.Drain(client);
            client.State = state;

            new ManifestationManager(null).GainExperience(client, 10500);

            Assert.AreEqual(0U, client.Player.Experience);
            Assert.AreEqual((byte)1, client.Player.Level);
            Assert.AreEqual(0, WorldTestContext.Drain(client).Count);
        }

        [TestMethod]
        public void QueuedOwnerAndObserverStatsRetainTheLevelTheyDescribe()
        {
            using var context = new ProgressionTestContext();
            using var database = new ProgressionDatabase();
            var manager = new ManifestationManager(database);
            var client = context.CreateClient();
            var observer = context.CreateClient();
            CellManager.Instance.AddToWorld(client);
            CellManager.Instance.AddToWorld(observer);
            WorldTestContext.Drain(client);
            WorldTestContext.Drain(observer);
            database.Seed(client);

            manager.GainExperience(client, 3000);
            manager.GainExperience(client, 7500);

            foreach (var recipient in new[] { client, observer })
            {
                var packets = WorldTestContext.Drain(recipient).Select(packet => packet.Message)
                    .OfType<CallMethodMessage>().Select(packet => packet.Packet).ToList();
                CollectionAssert.AreEqual(new[] { 12, 14 }, packets.OfType<AttributeInfoPacket>()
                    .Select(packet => packet.ActorAttributes[Attributes.Body].Current).ToArray());
            }
        }
    }
}
