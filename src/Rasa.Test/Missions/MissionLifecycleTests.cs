using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Packets.Mission.Server;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class MissionLifecycleTests
    {
        [TestMethod]
        public void MissionRowsAreScopedByPersistentCharacterAndSupportMultipleMissions()
        {
            using var db = new MissionTestContext();
            db.SeedCharacter(10, 0, 100);
            db.SeedCharacter(10, 1, 101);
            db.SeedCharacter(20, 0, 200);
            db.SeedMission(100, 321, 0, false);
            db.SeedMission(100, 429, 0, true);
            db.SeedMission(101, 321, 4, false);
            db.SeedMission(200, 429, 0, false);

            using var unit = db.CreateChar();
            CollectionAssert.AreEquivalent(new uint[] { 321, 429 },
                unit.CharacterMissions.Get(100).Select(x => x.MissionId).ToArray());
            CollectionAssert.AreEquivalent(new uint[] { 321 },
                unit.CharacterMissions.Get(101).Select(x => x.MissionId).ToArray());
            CollectionAssert.AreEquivalent(new uint[] { 429 },
                unit.CharacterMissions.Get(200).Select(x => x.MissionId).ToArray());
        }

        [TestMethod]
        public void MissionStateMutationsPersistAndRemoveOnlyTheSelectedMission()
        {
            using var db = new MissionTestContext();
            db.SeedCharacter(10, 0, 100);

            using (var unit = db.CreateChar())
            {
                unit.CharacterMissions.Add(new CharacterMissionEntry(100, 321, 0));
                unit.CharacterMissions.Add(new CharacterMissionEntry(100, 429, 1));
                unit.CharacterMissions.SetState(100, 321, 4);
                unit.CharacterMissions.SetCompletable(100, 321, true);
            }

            using (var unit = db.CreateChar())
            {
                var mission = unit.CharacterMissions.Get(100, 321);
                Assert.AreEqual(4U, mission.MissionState);
                Assert.IsTrue(mission.Completeable);
                Assert.AreSame(mission, unit.CharacterMissions.Get(100, 321));
                unit.CharacterMissions.Remove(100, 321);
            }

            using var reopened = db.CreateChar();
            Assert.IsNull(reopened.CharacterMissions.Get(100, 321));
            CollectionAssert.AreEqual(new uint[] { 429 },
                reopened.CharacterMissions.Get(100).Select(x => x.MissionId).ToArray());
        }

        [TestMethod]
        public void InitialSnapshotMapsOnlySupportedDurableCharacterRows()
        {
            using var context = MissionTestContext.WithDefinitions(321, 429, 666, 777, 888);
            context.SeedMission(context.Client.Player.Id, 321, (uint)MissionState.Active, false);
            context.SeedMission(context.Client.Player.Id, 429, (uint)MissionState.Active, true);
            context.SeedMission(context.Client.Player.Id, 666, (uint)MissionState.Failded, true);
            context.SeedMission(context.Client.Player.Id, 777, (uint)MissionState.Completed, false);
            context.SeedMission(context.Client.Player.Id, 888, (uint)MissionState.Success, false);
            context.SeedMission(context.Client.Player.Id, 999, (uint)MissionState.Failded, false);
            context.ReloadPlayerMissions();

            context.Manager.PublishInitialState(context.Client);

            var packet = context.Drain().OfType<MissionStatusInfoPacket>().Single();
            CollectionAssert.AreEquivalent(new uint[] { 321, 429, 666, 777 },
                packet.MissionStatusDict.Keys.ToArray());
            Assert.AreEqual(MissionState.Active, packet.MissionStatusDict[321].MissionState);
            Assert.IsFalse(packet.MissionStatusDict[321].Completeable);
            Assert.AreEqual(MissionState.Active, packet.MissionStatusDict[429].MissionState);
            Assert.IsTrue(packet.MissionStatusDict[429].Completeable);
            Assert.AreEqual(MissionState.Failded, packet.MissionStatusDict[666].MissionState);
            Assert.IsFalse(packet.MissionStatusDict[666].Completeable);
            Assert.AreEqual(MissionState.Completed, packet.MissionStatusDict[777].MissionState);
            Assert.IsFalse(packet.MissionStatusDict[777].Completeable);
        }

        [TestMethod]
        public void AcceptingNpcMissionPersistsActiveStateBeforePublishingTheDelta()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);

            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));

            var runtime = context.Client.Player.Missions[321];
            Assert.AreEqual(MissionState.Active, runtime.State);
            Assert.IsFalse(runtime.Completeable);
            using (var unit = context.CreateChar())
            {
                var durable = unit.CharacterMissions.Get(context.Client.Player.Id, 321);
                Assert.IsNotNull(durable);
                Assert.AreEqual((uint)MissionState.Active, durable.MissionState);
                Assert.IsFalse(durable.Completeable);
            }
            var gained = context.Drain().OfType<MissionGainedPacket>().Single();
            Assert.AreEqual(MissionState.Active, gained.MissionInfo.MissionState);
            Assert.IsFalse(gained.MissionInfo.Completeable);
        }

        [TestMethod]
        public void AcceptanceRejectsWrongNpcAndNpcFromAnotherMapInstance()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var wrongNpc = context.AddNpc(66);
            var otherMap = new MapChannel
            {
                MapInfo = context.Map.MapInfo,
                ClientList = new List<Rasa.Game.Client>(),
                PlayerLimit = 128,
                InstanceId = context.Map.InstanceId + 1
            };
            var remoteNpc = context.AddNpc(77, otherMap);

            Assert.IsFalse(context.Manager.TryAcceptNpcMission(context.Client, wrongNpc.EntityId, 321));
            Assert.IsFalse(context.Manager.TryAcceptNpcMission(context.Client, remoteNpc.EntityId, 321));

            Assert.AreEqual(0, context.Client.Player.Missions.Count);
            Assert.AreEqual(0, context.Drain().OfType<MissionGainedPacket>().Count());
        }

        [TestMethod]
        public void DuplicateAcceptanceDoesNotPersistOrPublishTwice()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));
            context.Drain();

            Assert.IsFalse(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));

            using var unit = context.CreateChar();
            Assert.AreEqual(1, unit.CharacterMissions.Get(context.Client.Player.Id).Count);
            Assert.AreEqual(0, context.Drain().OfType<MissionGainedPacket>().Count());
        }

        [TestMethod]
        public void MissionLogAtExactlyThirtyRejectsAnotherMission()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);
            for (uint id = 1; id <= 30; id++)
                context.Client.Player.Missions.Add(id, new MissionLog(id, MissionState.Active, false));

            Assert.IsFalse(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));

            Assert.AreEqual(30, context.Client.Player.Missions.Count);
            Assert.AreEqual(0, context.Drain().OfType<MissionGainedPacket>().Count());
        }

        [TestMethod]
        public void AcceptancePersistenceFailureLeavesRuntimeAndPacketsUnchanged()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);
            context.BeforeSave = _ => throw new DbUpdateException("fixture failure");

            Assert.IsFalse(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));

            Assert.AreEqual(0, context.Client.Player.Missions.Count);
            Assert.AreEqual(0, context.Drain().OfType<MissionGainedPacket>().Count());
        }

        [TestMethod]
        public void AcceptanceDoesNotHideProgrammingErrors()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);
            var expected = new InvalidOperationException("fixture programming error");
            context.BeforeSave = _ => throw expected;

            Assert.AreSame(expected, Assert.ThrowsExactly<InvalidOperationException>(
                () => context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321)));

            Assert.AreEqual(0, context.Client.Player.Missions.Count);
            Assert.AreEqual(0, context.Drain().OfType<MissionGainedPacket>().Count());
        }

        [TestMethod]
        public void AbandoningActiveMissionRemovesDurableAndRuntimeStateBeforePublishing()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var npc = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));
            context.Drain();

            Assert.IsTrue(context.Manager.TryAbandon(context.Client, 321));

            Assert.IsFalse(context.Client.Player.Missions.ContainsKey(321));
            using (var unit = context.CreateChar())
                Assert.IsNull(unit.CharacterMissions.Get(context.Client.Player.Id, 321));
            Assert.AreEqual(321U, context.Drain().OfType<MissionDiscardedPacket>().Single().MissionId);

            context.ReloadPlayerMissions();
            Assert.IsFalse(context.Client.Player.Missions.ContainsKey(321));
        }

        [TestMethod]
        public void MissionDefinitionsAreImmutableAndNeverCarryCharacterState()
        {
            using var context = MissionTestContext.WithDefinitions(321);
            var definition = context.Manager.LoadedMissions[321];
            var npc = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, npc.EntityId, 321));
            var gained = context.Drain().OfType<MissionGainedPacket>().Single();

            gained.MissionInfo.MissionState = MissionState.Completed;

            Assert.IsFalse(typeof(MissionInfo).IsAssignableFrom(typeof(Mission)));
            Assert.IsFalse(typeof(Mission).GetProperty(nameof(Mission.MissionId))!.CanWrite);
            Assert.AreEqual(MissionState.Active,
                context.Manager.BuildStatusSnapshot(context.Client.Player)[321].MissionState);
            Assert.AreEqual(77U, definition.MissionGiver);
        }
    }
}
