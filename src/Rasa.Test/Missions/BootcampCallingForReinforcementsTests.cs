extern alias RasaGame;

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ClientState = RasaGame::Rasa.Data.ClientState;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampCallingForReinforcementsTests
    {
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ArrivalDelay = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void ConradInteractionStartsTheDeadlineAndDropshipUsesConfiguredWindup()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);

            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995));

            var conrad = FindScenarioObject(harness, "bootcamp-conrad-corpse");
            harness.UseObjectAndRecover(conrad);

            using (var unit = harness.Context.CreateChar())
            {
                var deadline = unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(deadline);
                Assert.AreEqual(CharacterMissionDeadlineState.Active, deadline.State);
                Assert.AreEqual(harness.UtcNow + BombDeadline, deadline.DueAtUtc);
            }

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.AreEqual(1400U, dropship.WindupTime);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[1].State);
        }

        [TestMethod]
        public void DisconnectDuringIncompletePlantCancelsTheUseButPreservesTheExistingDeadline()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            using (var unit = harness.Context.CreateChar())
            {
                var before = unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(before);
                harness.BeginUseObject(dropship);
                harness.AdvanceRecovery(Math.Max(0L, dropship.WindupTime - 1L));

                Assert.AreEqual(1, harness.BootcampMap.PerformRecovery.Count);
                Assert.AreEqual(
                    MissionObjectiveState.Incomplete,
                    harness.Client.Player.Missions[1995].Objectives[1].State);

                harness.ReconnectFresh();

                using var reloaded = harness.Context.CreateChar();
                var after = reloaded.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(after);
                Assert.AreEqual(before.DueAtUtc, after.DueAtUtc);
                Assert.AreEqual(CharacterMissionDeadlineState.Active, after.State);
            }

            Assert.AreEqual(0, harness.BootcampMap.PerformRecovery.Count);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-dropship-debris"));
        }

        [TestMethod]
        public void PlantCompletionWithFiveSecondsRemainingAllowsTheFuseToFinishAfterExpiry()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            var dueAt = ReadDeadline(harness, 1995).DueAtUtc;
            harness.UtcNow = dueAt - TimeSpan.FromSeconds(5);

            harness.UseObjectAndRecover(dropship);

            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.AreEqual(CharacterMissionDeadlineState.Cancelled, ReadDeadline(harness, 1995).State);

            harness.UtcNow = dueAt + TimeSpan.FromSeconds(1);

            Assert.IsFalse(harness.Manager.EvaluateDeadlines(harness.Client));
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.AreEqual(
                MissionObjectiveState.Inactive,
                harness.Client.Player.Missions[1995].Objectives[4].State);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));

            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[4].State);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                2564));
        }

        [TestMethod]
        public void AbandoningDuringTheFuseFailsTheMissionAndMakesRetryAvailable()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-dropship-debris"));

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));
            Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[1995].State);
            Assert.AreEqual(
                MissionObjectiveState.Failed,
                harness.Client.Player.Missions[1995].Objectives[1].State);

            harness.UtcNow += TimeSpan.FromSeconds(30);
            Assert.IsFalse(harness.Manager.TickScenarios(harness.Client));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));

            var classification = harness.Manager.ClassifyNpcConversation(harness.Client.Player, youngblood);
            Assert.IsTrue(classification.TryGetStatus(out var status, out var missionIds));
            Assert.AreEqual(ConversationStatus.Available, status);
            CollectionAssert.Contains(missionIds, 2005U);
        }

        private static Creature StartTimedFinale(BootcampRuntimeTestHarness.Harness harness)
        {
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);
            var woundedSurvivor = harness.AddNpc(510208, 2584);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, completeable: true);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                1995));
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                woundedSurvivor.EntityId,
                1995,
                2,
                1));

            return youngblood;
        }

        private static DynamicObject FindScenarioObject(
            BootcampRuntimeTestHarness.Harness harness,
            string key) =>
            BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, key)
            ?? throw new AssertFailedException($"Missing scenario object {key}.");

        private static CharacterMissionDeadlineEntry ReadDeadline(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId)
        {
            using var unit = harness.Context.CreateChar();
            return unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, missionId)
                   ?? throw new AssertFailedException($"Missing mission deadline for {missionId}.");
        }
    }
}
