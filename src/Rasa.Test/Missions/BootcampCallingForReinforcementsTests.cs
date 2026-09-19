extern alias RasaGame;

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ClientState = RasaGame::Rasa.Data.ClientState;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Repositories.Char.CharacterMissionScenario;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampCallingForReinforcementsTests
    {
        private const uint MissingScoutAreaId = 435;
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ArrivalDelay = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void ConradInteractionStartsTheDeadlineAndDropshipUsesConfiguredWindup()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartCrashSiteScene(harness);

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
        public void MissingScoutAreaStartsOnlyTheSurvivorSceneAndReconnectRebuildsTheCorrectActorsAcrossTheConversation()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, completeable: true);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                1995));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(1995, MissingScoutAreaId)));

            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[10].State);
            Assert.AreEqual(
                MissionObjectiveState.Inactive,
                harness.Client.Player.Missions[1995].Objectives[3].State);

            var foreignMap = harness.Maps.GetOrCreatePrivateInstance(
                BootcampRuntimeTestHarness.BootcampMapContextId,
                999);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(foreignMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(foreignMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(foreignMap, "bootcamp-dropship-debris"));

            harness.ReconnectFresh();

            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));

            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                1995,
                10,
                1));

            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[1995].Objectives[10].State);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[3].State);

            harness.ReconnectFresh();

            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
        }

        [TestMethod]
        public void DisconnectDuringIncompletePlantCancelsTheUseButPreservesTheExistingDeadline()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartCrashSiteScene(harness);
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
            StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            var dueAt = ReadDeadline(harness, 1995).DueAtUtc;
            harness.UtcNow = dueAt - TimeSpan.FromSeconds(5);

            harness.UseObjectAndRecover(dropship);

            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.AreEqual(CharacterMissionDeadlineState.Satisfied, ReadDeadline(harness, 1995).State);

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
        public void AbandoningDuringActiveDeadlineFailsAndResetsMission1995OnceWhileUnlockingRetry()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.IsTrue(dropship.IsEnabled);

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            AssertFailedMissionReset(
                harness,
                missionId: 1995,
                objectiveId: 1,
                resetScenarioId: 5,
                dropship,
                expectRetryAvailable: true,
                retryMissionId: 2005,
                retryNpc: youngblood);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));
            var retryDeadline = ReadDeadline(harness, 2005);

            Assert.IsFalse(harness.Manager.TryAbandon(harness.Client, 1995));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);
            Assert.AreEqual(CharacterMissionDeadlineState.Active, ReadDeadline(harness, 2005).State);
            Assert.AreEqual(retryDeadline.DueAtUtc, ReadDeadline(harness, 2005).DueAtUtc);
        }

        [TestMethod]
        public void AbandoningDuringTheFuseFailsTheMissionAndMakesRetryAvailable()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            harness.UseObjectAndRecover(dropship);

            Assert.IsFalse(dropship.IsEnabled);

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            AssertFailedMissionReset(
                harness,
                missionId: 1995,
                objectiveId: 1,
                resetScenarioId: 5,
                dropship,
                expectRetryAvailable: true,
                retryMissionId: 2005,
                retryNpc: youngblood);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));
            var retryDeadline = ReadDeadline(harness, 2005);

            Assert.IsFalse(harness.Manager.TryAbandon(harness.Client, 1995));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);
            Assert.AreEqual(CharacterMissionDeadlineState.Active, ReadDeadline(harness, 2005).State);
            Assert.AreEqual(retryDeadline.DueAtUtc, ReadDeadline(harness, 2005).DueAtUtc);
        }

        private static Creature StartCrashSiteScene(BootcampRuntimeTestHarness.Harness harness)
        {
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, completeable: true);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                1995));
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(1995, MissingScoutAreaId)));
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId)
                ?? throw new AssertFailedException("Missing wounded survivor.");
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                1995,
                10,
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

        private static void AssertFailedMissionReset(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId,
            uint objectiveId,
            uint resetScenarioId,
            DynamicObject dropship,
            bool expectRetryAvailable,
            uint retryMissionId,
            Creature retryNpc)
        {
            Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[missionId].State);
            Assert.AreEqual(
                MissionObjectiveState.Failed,
                harness.Client.Player.Missions[missionId].Objectives[objectiveId].State);
            Assert.AreEqual(CharacterMissionDeadlineState.Cancelled, ReadDeadline(harness, missionId).State);
            Assert.IsTrue(dropship.IsEnabled);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-dropship-debris"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 39));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 50));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId));
            AssertResetScenarioRecordedOnce(harness, missionId, resetScenarioId, stepCount: 4);

            var classification = harness.Manager.ClassifyNpcConversation(harness.Client.Player, retryNpc);
            if (!classification.TryGetStatus(out _, out var missionIds))
                missionIds = new System.Collections.Generic.List<uint>();

            if (expectRetryAvailable)
                CollectionAssert.Contains(missionIds, retryMissionId);
            else
                CollectionAssert.DoesNotContain(missionIds, retryMissionId);
        }

        private static void AssertResetScenarioRecordedOnce(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId,
            uint resetScenarioId,
            uint stepCount)
        {
            var keys = ReadScenarioKeys(harness, missionId);
            for (var stepId = 1U; stepId <= stepCount; stepId++)
                Assert.AreEqual(
                    1,
                    keys.Count(key => key == $"scenario:{resetScenarioId}:step:{stepId}"),
                    $"Expected reset scenario {resetScenarioId} step {stepId} exactly once.");
        }

        private static string[] ReadScenarioKeys(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId)
        {
            using var unit = harness.Context.CreateChar();
            return unit.CharacterMissionScenario.Get(harness.Client.Player.Id, missionId)
                .Select(entry => entry.StepKey)
                .ToArray();
        }
    }
}
