using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Inventory.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Mission.Server;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class MissionRewardTests
    {
        [TestMethod]
        public void SuccessfulTurnInCommitsTheWholeRewardBeforePublishing()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            context.BeforeSave = _ =>
            {
                Assert.AreEqual(MissionState.Active, context.Client.Player.Missions[429].State);
                Assert.IsTrue(context.Client.Player.Missions[429].Completeable);
                Assert.AreEqual(before.Experience, context.Client.Player.Experience);
                Assert.AreEqual(before.Credits, context.Client.Player.Credits[CurencyType.Credits]);
                Assert.AreEqual(before.Prestige, context.Client.Player.Credits[CurencyType.Prestige]);
                Assert.AreEqual(0, context.Drain().Count);
            };

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience + context.Reward.Experience, after.Experience);
            Assert.AreEqual(before.Credits + context.Reward.Currencies[CurencyType.Credits], after.Credits);
            Assert.AreEqual(before.Prestige + context.Reward.Currencies[CurencyType.Prestige], after.Prestige);
            Assert.AreEqual(before.ItemCount + 5, after.ItemCount);
            Assert.AreEqual(MissionState.Completed, context.Client.Player.Missions[429].State);
            Assert.IsFalse(context.Client.Player.Missions[429].Completeable);
            var packets = context.Drain();
            Assert.AreEqual(1, packets.OfType<ExperienceChangedPacket>().Count());
            Assert.AreEqual(2, packets.OfType<UpdateCreditsPacket>().Count());
            Assert.AreEqual(2, packets.OfType<InventoryAddItemPacket>().Count());
            Assert.AreEqual(1, packets.OfType<MissionCompleteablePacket>().Count());
            Assert.AreEqual(1, packets.OfType<MissionCompletedPacket>().Count());
            Assert.AreEqual(1, packets.OfType<MissionRewardedPacket>().Count());
        }

        [TestMethod]
        public void RetriedTurnInCannotDuplicateRewards()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            context.Drain();
            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertGrantedOnce(context, before, 5);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void InternalIntegerSelectionChoosesExactlyOneRewardAlternative()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 1));

            AssertGrantedOnce(context, before, 7);
        }

        [TestMethod]
        public void ConcurrentTurnInGrantsAtMostOnce()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();

            var results = Task.WhenAll(
                Task.Run(() => context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0)),
                Task.Run(() => context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0))).GetAwaiter().GetResult();

            Assert.AreEqual(1, results.Count(result => result));
            AssertGrantedOnce(context, before, 5);
            Assert.AreEqual(1, context.Drain().OfType<MissionRewardedPacket>().Count());
        }

        [TestMethod]
        public void ReconnectRetryUsesCompletedDurableStateAndGrantsNothing()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            context.Drain();

            context.Client.Player.Missions.Clear();
            context.ReloadPlayerMissions();

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            AssertGrantedOnce(context, before, 5);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void SaveFailurePublishesNothingAndAllowsRetry()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            context.BeforeSave = _ => throw new DbUpdateException("Injected save failure.");

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertUnchanged(context, before);
            context.BeforeSave = null;
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            AssertGrantedOnce(context, before, 5);
        }

        [TestMethod]
        public void ConnectionLossPublishesNothingAndAllowsRetry()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            context.AfterSave = database => database.Database.GetDbConnection().Close();

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertUnchanged(context, before);
            context.AfterSave = null;
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            AssertGrantedOnce(context, before, 5);
        }

        [TestMethod]
        [DataRow(true, true)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(false, false)]
        public void UnrelatedPersistenceErrorsPreserveIdentityAndStack(
            bool duringQuery,
            bool invalidOperation)
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            Exception expected = invalidOperation
                ? new InvalidOperationException("Injected application failure.")
                : new NullReferenceException("Injected application failure.");
            if (duringQuery)
                context.BeforeQuery = () => ThrowAtPersistenceBoundary(expected);
            else
                context.AfterSave = database =>
                {
                    database.Database.GetDbConnection().Close();
                    ThrowAtPersistenceBoundary(expected);
                };

            var actual = invalidOperation
                ? Assert.ThrowsExactly<InvalidOperationException>(() =>
                    context.Manager.TryCompleteNpcMission(
                        context.Client, context.Receiver.EntityId, 429, 0))
                : (Exception)Assert.ThrowsExactly<NullReferenceException>(() =>
                    context.Manager.TryCompleteNpcMission(
                        context.Client, context.Receiver.EntityId, 429, 0));

            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            AssertUnchanged(context, before);
            context.BeforeQuery = null;
            context.AfterSave = null;
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
        }

        [TestMethod]
        public void ProgrammingFailureReleasesStagedRewardEntities()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var expectedEntityId = EntityManager.Instance.GetEntityId;
            EntityManager.Instance.FreeEntity(expectedEntityId);
            var expected = new InvalidOperationException("Injected application failure.");
            context.AfterSave = _ => ThrowAtPersistenceBoundary(expected);

            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0));

            Assert.AreSame(expected, actual);
            var reusableEntityId = EntityManager.Instance.GetEntityId;
            Assert.AreEqual(expectedEntityId, reusableEntityId);
            EntityManager.Instance.FreeEntity(reusableEntityId);
        }

        [TestMethod]
        [DataRow("database")]
        [DataRow("overflow")]
        [DataRow("capability")]
        public void ExpectedGameplayAndPersistenceFailuresPublishNothingAndAllowRetry(string failure)
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            Exception expected = failure switch
            {
                "database" => new DbUpdateException("Injected update failure."),
                "overflow" => new OverflowException("Injected arithmetic failure."),
                "capability" => new NotSupportedException("Injected transaction capability failure."),
                _ => throw new AssertFailedException($"Unknown failure {failure}.")
            };
            context.AfterSave = _ => throw expected;

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertUnchanged(context, before);
            context.AfterSave = null;
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
        }

        [TestMethod]
        public void WrongReceiverNonCompletableMissionAndInvalidSelectionAreRejected()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var wrong = context.AddNpc(77);
            var before = context.ReadRewardTotals();

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, wrong.EntityId, 429, 0));
            context.Client.Player.Missions[429].Completeable = false;
            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
            context.Client.Player.Missions[429].Completeable = true;
            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 2));

            AssertUnchanged(context, before);
        }

        [TestMethod]
        public void FullInventoryRejectsTheWholeRewardAndAllowsRetry()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            context.FillRewardCategory();
            var before = context.ReadRewardTotals();

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertUnchanged(context, before);
            for (var slot = 50; slot <= 51; slot++)
            {
                var item = EntityManager.Instance.GetItem(
                    context.Client.Player.Inventory.PersonalInventory[slot]);
                using (var unit = context.CreateChar())
                {
                    unit.CharacterInventories.DeleteInvItem(
                        context.Client.AccountEntry.Id,
                        context.Client.Player.Id,
                        (uint)InventoryType.Personal,
                        (uint)slot);
                    unit.Items.DeleteItem(item.Id);
                }
                EntityManager.Instance.ReleaseEntity(item.EntityId, EntityType.Item);
                context.Client.Player.Inventory.PersonalInventory[slot] = 0;
            }
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UnverifiedWireSelectionCannotGrantRewards(bool selectionIndex)
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();

            NpcManager.Instance.CompleteNPCMission(context.Client, new CompleteNPCMissionPacket
            {
                EntityId = context.Receiver.EntityId,
                MissionId = 429,
                SelectionIdx = selectionIndex
            });

            AssertUnchanged(context, before);
            Assert.AreEqual(typeof(bool),
                typeof(CompleteNPCMissionPacket).GetProperty(nameof(CompleteNPCMissionPacket.SelectionIdx))!.PropertyType);
        }

        [TestMethod]
        [DataRow(1449u)]
        [DataRow(1407u)]
        [DataRow(1069u)]
        public void ProductionMissionsRemainInactiveWithoutApprovedRewardDefinitions(uint missionId)
        {
            using var context = MissionTestContext.WithDefinitions(missionId);
            context.SeedMission(context.Client.Player.Id, missionId, (uint)MissionState.Active, true);
            context.ReloadPlayerMissions();
            var receiver = context.AddNpc(88);
            var before = context.ReadRewardTotals();

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, receiver.EntityId, missionId, 0));

            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience, after.Experience);
            Assert.AreEqual(before.Credits, after.Credits);
            Assert.AreEqual(before.Prestige, after.Prestige);
            Assert.AreEqual(before.ItemCount, after.ItemCount);
            Assert.AreEqual((uint)MissionState.Active, context.ReadMission(missionId).MissionState);
            Assert.IsTrue(context.ReadMission(missionId).Completeable);
            Assert.AreEqual(0, context.Drain().Count);
        }

        private static void AssertGrantedOnce(
            MissionTestContext context,
            MissionTestContext.RewardTotals before,
            long itemQuantity)
        {
            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience + context.Reward.Experience, after.Experience);
            Assert.AreEqual(before.Credits + context.Reward.Currencies[CurencyType.Credits], after.Credits);
            Assert.AreEqual(before.Prestige + context.Reward.Currencies[CurencyType.Prestige], after.Prestige);
            Assert.AreEqual(before.ItemCount + itemQuantity, after.ItemCount);
            Assert.AreEqual((uint)MissionState.Completed, context.ReadMission(429).MissionState);
            Assert.IsFalse(context.ReadMission(429).Completeable);
        }

        private static void AssertUnchanged(
            MissionTestContext context,
            MissionTestContext.RewardTotals before)
        {
            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience, after.Experience);
            Assert.AreEqual(before.Credits, after.Credits);
            Assert.AreEqual(before.Prestige, after.Prestige);
            Assert.AreEqual(before.ItemCount, after.ItemCount);
            Assert.AreEqual((uint)MissionState.Active, context.ReadMission(429).MissionState);
            Assert.IsTrue(context.ReadMission(429).Completeable);
            Assert.AreEqual(MissionState.Active, context.Client.Player.Missions[429].State);
            Assert.IsTrue(context.Client.Player.Missions[429].Completeable);
            Assert.AreEqual(0, context.Drain().Count);
        }

        private static void ThrowAtPersistenceBoundary(Exception error) => throw error;
    }
}
