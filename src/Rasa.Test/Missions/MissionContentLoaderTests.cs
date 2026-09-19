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

            var transition = content.Transitions.Values.Single();
            Assert.AreEqual(1, transition.Triggers.Count);
            Assert.AreEqual(4, transition.Actions.Count);
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
