using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Structures;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionScenarioProgressTests
    {
        [TestMethod]
        public void DurableScenarioStepCommitsBeforeScenarioProgressAndDoesNotReplay()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateScenarioFixture();
            var manager = LoadManager(context, fixture);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(
                context.Client,
                giver.EntityId,
                321));

            Assert.IsFalse(manager.TryRecordScenarioEvent(
                context.Client,
                321,
                60,
                999));
            Assert.IsTrue(manager.TryRecordScenarioEvent(
                context.Client,
                321,
                60,
                1));
            Assert.IsFalse(manager.TryRecordScenarioEvent(
                context.Client,
                321,
                60,
                1));

            Assert.AreEqual(
                MissionObjectiveState.Completed,
                context.Client.Player.Missions[321].Objectives[10].State);
            Assert.AreEqual(
                1,
                context.Drain().OfType<ObjectiveCompletedPacket>().Count());
            using var unit = context.CreateChar();
            CollectionAssert.AreEqual(
                new[] { "scenario:60:step:1" },
                unit.CharacterMissionScenario.Get(1, 321)
                    .Select(entry => entry.StepKey)
                    .ToArray());
        }

        [TestMethod]
        public void ConcurrentScenarioEventsCommitOneDurableStep()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateScenarioFixture();
            var manager = LoadManager(context, fixture);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(
                context.Client,
                giver.EntityId,
                321));
            var competitor = context.CreateCompetingClient(manager);
            context.Drain();

            using var start = new ManualResetEventSlim();
            var results = Task.WhenAll(
                Task.Run(() =>
                {
                    start.Wait();
                    return manager.TryRecordScenarioEvent(
                        context.Client,
                        321,
                        60,
                        1);
                }),
                Task.Run(() =>
                {
                    start.Wait();
                    return manager.TryRecordScenarioEvent(
                        competitor,
                        321,
                        60,
                        1);
                }));
            start.Set();

            Assert.AreEqual(1, results.GetAwaiter().GetResult().Count(result => result));
            Assert.AreEqual(
                1,
                context.Drain()
                    .Concat(MissionTestContext.Drain(competitor))
                    .OfType<ObjectiveCompletedPacket>()
                    .Count());
        }

        private static MissionContentFixture CreateScenarioFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Triggers.Clear();
            fixture.Actions.Clear();
            fixture.Transitions.Clear();
            fixture.Rewards.Clear();
            fixture.RewardItems.Clear();
            fixture.Indicators.Clear();
            fixture.Objectives.RemoveAll(objective => objective.ObjectiveId == 11);
            fixture.Transitions.Add(new MissionObjectiveTransitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                Requirement = MissionContentRequirement.Required,
                Sequence = 1,
                FromState = (byte)MissionObjectiveState.Incomplete,
                ToState = (byte)MissionObjectiveState.Completed,
                Comment = "Scenario step"
            });
            fixture.Triggers.Add(new MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 10,
                TransitionId = 20,
                TriggerId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionTriggerKind.ProgressEvent,
                Sequence = 1,
                EventKind = (byte)MissionProgressEventKind.ScenarioEvent,
                SubjectId = 1,
                CounterId = 60,
                Comment = "Scenario 60 step 1"
            });
            return fixture;
        }

        private static MissionManager LoadManager(
            MissionTestContext context,
            MissionContentFixture fixture)
        {
            var manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new Dictionary<uint, Mission>());
            var report = manager.LoadMissions();
            Assert.IsFalse(
                report.BlocksReadiness,
                string.Join(" | ", report.Diagnostics.Select(diagnostic => diagnostic.Code)));
            return manager;
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
