extern alias RasaGame;

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampBombRetryTests
    {
        private const uint MissingScoutAreaId = 435;
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan FuseDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ArrivalDelay = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void RetryAcceptsAfterTimeoutAndRebuildsWithoutResettingTheDeadline()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));

            var youngblood = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId);
            Assert.IsNotNull(youngblood);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));

            var beforeReconnect = ReadDeadline(harness, 2005);
            harness.ReconnectFresh();

            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-dropship-debris"));
            var afterReconnect = ReadDeadline(harness, 2005);
            Assert.AreEqual(beforeReconnect.DueAtUtc, afterReconnect.DueAtUtc);
            Assert.AreEqual(CharacterMissionDeadlineState.Active, afterReconnect.State);
        }

        [TestMethod]
        public void RetryCannotBeAcceptedAfterBootcampCompletionWasAlreadyGranted()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);
            harness.SeedMission(1, 1995, (uint)MissionState.Failed, completeable: false);

            using (var unit = harness.Context.CreateChar())
            {
                unit.ExecuteTransaction(() =>
                {
                    if (!unit.CharacterQualifications.HasQualification(
                            harness.Client.Player.Id,
                            CharacterQualificationKey.BootcampComplete))
                    {
                        unit.CharacterQualifications.Add(
                            new CharacterQualificationEntry(
                                harness.Client.Player.Id,
                                CharacterQualificationKey.BootcampComplete));
                    }

                    unit.GameAccounts.UpdateCanSkipBootcamp(harness.Client.AccountEntry.Id, true);
                });
            }

            harness.Client.ReloadGameAccountEntry();

            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));
        }

        [TestMethod]
        public void RetryUseAndDetonationDoNotDuplicateTheExitHandoff()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));

            var youngblood = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId);
            Assert.IsNotNull(youngblood);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            harness.UseObjectAndRecover(dropship);
            harness.UseObjectAndRecover(dropship);

            harness.UtcNow += FuseDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[2005].Objectives[1].State);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));

            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.IsFalse(harness.Manager.TickScenarios(harness.Client));

            var vanValkenbergs = harness.BootcampMap.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList)
                .Where(creature => creature.Npc?.NpcPackageId == 2564)
                .ToArray();
            Assert.AreEqual(1, vanValkenbergs.Length);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[2005].Objectives[4].State);
        }

        [TestMethod]
        public void RetryReconnectDuringFusePreservesTheSatisfiedDeadlineAndPendingDetonation()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartTimedFinale(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));

            var youngblood = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId);
            Assert.IsNotNull(youngblood);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));

            var deadline = ReadDeadline(harness, 2005);
            harness.UtcNow = deadline.DueAtUtc - FuseDelay;

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            harness.UseObjectAndRecover(dropship);
            Assert.AreEqual(CharacterMissionDeadlineState.Satisfied, ReadDeadline(harness, 2005).State);

            harness.UtcNow += FuseDelay - TimeSpan.FromMilliseconds(1);
            harness.ReconnectFresh();

            Assert.AreEqual(CharacterMissionDeadlineState.Satisfied, ReadDeadline(harness, 2005).State);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[2005].Objectives[1].State);
            Assert.IsFalse(harness.Manager.EvaluateDeadlines(harness.Client));
            Assert.IsFalse(harness.Manager.TickScenarios(harness.Client));

            harness.UtcNow += TimeSpan.FromMilliseconds(2);
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[2005].Objectives[1].State);

            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[2005].Objectives[4].State);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));
        }

        private static void StartTimedFinale(BootcampRuntimeTestHarness.Harness harness)
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
