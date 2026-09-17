using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
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
        public void CompetingClientsGrantOneRewardBatchAndOneDurableCompletion()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var competitor = context.CreateCompetingClient();
            context.ResetCharUnitCount();
            var before = context.ReadRewardTotals();
            using var start = new ManualResetEventSlim();
            Assert.AreNotSame(context.Client.SyncRoot, competitor.SyncRoot);

            var results = Task.WhenAll(
                Task.Run(() =>
                {
                    start.Wait();
                    return context.Manager.TryCompleteNpcMission(
                        context.Client, context.Receiver.EntityId, 429, 0);
                }),
                Task.Run(() =>
                {
                    start.Wait();
                    return context.Manager.TryCompleteNpcMission(
                        competitor, context.Receiver.EntityId, 429, 0);
                }));
            start.Set();
            var completed = results.GetAwaiter().GetResult();

            Assert.AreEqual(1, completed.Count(result => result));
            Assert.AreEqual(2, context.CharUnitsCreated);
            AssertGrantedOnce(context, before, 5);
            Assert.AreEqual(1, context.Drain().Concat(MissionTestContext.Drain(competitor))
                .OfType<MissionRewardedPacket>().Count());
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
                context.BeforeQuery = _ => ThrowAtPersistenceBoundary(expected);
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
            var heldEntityIds = new List<ulong>();
            while (GetFreeEntityIds().Count > 0)
                heldEntityIds.Add(EntityManager.Instance.GetEntityId);
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
            foreach (var entityId in heldEntityIds)
                EntityManager.Instance.FreeEntity(entityId);
        }

        [TestMethod]
        public void MidPublicationFailureReleasesOnlyUnregisteredStagedEntities()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var expected = new InvalidOperationException("Injected publication failure.");
            var published = 0;
            using var grant = new InventoryManager.InventoryGrant(_ =>
            {
                if (published++ == 1)
                    ThrowAtPersistenceBoundary(expected);
            });
            using (var unit = context.CreateChar())
                unit.ExecuteTransaction(() => grant.PlanAndSave(
                    context.Client,
                    new[] { new InventoryManager.InventoryItemGrant(28, 100001) },
                    unit));
            var staged = GetStagedItems(grant);
            Assert.AreEqual(3, staged.Length);

            var actual = Assert.ThrowsExactly<InvalidOperationException>(() => grant.Publish(context.Client));

            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            grant.Dispose();
            var freeIds = GetFreeEntityIds();
            CollectionAssert.DoesNotContain(freeIds, staged[0].EntityId);
            CollectionAssert.Contains(freeIds, staged[1].EntityId);
            CollectionAssert.Contains(freeIds, staged[2].EntityId);
            EntityManager.Instance.ReleaseEntity(staged[0].EntityId, EntityType.Item);
        }

        [TestMethod]
        public void ClosedConnectionLookalikeInvalidOperationPreservesIdentityAndStack()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            var expected = new InvalidOperationException(
                "The transaction object is not associated with the same connection object as this command.");
            context.AfterSave = database =>
            {
                database.Database.GetDbConnection().Close();
                ThrowAtPersistenceBoundary(expected);
            };

            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0));

            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            AssertUnchanged(context, before);
        }

        [TestMethod]
        public void ReceiverRemovedAfterTransactionStartsCannotReceiveReward()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            var removed = 0;
            context.BeforeQuery = _ =>
            {
                if (Interlocked.Exchange(ref removed, 1) == 0)
                    context.RemoveNpcFromWorld(context.Receiver);
            };

            Assert.IsFalse(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0));

            AssertUnchanged(context, before);
        }

        [TestMethod]
        public void IntegerSelectionTurnInApiIsNotPublic()
        {
            var method = typeof(MissionManager).GetMethod(
                nameof(MissionManager.TryCompleteNpcMission),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Client), typeof(ulong), typeof(uint), typeof(int) },
                null);

            Assert.IsNotNull(method);
            Assert.IsFalse(method.IsPublic);
            Assert.IsTrue(method.IsAssembly);
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
        public void SyntheticTransientExecutionStrategyLookalikePreservesIdentityAndStack()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            var expected = new InvalidOperationException(
                "Injected execution strategy wrapper.",
                new DbUpdateException("Injected transient update failure.", new TestDbException(true)));
            context.AfterSave = _ => ThrowAtPersistenceBoundary(expected);

            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0));

            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            AssertUnchanged(context, before);
        }

        [TestMethod]
        public void NonTransientExecutionStrategyLookalikePreservesIdentityAndStack()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var before = context.ReadRewardTotals();
            var expected = new InvalidOperationException(
                "Injected execution strategy lookalike.",
                new DbUpdateException("Injected non-transient update failure.", new TestDbException(false)));
            context.AfterSave = _ => ThrowAtPersistenceBoundary(expected);

            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                context.Manager.TryCompleteNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0));

            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            AssertUnchanged(context, before);
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

        private static Item[] GetStagedItems(InventoryManager.InventoryGrant grant)
        {
            var slots = (Array)typeof(InventoryManager.InventoryGrant)
                .GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(grant)!;
            return slots.Cast<object>()
                .Where(slot => slot != null)
                .Select(slot => (Item)slot.GetType()
                    .GetField("Staged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(slot))
                .Where(item => item != null)
                .ToArray();
        }

        private static ICollection GetFreeEntityIds() => (ICollection)typeof(EntityManager)
            .GetField("_freeEntityIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(EntityManager.Instance)!;

        private sealed class TestDbException : DbException
        {
            private readonly bool _isTransient;

            internal TestDbException(bool isTransient) : base("Injected database failure.")
            {
                _isTransient = isTransient;
            }

            public override bool IsTransient => _isTransient;
        }
    }
}
