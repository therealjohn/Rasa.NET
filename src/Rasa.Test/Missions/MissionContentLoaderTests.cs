using System;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Managers;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Structures;
    using Rasa.Structures.Missions;

    [TestClass]
    public class MissionContentLoaderTests
    {
        [TestMethod]
        public void LoaderPrefersAuthoredRevisionAndBuildsImmutableRuntimeDefinitions()
        {
            var fixture = MissionContentFixture.CreateValid();
            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());

            Assert.AreEqual(1, snapshot.Definitions.Count);
            var content = snapshot.Definitions[321];
            Assert.AreEqual("deployment_11", content.ContentRevision);
            Assert.AreEqual(2, content.Mission.Objectives.Count);
            Assert.AreEqual(1, content.Rewards.Count);
            Assert.AreEqual(1, content.Scenarios.Count);

            var firstObjective = content.Mission.Objectives[10];
            Assert.AreEqual(1, firstObjective.Conversations.Count);
            Assert.AreEqual(77U, firstObjective.Conversations[0].NpcPackageId);
            CollectionAssert.AreEqual(new uint[] { 11 }, firstObjective.RevealedObjectiveIds.ToArray());
            CollectionAssert.AreEqual(new uint[] { 11 }, firstObjective.ActivatedObjectiveIds.ToArray());

            var reward = content.Rewards[40];
            Assert.AreEqual(75U, reward.Credits);
            Assert.AreEqual(1, reward.FixedItems.Count);
            Assert.AreEqual(1, reward.SelectableItems.Count);

            var firstStep = content.Scenarios[60].Steps.Single();
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.EmitScenarioEvent, firstStep.Kind);
            Assert.AreEqual(1U, firstStep.ScenarioEventId);

            var transition = content.Transitions.Values.Single();
            Assert.AreEqual(1, transition.Triggers.Count);
            Assert.AreEqual(4, transition.Actions.Count);
        }

        [TestMethod]
        public void LoaderBuildsImmutableScenarioStepDefinitionsForApprovedVocabulary()
        {
            var fixture = MissionContentFixture.CreateValid();
            ConfigureApprovedScenarioVocabulary(fixture);

            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var steps = snapshot.Definitions[321].Scenarios[60].Steps;

            Assert.AreEqual(20, steps.Count);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.SpawnGroup, steps[0].Kind);
            Assert.AreEqual(50U, steps[0].SpawnGroupId);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.EnableInteraction, steps[2].Kind);
            Assert.AreEqual(3147U, steps[2].EntityClassId);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.DisableInteraction, steps[3].Kind);
            Assert.AreEqual(1U, steps[3].SpawnId);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.StartDeadline, steps[8].Kind);
            Assert.AreEqual(30000U, steps[8].DelayMilliseconds);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.GrantSkillAbility, steps[11].Kind);
            Assert.AreEqual(901U, steps[11].SkillId);
            Assert.AreEqual(194U, steps[11].AbilityId);
            Assert.AreEqual((byte)2, steps[11].SkillLevel);
            Assert.AreEqual((byte)3, steps[11].AbilitySlot);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.PlayTutorial, steps[12].Kind);
            Assert.AreEqual((uint)Rasa.Data.TutorialId.Tutmissiongiver, steps[12].TutorialId);
            Assert.AreEqual(88U, steps[12].AudioSetId);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.ScheduleScenario, steps[13].Kind);
            Assert.AreEqual(60U, steps[13].TargetScenarioId);
            Assert.AreEqual(5000U, steps[13].DelayMilliseconds);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.ResetAttempt, steps[14].Kind);
            Assert.AreEqual("bootcamp-scout", steps[14].AttemptKey);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.ResetAttempt, steps[15].Kind);
            Assert.AreEqual(60U, steps[15].TargetScenarioId);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.TransferPlayer, steps[17].Kind);
            Assert.AreEqual(1220U, steps[17].MapContextId);
            Assert.AreEqual(1D, steps[17].PosX);
            Assert.AreEqual(2D, steps[17].PosY);
            Assert.AreEqual(3D, steps[17].PosZ);
            Assert.AreEqual(1.5D, steps[17].Orientation);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.SetQualification, steps[18].Kind);
            Assert.AreEqual(Rasa.Structures.Char.CharacterQualificationKey.BootcampComplete, steps[18].QualificationKey);
            Assert.AreEqual((byte)1, steps[18].QualificationValue);
            Assert.AreEqual(Rasa.Structures.World.MissionScenarioStepKind.SetAccountSkipEntitlement, steps[19].Kind);
            Assert.AreEqual(true, steps[19].AccountSkipEntitlement);

            fixture.ScenarioSteps[0].SpawnGroupId = 999;
            Assert.AreEqual(50U, steps[0].SpawnGroupId);
        }

        [TestMethod]
        public void LoaderBuildsProgressRulesFromSupportedProgressTransitions()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Triggers.Clear();
            fixture.Actions.Clear();
            fixture.Transitions.Clear();
            fixture.Objectives.RemoveAll(objective => objective.ObjectiveId == 11);
            fixture.Transitions.Add(new Rasa.Structures.World.MissionObjectiveTransitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Sequence = 1,
                FromState = (byte)Rasa.Data.MissionObjectiveState.Incomplete,
                ToState = (byte)Rasa.Data.MissionObjectiveState.Completed,
                Comment = "Kill one creature"
            });
            fixture.Triggers.Add(new Rasa.Structures.World.MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                TriggerId = 1,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Kind = Rasa.Structures.World.MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)Rasa.Data.MissionProgressEventKind.CreatureKilled,
                SubjectId = 501,
                Comment = "Kill creature 501"
            });

            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var rule = snapshot.Definitions[321].Mission.Objectives[10].ProgressRule;

            Assert.IsNotNull(rule);
            Assert.AreEqual(Rasa.Data.MissionProgressEventKind.CreatureKilled, rule.Kind);
            CollectionAssert.AreEqual(new uint[] { 501 }, rule.Subjects.ToArray());
        }

        [TestMethod]
        public void LoaderBuildsAreaAndDeadlineRulesFromSupportedTriggerKinds()
        {
            var areaFixture = CreatePureProgressFixture();
            areaFixture.Triggers[0].Kind = Rasa.Structures.World.MissionTriggerKind.AreaEntered;
            areaFixture.Triggers[0].EventKind = null;
            areaFixture.Triggers[0].SubjectId = null;
            areaFixture.Triggers[0].AreaId = 30;

            var areaSnapshot = new MissionContentLoader().Load(areaFixture.CreateRepository());
            var areaRule = areaSnapshot.Definitions[321].Mission.Objectives[10].ProgressRule;

            Assert.IsNotNull(areaRule);
            Assert.AreEqual(Rasa.Data.MissionProgressEventKind.AreaEntered, areaRule.Kind);
            Assert.AreEqual((uint)321, areaRule.ScopeId);
            CollectionAssert.AreEqual(new uint[] { 30 }, areaRule.Subjects.ToArray());

            var timerFixture = CreatePureProgressFixture();
            timerFixture.Triggers[0].Kind = Rasa.Structures.World.MissionTriggerKind.TimerElapsed;
            timerFixture.Triggers[0].EventKind = null;
            timerFixture.Triggers[0].SubjectId = null;
            timerFixture.Triggers[0].DurationSeconds = 5;

            var timerSnapshot = new MissionContentLoader().Load(timerFixture.CreateRepository());
            var timerRule = timerSnapshot.Definitions[321].Mission.Objectives[10].ProgressRule;

            Assert.IsNotNull(timerRule);
            Assert.AreEqual(Rasa.Data.MissionProgressEventKind.DeadlineElapsed, timerRule.Kind);
            Assert.AreEqual((uint)321, timerRule.ScopeId);
            Assert.AreEqual((uint)10, timerRule.Subjects.Single());
            Assert.AreEqual((uint)5, timerRule.DurationSeconds);
        }

        [TestMethod]
        public void LoaderFailsClosedInsteadOfChoosingTheFirstExecutableProgressPath()
        {
            var fixture = CreatePureProgressFixture();
            AddProgressTransition(
                fixture,
                objectiveId: 10,
                transitionId: 21,
                triggerId: 2,
                subjectId: 502);

            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var mission = snapshot.Definitions[321].Mission;
            var objective = mission.Objectives[10];

            Assert.IsFalse(mission.IsOperational);
            StringAssert.Contains(
                mission.OperationalDiagnostic,
                "objective 10 has multiple executable transition paths");
            Assert.IsNull(objective.ProgressRule);
            CollectionAssert.AreEqual(Array.Empty<uint>(), objective.RevealedObjectiveIds.ToArray());
            CollectionAssert.AreEqual(Array.Empty<uint>(), objective.ActivatedObjectiveIds.ToArray());
            Assert.AreEqual(0, objective.Conversations.Count);
        }

        [TestMethod]
        public void LoaderScopesTransitionDefinitionsByObjectiveAndTransitionId()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Transitions[0].TransitionId = 1;
            fixture.Triggers[0].TransitionId = 1;
            fixture.Actions.ForEach(action => action.TransitionId = 1);
            fixture.Transitions.Add(new Rasa.Structures.World.MissionObjectiveTransitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 11,
                TransitionId = 1,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Sequence = 2,
                FromState = (byte)Rasa.Data.MissionObjectiveState.Incomplete,
                ToState = (byte)Rasa.Data.MissionObjectiveState.Completed,
                Comment = "Objective-local transition id"
            });
            fixture.Triggers.Add(new Rasa.Structures.World.MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 11,
                TransitionId = 1,
                TriggerId = 2,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Kind = Rasa.Structures.World.MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)Rasa.Data.MissionProgressEventKind.CreatureKilled,
                SubjectId = 501,
                Comment = "Kill creature 501"
            });
            var snapshot = new MissionContentLoader().Load(fixture.CreateRepository());
            var content = snapshot.Definitions[321];
            var objectiveTenTransition = content.Transitions[(10U, 1U)];
            var objectiveElevenTransition = content.Transitions[(11U, 1U)];

            Assert.AreEqual(2, content.Transitions.Count);
            Assert.AreEqual(Rasa.Structures.World.MissionTriggerKind.Conversation,
                objectiveTenTransition.Triggers.Single().Kind);
            Assert.AreEqual(4, objectiveTenTransition.Actions.Count);
            Assert.AreEqual(Rasa.Structures.World.MissionTriggerKind.ProgressEvent,
                objectiveElevenTransition.Triggers.Single().Kind);
            Assert.AreEqual(0, objectiveElevenTransition.Actions.Count);
            Assert.IsNotNull(content.Mission.Objectives[11].ProgressRule);
            Assert.AreEqual(Rasa.Data.MissionProgressEventKind.CreatureKilled,
                content.Mission.Objectives[11].ProgressRule.Kind);
        }

        [TestMethod]
        public void MissionManagerUsesReferencedGrantRewardInsteadOfLowestRewardId()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Rewards.Add(new Rasa.Structures.World.MissionRewardDefinitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                RewardId = 39,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Experience = 1,
                Credits = 5,
                Prestige = 0,
                SelectionCount = 0,
                Comment = "Lower id reward"
            });
            fixture.RewardItems.Add(new Rasa.Structures.World.MissionRewardItemEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                RewardId = 39,
                ItemId = 1,
                Kind = Rasa.Structures.World.MissionRewardItemKind.Fixed,
                ItemTemplateId = 28,
                Quantity = 1
            });
            using var context = MissionTestContext.WithCustomDefinitions(
                new System.Collections.Generic.Dictionary<uint, Mission>());
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new System.Collections.Generic.Dictionary<uint, Mission>());

            var report = manager.LoadMissions();

            Assert.IsFalse(report.BlocksReadiness);
            Assert.IsTrue(manager.TryGetRewardInfo(321, out var rewardInfo));
            Assert.AreEqual(75U, rewardInfo.FixedReward.Credits[Rasa.Data.CurencyType.Credits]);
            Assert.AreEqual(10U, rewardInfo.FixedReward.Credits[Rasa.Data.CurencyType.Prestige]);
            Assert.AreEqual(1, rewardInfo.SelectableReward.Count);
        }

        [TestMethod]
        public void MissionManagerLoadsOperationalDatabaseMissionFromMissionContent()
        {
            var fixture = MissionContentFixture.CreateValid();
            using var context = MissionTestContext.WithCustomDefinitions(
                new System.Collections.Generic.Dictionary<uint, Mission>());
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new System.Collections.Generic.Dictionary<uint, Mission>());

            var report = manager.LoadMissions();
            var giver = context.AddNpc(101);

            Assert.IsFalse(report.BlocksReadiness);
            Assert.IsTrue(manager.LoadedMissions[321].IsOperational);
            Assert.IsTrue(manager.TryGetRewardInfo(321, out var rewardInfo));
            Assert.AreEqual(1, rewardInfo.SelectableReward.Count);
            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
        }

        [TestMethod]
        public void MissionManagerReturnsBlockingReportForRequiredInvalidMissionContent()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Objectives[0].ClientBodyTextId = 0;
            using var context = MissionTestContext.WithCustomDefinitions(
                new System.Collections.Generic.Dictionary<uint, Mission>());
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new System.Collections.Generic.Dictionary<uint, Mission>());

            var report = manager.LoadMissions();

            Assert.IsTrue(report.BlocksReadiness);
            Assert.IsFalse(manager.LoadedMissions[321].IsOperational);
            StringAssert.Contains(
                manager.LoadedMissions[321].OperationalDiagnostic,
                "objective text bindings are incomplete");
        }

        [TestMethod]
        public void MissionManagerKeepsProgressTransitionsWithActionsInactive()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Triggers[0].Kind = Rasa.Structures.World.MissionTriggerKind.ProgressEvent;
            fixture.Triggers[0].NpcPackageId = null;
            fixture.Triggers[0].PlayerFlagId = null;
            fixture.Triggers[0].EventKind = (byte)Rasa.Data.MissionProgressEventKind.CreatureKilled;
            fixture.Triggers[0].SubjectId = 501;
            using var context = MissionTestContext.WithCustomDefinitions(
                new System.Collections.Generic.Dictionary<uint, Mission>());
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new System.Collections.Generic.Dictionary<uint, Mission>());

            var report = manager.LoadMissions();
            var giver = context.AddNpc(101);

            Assert.IsTrue(report.BlocksReadiness);
            Assert.IsFalse(manager.LoadedMissions[321].IsOperational);
            StringAssert.Contains(
                manager.LoadedMissions[321].OperationalDiagnostic,
                "progress-triggered transitions cannot execute actions");
            Assert.IsFalse(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
        }

        [TestMethod]
        public void MissionManagerLoadsOperationalPureProgressCompletionMission()
        {
            var fixture = CreatePureProgressFixture();
            using var context = MissionTestContext.WithCustomDefinitions(
                new System.Collections.Generic.Dictionary<uint, Mission>());
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new System.Collections.Generic.Dictionary<uint, Mission>());

            var report = manager.LoadMissions();
            var giver = context.AddNpc(101);

            Assert.IsFalse(report.BlocksReadiness);
            Assert.IsTrue(manager.LoadedMissions[321].IsOperational);
            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
        }

        private static MissionContentFixture CreatePureProgressFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Triggers.Clear();
            fixture.Actions.Clear();
            fixture.Transitions.Clear();
            fixture.Rewards.Clear();
            fixture.RewardItems.Clear();
            fixture.Indicators.Clear();
            fixture.Objectives.RemoveAll(objective => objective.ObjectiveId == 11);
            AddProgressTransition(
                fixture,
                objectiveId: 10,
                transitionId: 20,
                triggerId: 1,
                subjectId: 501);
            return fixture;
        }

        private static void ConfigureApprovedScenarioVocabulary(MissionContentFixture fixture)
        {
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.AddRange(new[]
            {
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 1,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.SpawnGroup,
                    Sequence = 1,
                    SpawnGroupId = 50,
                    Comment = "Spawn bootcamp group"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 2,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.DespawnGroup,
                    Sequence = 2,
                    SpawnGroupId = 50,
                    Comment = "Despawn bootcamp group"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 3,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.EnableInteraction,
                    Sequence = 3,
                    EntityClassId = 3147,
                    Comment = "Enable interaction on the stable world object"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 4,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.DisableInteraction,
                    Sequence = 4,
                    SpawnGroupId = 50,
                    SpawnId = 1,
                    Comment = "Disable interaction on the spawned actor"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 5,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.RevealObjective,
                    Sequence = 5,
                    TargetObjectiveId = 11,
                    Comment = "Reveal follow-up objective"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 6,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.ActivateObjective,
                    Sequence = 6,
                    TargetObjectiveId = 11,
                    Comment = "Activate follow-up objective"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 7,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.CompleteObjective,
                    Sequence = 7,
                    TargetObjectiveId = 10,
                    Comment = "Complete current objective"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 8,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.FailObjective,
                    Sequence = 8,
                    TargetObjectiveId = 11,
                    Comment = "Fail follow-up objective"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 9,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.StartDeadline,
                    Sequence = 9,
                    DelayMilliseconds = 30000,
                    Comment = "Start a 30 second deadline"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 10,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.CancelDeadline,
                    Sequence = 10,
                    Comment = "Cancel active deadline"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 11,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.GrantRewardPackage,
                    Sequence = 11,
                    RewardId = 40,
                    Comment = "Grant the authored reward package"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 12,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.GrantSkillAbility,
                    Sequence = 12,
                    SkillId = 901,
                    AbilityId = 194,
                    SkillLevel = 2,
                    AbilitySlot = 3,
                    Comment = "Grant a skill and tray slot"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 13,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.PlayTutorial,
                    Sequence = 13,
                    TutorialId = (uint)Rasa.Data.TutorialId.Tutmissiongiver,
                    AudioSetId = 88,
                    Comment = "Play the mission giver tutorial"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 14,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.ScheduleScenario,
                    Sequence = 14,
                    TargetScenarioId = 60,
                    DelayMilliseconds = 5000,
                    Comment = "Schedule the follow-up scenario"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 15,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.ResetAttempt,
                    Sequence = 15,
                    AttemptKey = "bootcamp-scout",
                    Comment = "Reset by attempt key"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 16,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.ResetAttempt,
                    Sequence = 16,
                    TargetScenarioId = 60,
                    Comment = "Reset by scenario id"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 17,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.EmitScenarioEvent,
                    Sequence = 17,
                    ScenarioEventId = 7,
                    Comment = "Emit a scenario event"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 18,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.TransferPlayer,
                    Sequence = 18,
                    MapContextId = 1220,
                    PosX = 1,
                    PosY = 2,
                    PosZ = 3,
                    Orientation = 1.5,
                    Comment = "Transfer to the authored map marker"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 19,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.SetQualification,
                    Sequence = 19,
                    QualificationKey = Rasa.Structures.Char.CharacterQualificationKey.BootcampComplete,
                    QualificationValue = 1,
                    Comment = "Mark bootcamp complete"
                },
                new Rasa.Structures.World.MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 20,
                    Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                    Kind = Rasa.Structures.World.MissionScenarioStepKind.SetAccountSkipEntitlement,
                    Sequence = 20,
                    AccountSkipEntitlement = true,
                    Comment = "Grant the account skip entitlement"
                }});
        }

        private static void AddProgressTransition(
            MissionContentFixture fixture,
            uint objectiveId,
            uint transitionId,
            uint triggerId,
            uint subjectId)
        {
            fixture.Transitions.Add(new Rasa.Structures.World.MissionObjectiveTransitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = objectiveId,
                TransitionId = transitionId,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Sequence = transitionId - 19,
                FromState = (byte)Rasa.Data.MissionObjectiveState.Incomplete,
                ToState = (byte)Rasa.Data.MissionObjectiveState.Completed,
                Comment = $"Progress transition {transitionId}"
            });
            fixture.Triggers.Add(new Rasa.Structures.World.MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = objectiveId,
                TransitionId = transitionId,
                TriggerId = triggerId,
                Requirement = Rasa.Structures.World.MissionContentRequirement.Required,
                Kind = Rasa.Structures.World.MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)Rasa.Data.MissionProgressEventKind.CreatureKilled,
                SubjectId = subjectId,
                Comment = $"Kill creature {subjectId}"
            });
        }

        private sealed class MissionContentLoadingFactory : IGameUnitOfWorkFactory
        {
            private readonly MissionTestContext _charFactory;
            private readonly IWorldUnitOfWork _worldUnit;

            internal MissionContentLoadingFactory(
                MissionTestContext charFactory,
                IWorldUnitOfWork worldUnit)
            {
                _charFactory = charFactory;
                _worldUnit = worldUnit;
            }

            public ICharUnitOfWork CreateChar() => _charFactory.CreateChar();
            public IWorldUnitOfWork CreateWorld() => _worldUnit;
        }
    }
}
