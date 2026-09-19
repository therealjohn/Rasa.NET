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
            Assert.AreEqual(3, transition.Actions.Count);
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
