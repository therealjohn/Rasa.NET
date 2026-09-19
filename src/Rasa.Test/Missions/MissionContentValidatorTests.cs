using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Structures.Missions;
    using Rasa.Structures.World;

    [TestClass]
    public class MissionContentValidatorTests
    {
        [TestMethod]
        public void ValidatorAcceptsACompleteOperationalGraph()
        {
            var fixture = MissionContentFixture.CreateValid();
            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var report = new MissionContentValidator().Validate(
                snapshot,
                fixture.CreateWorldUnitOfWork());

            Assert.AreEqual(0, report.Diagnostics.Count);
            Assert.IsFalse(report.BlocksReadiness);
        }

        [TestMethod]
        [DynamicData(nameof(GetFailureCases))]
        public void ValidatorReportsEachDiagnosticClass(
            string _,
            Action<MissionContentFixture> mutate,
            string expectedCode)
        {
            var fixture = MissionContentFixture.CreateValid();
            mutate(fixture);
            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var report = new MissionContentValidator().Validate(
                snapshot,
                fixture.CreateWorldUnitOfWork());

            CollectionAssert.Contains(
                report.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray(),
                expectedCode);
            Assert.IsTrue(report.BlocksReadiness);
        }

        [TestMethod]
        public void ValidatorSortsDiagnosticsDeterministicallyForOperators()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Objectives[0].ClientBodyTextId = 0;
            fixture.NpcPackageIds.Clear();
            fixture.Actions[0].Kind = (MissionActionKind)99;
            fixture.Indicators[0].ObjectiveId = 10;
            fixture.Indicators[0].Radius = 0;

            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var report = new MissionContentValidator().Validate(
                snapshot,
                fixture.CreateWorldUnitOfWork());

            CollectionAssert.AreEqual(
                new[]
                {
                    "missing-client-text",
                    "invalid-radius",
                    "unsupported-action",
                    "missing-npc-package"
                },
                report.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        }

        public static IEnumerable<object[]> GetFailureCases()
        {
            yield return Case("duplicate mission", fixture =>
            {
                fixture.Definitions.Add(new MissionContentDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_12",
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 3001,
                    GiverId = 101,
                    ReceiverId = 102,
                    Level = 9,
                    GroupType = 2,
                    CategoryId = 3,
                    Shareable = false,
                    RadioCompleteable = false,
                    Comment = "Competing authored revision"
                });
            }, "duplicate-mission-id");

            yield return Case("duplicate objective", fixture =>
            {
                fixture.Objectives.Add(new MissionObjectiveDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 9999,
                    ClientBodyTextId = 9998,
                    Ordinal = 9,
                    InitialState = (byte)MissionObjectiveState.Inactive,
                    IsRequired = true,
                    Comment = "Duplicate objective"
                });
            }, "duplicate-objective-id");

            yield return Case("missing objective", fixture => fixture.Objectives.Clear(), "missing-objective-id");

            yield return Case("transition cycle", fixture =>
            {
                fixture.Objectives.Add(new MissionObjectiveDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 12,
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 2301,
                    ClientBodyTextId = 2302,
                    Ordinal = 3,
                    InitialState = (byte)MissionObjectiveState.Inactive,
                    IsRequired = true,
                    Comment = "Cycle objective"
                });
                fixture.Transitions.Add(new MissionObjectiveTransitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 11,
                    TransitionId = 21,
                    Requirement = MissionContentRequirement.Required,
                    Sequence = 2,
                    FromState = (byte)MissionObjectiveState.Incomplete,
                    ToState = (byte)MissionObjectiveState.Completed,
                    Comment = "Cycle transition"
                });
                fixture.Actions.AddRange(
                    new MissionActionEntry
                    {
                        MissionId = 321,
                        ContentRevision = "deployment_11",
                        ObjectiveId = 11,
                        TransitionId = 21,
                        ActionId = 21,
                        Requirement = MissionContentRequirement.Required,
                        Kind = MissionActionKind.RevealObjective,
                        Sequence = 1,
                        TargetObjectiveId = 12,
                        Comment = "Reveal 12"
                    },
                    new MissionActionEntry
                    {
                        MissionId = 321,
                        ContentRevision = "deployment_11",
                        ObjectiveId = 12,
                        TransitionId = 22,
                        ActionId = 22,
                        Requirement = MissionContentRequirement.Required,
                        Kind = MissionActionKind.RevealObjective,
                        Sequence = 1,
                        TargetObjectiveId = 10,
                        Comment = "Reveal 10"
                    });
                fixture.Transitions.Add(new MissionObjectiveTransitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 12,
                    TransitionId = 22,
                    Requirement = MissionContentRequirement.Required,
                    Sequence = 3,
                    FromState = (byte)MissionObjectiveState.Incomplete,
                    ToState = (byte)MissionObjectiveState.Completed,
                    Comment = "Cycle back"
                });
            }, "transition-cycle");

            yield return Case("missing target", fixture =>
            {
                fixture.Actions[1].TargetObjectiveId = 999;
            }, "missing-target");

            yield return Case("invalid graph", fixture =>
            {
                fixture.Actions.RemoveAll(action => action.Kind == MissionActionKind.RevealObjective);
            }, "invalid-objective-graph");

            yield return Case("unsupported trigger", fixture =>
            {
                fixture.Triggers[0].Kind = (MissionTriggerKind)99;
            }, "unsupported-trigger");

            yield return Case("unsupported area trigger runtime", fixture =>
            {
                fixture.Triggers[0].Kind = MissionTriggerKind.AreaEntered;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].AreaId = 30;
            }, "unsupported-trigger");

            yield return Case("unsupported timer trigger runtime", fixture =>
            {
                fixture.Triggers[0].Kind = MissionTriggerKind.TimerElapsed;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].DurationSeconds = 5;
            }, "unsupported-trigger");

            yield return Case("unsupported action", fixture =>
            {
                fixture.Actions[0].Kind = (MissionActionKind)99;
            }, "unsupported-action");

            yield return Case("unsupported scenario action runtime", fixture =>
            {
                fixture.Actions[0].Kind = MissionActionKind.StartScenario;
                fixture.Actions[0].TargetObjectiveId = null;
                fixture.Actions[0].ObjectiveState = null;
                fixture.Actions[0].ScenarioId = 60;
            }, "unsupported-action");

            yield return Case("unsupported spawn action runtime", fixture =>
            {
                fixture.Actions[0].Kind = MissionActionKind.ActivateSpawnGroup;
                fixture.Actions[0].TargetObjectiveId = null;
                fixture.Actions[0].ObjectiveState = null;
                fixture.Actions[0].SpawnGroupId = 50;
            }, "unsupported-action");

            yield return Case("unsupported indicator action runtime", fixture =>
            {
                fixture.Actions[0].Kind = MissionActionKind.ShowIndicator;
                fixture.Actions[0].TargetObjectiveId = null;
                fixture.Actions[0].ObjectiveState = null;
                fixture.Actions[0].IndicatorId = 70;
            }, "unsupported-action");

            yield return Case("unsupported player flag action runtime", fixture =>
            {
                fixture.Actions[0].Kind = MissionActionKind.SetPlayerFlag;
                fixture.Actions[0].TargetObjectiveId = null;
                fixture.Actions[0].ObjectiveState = null;
                fixture.Actions[0].PlayerFlagId = 7;
                fixture.Actions[0].PlayerFlagValue = 2;
            }, "unsupported-action");

            yield return Case("missing npc package", fixture =>
            {
                fixture.NpcPackageIds.Clear();
            }, "missing-npc-package");

            yield return Case("missing item template", fixture =>
            {
                fixture.ItemTemplateClasses.Clear();
            }, "missing-item-template");

            yield return Case("missing entity class", fixture =>
            {
                fixture.EntityClassIds.Clear();
            }, "missing-entity-class");

            yield return Case("missing map context", fixture =>
            {
                fixture.MapContextIds.Clear();
            }, "missing-map-context");

            yield return Case("missing area", fixture =>
            {
                fixture.Areas.Clear();
                fixture.SpawnGroups[0].AreaId = 30;
            }, "missing-area");

            yield return Case("missing spawn group", fixture =>
            {
                fixture.SpawnGroups.Clear();
                fixture.Actions.Add(new MissionActionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    TransitionId = 20,
                    ActionId = 99,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionActionKind.ActivateSpawnGroup,
                    Sequence = 99,
                    SpawnGroupId = 50,
                    Comment = "Missing spawn group"
                });
            }, "missing-spawn-group");

            yield return Case("missing scenario", fixture =>
            {
                fixture.Scenarios.Clear();
                fixture.Actions.Add(new MissionActionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    TransitionId = 20,
                    ActionId = 98,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionActionKind.StartScenario,
                    Sequence = 98,
                    ScenarioId = 60,
                    Comment = "Missing scenario"
                });
            }, "missing-scenario");

            yield return Case("missing reward", fixture =>
            {
                fixture.Rewards.Clear();
                fixture.RewardItems.Clear();
            }, "missing-reward");

            yield return Case("missing reward reference", fixture =>
            {
                fixture.Actions.RemoveAll(action => action.Kind == MissionActionKind.GrantReward);
            }, "missing-reward-reference");

            yield return Case("ambiguous reward reference", fixture =>
            {
                fixture.Rewards.Add(new MissionRewardDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    RewardId = 41,
                    Requirement = MissionContentRequirement.Required,
                    Experience = 10,
                    Credits = 1,
                    Prestige = 0,
                    SelectionCount = 0,
                    Comment = "Alternate reward"
                });
                fixture.RewardItems.Add(new MissionRewardItemEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    RewardId = 41,
                    ItemId = 1,
                    Kind = MissionRewardItemKind.Fixed,
                    ItemTemplateId = 28,
                    Quantity = 1
                });
                fixture.Actions.Add(new MissionActionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    TransitionId = 20,
                    ActionId = 97,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionActionKind.GrantReward,
                    Sequence = 97,
                    RewardId = 41,
                    Comment = "Grant alternate reward"
                });
            }, "ambiguous-reward-reference");

            yield return Case("missing client text", fixture =>
            {
                fixture.Objectives[0].ClientBodyTextId = 0;
            }, "missing-client-text");

            yield return Case("missing counter text binding", fixture =>
            {
                fixture.Triggers[0].Kind = MissionTriggerKind.ProgressEvent;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].EventKind = (byte)MissionProgressEventKind.CreatureKilled;
                fixture.Triggers[0].SubjectId = 501;
                fixture.Triggers[0].CounterId = 0;
                fixture.Triggers[0].InitialValue = 0;
                fixture.Triggers[0].TargetValue = 3;
                fixture.Triggers[0].SourceSpawnResolved = null;
            }, "invalid-counter-text-binding");

            yield return Case("out of range counter text binding", fixture =>
            {
                fixture.Objectives[0].ClientCounter0TextId = 9100;
                fixture.Objectives[0].ClientCounter1TextId = 9101;
                fixture.Objectives[0].ClientCounter2TextId = 9102;
                fixture.Triggers[0].Kind = MissionTriggerKind.ProgressEvent;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].EventKind = (byte)MissionProgressEventKind.CreatureKilled;
                fixture.Triggers[0].SubjectId = 501;
                fixture.Triggers[0].CounterId = 3;
                fixture.Triggers[0].InitialValue = 0;
                fixture.Triggers[0].TargetValue = 3;
                fixture.Triggers[0].SourceSpawnResolved = null;
            }, "invalid-counter-text-binding");

            yield return Case("invalid radius", fixture =>
            {
                fixture.Indicators[0].Radius = 0;
            }, "invalid-radius");

            yield return Case("invalid quantity", fixture =>
            {
                fixture.RewardItems[0].Quantity = 0;
            }, "invalid-quantity");

            yield return Case("invalid delay", fixture =>
            {
                fixture.Triggers[0].Kind = MissionTriggerKind.TimerElapsed;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].DurationSeconds = 0;
            }, "invalid-delay");

            yield return Case("invalid reward selection", fixture =>
            {
                fixture.Rewards[0].SelectionCount = 1;
                fixture.RewardItems.RemoveAll(item => item.Kind == MissionRewardItemKind.Selectable);
            }, "invalid-reward-selection");

            yield return Case("invalid progress event", fixture =>
            {
                fixture.Triggers[0].Kind = MissionTriggerKind.ProgressEvent;
                fixture.Triggers[0].NpcPackageId = null;
                fixture.Triggers[0].PlayerFlagId = null;
                fixture.Triggers[0].EventKind = (byte)MissionProgressEventKind.CreatureKilled;
                fixture.Triggers[0].SubjectId = 501;
                fixture.Triggers[0].CounterId = null;
                fixture.Triggers[0].InitialValue = null;
                fixture.Triggers[0].TargetValue = null;
                fixture.Triggers[0].SourceSpawnResolved = null;
                fixture.Triggers.Add(new MissionTriggerEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    TransitionId = 20,
                    TriggerId = 2,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionTriggerKind.ProgressEvent,
                    Sequence = 2,
                    EventKind = (byte)MissionProgressEventKind.CreatureKilled,
                    SubjectId = 502,
                    Comment = "Invalid mixed multi-trigger exact rule"
                });
            }, "invalid-progress-event");

            yield return Case("cross revision reference", fixture =>
            {
                fixture.Definitions.Add(new MissionContentDefinitionEntry
                {
                    MissionId = 322,
                    ContentRevision = "legacy",
                    Requirement = MissionContentRequirement.Optional,
                    ClientNameTextId = 0,
                    GiverId = 1,
                    ReceiverId = 2,
                    Level = 1,
                    GroupType = 1,
                    CategoryId = 1,
                    Shareable = false,
                    RadioCompleteable = false,
                    Comment = "Legacy chain member"
                });
                fixture.Prerequisites.Add(new MissionPrerequisiteEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    PrerequisiteId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionPrerequisiteKind.MissionCompleted,
                    RequiredMissionId = 322,
                    RequiredMissionState = (byte)MissionState.Completed,
                    Comment = "Cross revision prerequisite"
                });
            }, "cross-revision-reference");

            yield return Case("required chain inactive", fixture =>
            {
                fixture.Definitions.Add(new MissionContentDefinitionEntry
                {
                    MissionId = 322,
                    ContentRevision = "deployment_11",
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 3201,
                    GiverId = 1,
                    ReceiverId = 2,
                    Level = 1,
                    GroupType = 1,
                    CategoryId = 1,
                    Shareable = false,
                    RadioCompleteable = false,
                    Comment = "Required predecessor"
                });
                fixture.Prerequisites.Add(new MissionPrerequisiteEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    PrerequisiteId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionPrerequisiteKind.MissionCompleted,
                    RequiredMissionId = 322,
                    RequiredMissionState = (byte)MissionState.Completed,
                    Comment = "Required prerequisite"
                });
            }, "required-chain-inactive");
        }

        private static object[] Case(
            string name,
            Action<MissionContentFixture> mutate,
            string expectedCode) =>
            new object[] { name, mutate, expectedCode };
    }
}
