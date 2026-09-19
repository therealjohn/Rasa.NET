using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Structures;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.UnitOfWork;

    [TestClass]
    [DoNotParallelize]
    public class BootcampInitiationTests
    {
        [TestMethod]
        public void InitiationRequiresOrderedAreasSurvivesReconnectsAndCannotBeAbandoned()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var mcAllister = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionInitiation));
            BootcampRuntimeTestHarness.AssertObjectiveStates(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation],
                (1U, MissionObjectiveState.Incomplete),
                (2U, MissionObjectiveState.Inactive));

            Assert.IsFalse(harness.Manager.TryAbandon(
                harness.Client,
                BootcampRuntimeTestHarness.MissionInitiation));
            using (var unit = harness.Context.CreateChar())
            {
                Assert.IsNotNull(unit.CharacterMissions.GetByCharacterAndMission(
                    harness.Client.Player.Id,
                    BootcampRuntimeTestHarness.MissionInitiation));
            }

            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, 431)));
            BootcampRuntimeTestHarness.AssertObjectiveStates(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation],
                (1U, MissionObjectiveState.Incomplete),
                (2U, MissionObjectiveState.Inactive));

            harness.Reconnect();
            BootcampRuntimeTestHarness.AssertObjectiveStates(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation],
                (1U, MissionObjectiveState.Incomplete),
                (2U, MissionObjectiveState.Inactive));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, 430)));
            BootcampRuntimeTestHarness.AssertObjectiveStates(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation],
                (1U, MissionObjectiveState.Completed),
                (2U, MissionObjectiveState.Incomplete));
            Assert.AreEqual(
                1,
                harness.Context.Drain().OfType<ObjectiveCompletedPacket>().Count(packet => packet.ObjectiveId == 1));

            Assert.IsFalse(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, 430)));

            harness.Reconnect();
            BootcampRuntimeTestHarness.AssertObjectiveStates(
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation],
                (1U, MissionObjectiveState.Completed),
                (2U, MissionObjectiveState.Incomplete));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(BootcampRuntimeTestHarness.MissionInitiation, 431)));
            Assert.IsTrue(harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation].Completeable);
            Assert.AreEqual(
                1,
                harness.Context.Drain().OfType<MissionCompleteablePacket>().Count(packet => packet.IsCompleteable));

            harness.Reconnect();
            Assert.IsTrue(harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation].Completeable);
            var currentMcAllister = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                currentMcAllister?.EntityId ?? mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionInitiation,
                selectionIndex: null,
                rating: null));
            Assert.AreEqual(
                MissionState.Success,
                harness.Client.Player.Missions[BootcampRuntimeTestHarness.MissionInitiation].State);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                currentMcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp));
        }
    }
}
