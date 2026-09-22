using System;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Game.Server;
    using Rasa.Packets.LootDispenser.Server;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;

    [TestClass]
    [DoNotParallelize]
    public class BootcampMapSetupTests
    {
        private static readonly Vector3 CratePosition = new(398, 122, 173);
        private static readonly Vector3 AlisterDestination = new(400, 120, 150);

        [TestMethod]
        public void CrateIsVisibleButLockedBeforeAcceptingAnyMission()
        {
            using var harness = BootcampRuntimeTestHarness.CreateFromPendingSelection();
            Assert.IsFalse(harness.Client.Player.Missions.ContainsKey(1992));
            harness.Drain();
            harness.MovePlayerTo(CratePosition);
            CellManager.Instance.UpdateVisibility(harness.Client);

            var crate = harness.BootcampMap.DynamicObjects
                .SingleOrDefault(candidate => (uint)candidate.EntityClassId == 29877);
            Assert.IsNotNull(crate, "The equipment crate must already exist before mission acceptance.");
            Assert.AreEqual(CratePosition, crate.Position);
            Assert.IsFalse(crate.IsEnabled, "The pre-mission crate must not be usable.");
            Assert.IsTrue(harness.Drain().OfType<CreatePhysicalEntityPacket>()
                .Any(packet => packet.EntityId == crate.EntityId));

            harness.UseObjectAndRecover(crate, actionArgId: DynamicObjectManager.FootlockerUseArgId);

            Assert.IsFalse(harness.Drain().OfType<LootCorpsePacket>().Any());
            Assert.AreEqual(0UL, crate.LootDispenserEntityId);
        }

        [TestMethod]
        public void TheVisibleCrateStaysLockedUntilDelessioAndThenOpensWithoutBeingReplaced()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SpawnWorldNpcs();
            var crate = harness.BootcampMap.DynamicObjects.Single(candidate => (uint)candidate.EntityClassId == 29877);
            harness.MovePlayerTo(crate);
            CellManager.Instance.UpdateVisibility(harness.Client);
            Assert.IsFalse(crate.IsEnabled);
            Assert.AreEqual(0UL, crate.LootDispenserEntityId);
            var alister = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
            harness.MovePlayerTo(alister);
            CellManager.Instance.UpdateVisibility(harness.Client);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));

            Assert.IsFalse(crate.IsEnabled, "Acceptance must not skip Delessio's gear briefing.");
            harness.MovePlayerTo(crate);
            CellManager.Instance.UpdateVisibility(harness.Client);
            var delessio = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap, BootcampRuntimeTestHarness.CaptainDelessioPackageId);
            harness.Drain();

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client, delessio.EntityId, 1992, 4, 1));

            Assert.AreSame(crate, harness.BootcampMap.DynamicObjects
                .Single(candidate => (uint)candidate.EntityClassId == 29877));
            Assert.IsTrue(crate.IsEnabled);
            Assert.AreNotEqual(0UL, crate.LootDispenserEntityId);
            Assert.IsFalse(harness.Drain().OfType<CreatePhysicalEntityPacket>()
                .Any(packet => packet.EntityId == crate.EntityId), "Enabling the crate must not respawn its prop.");
            harness.UseObjectAndRecover(crate, actionArgId: DynamicObjectManager.FootlockerUseArgId);
            Assert.AreEqual(1, harness.Drain().OfType<LootCorpsePacket>().Count());
            Assert.AreEqual(6, harness.BootcampMap.LootDispensers[crate.LootDispenserEntityId].Remaining().Count);
        }

        [TestMethod]
        public void TheUnacceptedCrateRemainsVisibleAndLockedAfterFreshReconnect()
        {
            using var harness = BootcampRuntimeTestHarness.CreateFromPendingSelection();

            harness.ReconnectFresh();
            harness.MovePlayerTo(CratePosition);
            CellManager.Instance.UpdateVisibility(harness.Client);

            var crate = harness.BootcampMap.DynamicObjects.Single(candidate => (uint)candidate.EntityClassId == 29877);
            Assert.IsFalse(crate.IsEnabled);
            Assert.AreEqual(0UL, crate.LootDispenserEntityId);
            Assert.IsTrue(harness.Drain().OfType<CreatePhysicalEntityPacket>()
                .Any(packet => packet.EntityId == crate.EntityId));
        }

        [TestMethod]
        public void DeSimoneActuallySpawnsOnWalkableGroundAndIsIntroducedToTheClient()
        {
            using var harness = BootcampRuntimeTestHarness.CreateFromPendingSelection();
            harness.SpawnWorldNpcs();
            var deSimone = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap, BootcampRuntimeTestHarness.CorporalDeSimonePackageId);
            Assert.IsNotNull(deSimone, "DeSimone must be an actual world NPC, not only a mission definition.");
            Assert.AreSame(deSimone, EntityManager.Instance.GetCreature(deSimone.EntityId));
            var ground = harness.BootcampMap.NavMesh.Nearest(deSimone.Position);
            Assert.IsTrue(ground.HasValue, $"No walkable surface near DeSimone at {deSimone.Position}.");
            Assert.IsTrue(Math.Abs(ground.Value.Y - deSimone.Position.Y) < 0.5f,
                $"DeSimone is at {deSimone.Position}, but the walkable surface is {ground.Value}.");
            var hartmann = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.CorporalHartmannCreatureId);
            Assert.IsNotNull(hartmann);
            var route = harness.BootcampMap.NavMesh.FindPath(
                hartmann.Position, deSimone.Position, out var complete);
            Assert.IsNotNull(route);
            Assert.IsTrue(complete, "DeSimone must be reachable from Hartmann's handoff.");
            harness.Drain();
            harness.MovePlayerTo(ground.Value);

            CellManager.Instance.UpdateVisibility(harness.Client);

            Assert.IsTrue(harness.Drain().OfType<CreatePhysicalEntityPacket>()
                .Any(packet => packet.EntityId == deSimone.EntityId),
                "The player must receive DeSimone's physical entity at his actual spawn.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TheSpawnedDeSimoneTurnsInGearingUpAndOffersCaptureTheFlag(bool reconnect)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SeedMission(harness.Client.Player.Id, 1992, (uint)MissionState.Active, true);
            if (reconnect)
                harness.ReconnectFresh();
            harness.SpawnWorldNpcs();
            var deSimone = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap, BootcampRuntimeTestHarness.CorporalDeSimonePackageId);
            Assert.IsNotNull(deSimone);
            harness.MovePlayerTo(deSimone);
            CellManager.Instance.UpdateVisibility(harness.Client);
            Assert.IsTrue(harness.Manager.ClassifyNpcConversation(harness.Client.Player, deSimone)
                .TryGetStatus(out var status, out _));
            Assert.AreEqual(ConversationStatus.MissionComplete, status);

            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client, deSimone.EntityId, 1992, null, null));
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, deSimone.EntityId, 1994));

            Assert.AreEqual(MissionState.Completed, harness.Client.Player.Missions[1992].State);
            Assert.AreEqual(MissionState.Active, harness.Client.Player.Missions[1994].State);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1994].Objectives[4].State);
        }

        [TestMethod]
        public void AcceptingGearingUpRunsAlisterToHisFinalPositionAndLeavesHimThere()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SpawnWorldNpcs();
            var alister = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsNotNull(alister);
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
            harness.MovePlayerTo(alister);
            CellManager.Instance.UpdateVisibility(harness.Client);
            var start = alister.Position;

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));
            Assert.IsTrue(harness.Drain().OfType<IsRunningPacket>().Any(packet => packet.IsRunning));
            for (var tick = 0; tick < 4; tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);

            Assert.IsTrue(Vector3.Distance(start, alister.Position) > 0.1f,
                "Alister must start moving after Gearing Up is accepted.");
            Assert.IsTrue(Vector3.Distance(alister.Position, AlisterDestination) > 1,
                "Alister must run to the destination rather than teleport there.");
            for (var tick = 0; tick < 600; tick++)
            {
                var previous = alister.Position;
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
                if (previous.X != alister.Position.X || previous.Z != alister.Position.Z)
                {
                    // Detour can choose either adjoining polygon at an exact path vertex.
                    var insideSegment = Vector3.Lerp(previous, alister.Position, 0.001f);
                    var beforeEnd = Vector3.Lerp(previous, alister.Position, 0.999f);
                    Assert.IsTrue(harness.BootcampMap.NavMesh.IsWalkClear(insideSegment, beforeEnd),
                        $"Alister cut across blocked ground between {previous} and {alister.Position}.");
                }
            }

            Assert.IsTrue(Vector3.Distance(AlisterDestination, alister.Position) < 0.01f,
                $"Alister stopped at {alister.Position} instead of {AlisterDestination}.");
            Assert.AreEqual(2.175, alister.Rotation, 0.001);
            Assert.IsFalse(alister.IsRunning);
            var neighbor = harness.AddNpc(7777, position: AlisterDestination + new Vector3(0.3f, 0, 0));
            neighbor.Faction = Factions.AFS;
            for (var tick = 0; tick < 240; tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
            Assert.IsTrue(Vector3.Distance(AlisterDestination, alister.Position) < 0.01f);
            Assert.AreEqual(2.175, alister.Rotation, 0.001);
        }

        [TestMethod]
        public void AlisterWaitsForSuccessfulAcceptanceAndARejectedSaveDoesNotMoveHim()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SpawnWorldNpcs();
            var alister = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            var start = alister.Position;
            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));
            harness.Manager.ScenarioService.Tick(harness.Client);
            for (var tick = 0; tick < 160; tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
            Assert.AreEqual(start, alister.Position);
            Assert.IsFalse(alister.IsRunning);
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
            harness.MovePlayerTo(alister);
            CellManager.Instance.UpdateVisibility(harness.Client);
            harness.Context.BeforeSave = _ => throw new DbUpdateException("Injected acceptance failure.");
            harness.Drain();

            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));

            harness.Context.BeforeSave = null;
            harness.Manager.ScenarioService.Tick(harness.Client);
            for (var tick = 0; tick < 20; tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
            Assert.AreEqual(start, alister.Position);
            Assert.IsFalse(harness.Drain().OfType<IsRunningPacket>().Any(packet => packet.IsRunning));
            Assert.IsFalse(harness.Client.Player.Missions.ContainsKey(1992));
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));
            BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
            Assert.AreNotEqual(start, alister.Position);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void AlistersFinalPositionSurvivesReconnectDuringOrAfterHisRun(bool finishRun)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SpawnWorldNpcs();
            var alister = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
            harness.MovePlayerTo(alister);
            CellManager.Instance.UpdateVisibility(harness.Client);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));
            for (var tick = 0; tick < (finishRun ? 600 : 4); tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);

            harness.ReconnectFresh();
            harness.SpawnWorldNpcs();
            harness.Manager.ScenarioService.Tick(harness.Client);
            BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);

            var restored = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            Assert.IsNotNull(restored);
            Assert.AreNotSame(alister, restored);
            Assert.AreEqual(AlisterDestination, restored.Position);
            Assert.AreEqual(2.175, restored.Rotation, 0.001);
            Assert.IsFalse(restored.IsRunning);
            Assert.IsFalse(harness.Manager.TryAcceptNpcMission(harness.Client, restored.EntityId, 1992));
            harness.Manager.RebuildScenarioRuntime(harness.Client.Player.Id, harness.BootcampMap);
            for (var tick = 0; tick < 160; tick++)
                BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
            Assert.AreEqual(AlisterDestination, restored.Position);
            Assert.AreEqual(2.175, restored.Rotation, 0.001);
        }

        [TestMethod]
        public void AlistersDepartureAndCrateUnlockDoNotAffectAnotherPlayersInstance()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SpawnWorldNpcs();
            var otherMap = harness.Maps.GetOrCreatePrivateInstance(1985, 999);
            try
            {
                otherMap.SpawnPools.RemoveAll(pool =>
                    pool.DbId < BootcampRuntimeTestHarness.MajorMcAllisterCreatureId ||
                    pool.DbId > BootcampRuntimeTestHarness.CorporalDeSimoneCreatureId);
                SpawnPoolManager.Instance.SpawnPoolWorker(otherMap, 0);
                var otherAlister = BootcampRuntimeTestHarness.FindCreature(
                    otherMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
                Assert.IsNotNull(otherAlister);
                var otherStart = otherAlister.Position;
                var otherCrate = otherMap.DynamicObjects.Single(candidate => (uint)candidate.EntityClassId == 29877);
                var alister = BootcampRuntimeTestHarness.FindCreature(
                    harness.BootcampMap, BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
                harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Completed, true);
                harness.MovePlayerTo(alister);
                CellManager.Instance.UpdateVisibility(harness.Client);

                Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, alister.EntityId, 1992));
                var delessio = BootcampRuntimeTestHarness.FindNpcByPackage(
                    harness.BootcampMap, BootcampRuntimeTestHarness.CaptainDelessioPackageId);
                harness.MovePlayerTo(delessio);
                CellManager.Instance.UpdateVisibility(harness.Client);
                Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                    harness.Client, delessio.EntityId, 1992, 4, 1));
                for (var tick = 0; tick < 160; tick++)
                {
                    BehaviorManager.Instance.MapChannelThink(harness.BootcampMap, 250);
                    BehaviorManager.Instance.MapChannelThink(otherMap, 250);
                }

                Assert.AreEqual(AlisterDestination, alister.Position);
                Assert.AreEqual(otherStart, otherAlister.Position);
                Assert.IsFalse(otherAlister.IsRunning);
                Assert.IsFalse(otherCrate.IsEnabled);
                Assert.AreEqual(0UL, otherCrate.LootDispenserEntityId);
                var crate = harness.BootcampMap.DynamicObjects.Single(candidate => (uint)candidate.EntityClassId == 29877);
                Assert.IsTrue(crate.IsEnabled);
                Assert.AreNotEqual(crate.EntityId, otherCrate.EntityId);
            }
            finally
            {
                harness.Maps.ReleaseOwnedPrivateInstances(999);
            }
        }
    }
}
