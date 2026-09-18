extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Mission.Server;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class MissionProgressTests
    {
        [TestMethod]
        public void ExactSubjectCompletesOnceAfterCommitAndReconnectsCompleted()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(context);
            context.BeforeSave = _ =>
            {
                Assert.AreEqual(MissionObjectiveState.Incomplete,
                    context.Client.Player.Missions[321].Objectives[1].State);
                Assert.AreEqual(0, context.Drain().Count);
            };

            Assert.IsFalse(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(81)));
            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(82)));
            Assert.IsFalse(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(82)));

            Assert.AreEqual(MissionObjectiveState.Completed,
                context.Client.Player.Missions[321].Objectives[1].State);
            CollectionAssert.AreEqual(
                new[] { typeof(ObjectiveCompletedPacket), typeof(MissionCompleteablePacket) },
                context.Drain().Select(packet => packet.GetType()).ToArray());
            context.ReloadPlayerMissions();
            Assert.AreEqual(MissionObjectiveState.Completed,
                context.Client.Player.Missions[321].Objectives[1].State);
            Assert.IsTrue(context.Client.Player.Missions[321].Completeable);
        }

        [TestMethod]
        public void DistinctSubjectsUsePersistedAcquisitionCollectionsWithoutCounters()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                    MissionProgressEventKind.WaypointAcquired,
                    new HashSet<uint> { 49, 50, 51 }));
            SeedActive(context);
            context.Client.Player.GainedWaypoints.AddRange(new[]
            {
                new CharacterTeleporterEntry(1, 49, (byte)WaypointType.Waypoint),
                new CharacterTeleporterEntry(1, 50, (byte)WaypointType.Waypoint)
            });
            using (var unit = context.CreateChar())
            {
                unit.CharacterTeleporters.Add(
                    new CharacterTeleporterEntry(1, 49, (byte)WaypointType.Waypoint));
                unit.CharacterTeleporters.Add(
                    new CharacterTeleporterEntry(1, 50, (byte)WaypointType.Waypoint));
            }

            Assert.IsFalse(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Waypoint(50)));
            var finalWaypoint = new CharacterTeleporterEntry(
                1, 51, (byte)WaypointType.Waypoint);
            using (var unit = context.CreateChar())
                unit.CharacterTeleporters.Add(finalWaypoint);
            context.Client.Player.GainedWaypoints.Add(finalWaypoint);
            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Waypoint(51)));

            Assert.AreEqual(0, context.Client.Player.Missions[321].Objectives[1].Counters.Count);
            Assert.AreEqual(0, context.Drain().OfType<UpdateObjectiveCounterPacket>().Count());
        }

        [TestMethod]
        public void ExactCounterPublishesCounterBeforeCompletionAndUsesCheckedMonotonicValues()
        {
            var counters = new Dictionary<uint, MissionObjectiveCounterDefinition>
            {
                [0] = new MissionObjectiveCounterDefinition(0, 2, 4)
            };
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 75, 0, 2, 4),
                counters: counters);
            SeedActive(context);

            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(75)));
            Assert.AreEqual(3U, context.Client.Player.Missions[321].Objectives[1].Counters[0]);
            CollectionAssert.AreEqual(
                new[] { typeof(UpdateObjectiveCounterPacket) },
                context.Drain().Select(packet => packet.GetType()).ToArray());

            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(75)));
            CollectionAssert.AreEqual(
                new[]
                {
                    typeof(UpdateObjectiveCounterPacket),
                    typeof(ObjectiveCompletedPacket),
                    typeof(MissionCompleteablePacket)
                },
                context.Drain().Select(packet => packet.GetType()).ToArray());
        }

        [TestMethod]
        public void StaleAndOverflowingDurableProgressAreNoOps()
        {
            var counters = new Dictionary<uint, MissionObjectiveCounterDefinition>
            {
                [0] = new MissionObjectiveCounterDefinition(0, uint.MaxValue, uint.MaxValue)
            };
            using var overflow = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(
                    MissionProgressEventKind.CreatureKilled,
                    75, 0, uint.MaxValue, uint.MaxValue),
                counters: counters);
            SeedActive(overflow);
            Assert.IsFalse(overflow.Manager.RecordProgress(
                overflow.Client, MissionProgressEvent.Creature(75)));
            Assert.AreEqual(0, overflow.Drain().Count);

            using var stale = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(stale);
            using (var unit = stale.CreateChar())
                unit.CharacterMissionProgress.SetObjectiveState(
                    1, 321, 1,
                    (byte)MissionObjectiveState.Incomplete,
                    (byte)MissionObjectiveState.Completed);
            Assert.IsFalse(stale.Manager.RecordProgress(
                stale.Client, MissionProgressEvent.Creature(82)));
            Assert.AreEqual(MissionObjectiveState.Incomplete,
                stale.Client.Player.Missions[321].Objectives[1].State);
            Assert.AreEqual(0, stale.Drain().Count);
        }

        [TestMethod]
        public void PersistenceFailuresRollbackProgressButProgrammingErrorsRetainIdentity()
        {
            using var expectedFailure = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(expectedFailure);
            expectedFailure.AfterSave = _ => throw new DbUpdateException("Injected progress failure.");
            Assert.IsFalse(expectedFailure.Manager.RecordProgress(
                expectedFailure.Client, MissionProgressEvent.Creature(82)));
            Assert.AreEqual((byte)MissionObjectiveState.Incomplete,
                expectedFailure.ReadProgress(321).Missions[321].Objectives[1].State);
            Assert.AreEqual(0, expectedFailure.Drain().Count);

            using var programming = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(programming);
            var expected = new InvalidOperationException("Injected programming failure.");
            programming.AfterSave = _ => ThrowAtPersistenceBoundary(expected);
            var actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
                programming.Manager.RecordProgress(
                    programming.Client, MissionProgressEvent.Creature(82)));
            Assert.AreSame(expected, actual);
            StringAssert.Contains(actual.StackTrace, nameof(ThrowAtPersistenceBoundary));
            Assert.AreEqual(0, programming.Drain().Count);
        }

        [TestMethod]
        public void CompetingClientsPersistAndPublishOneProgressDelta()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(context);
            var competitor = context.CreateCompetingClient();
            using var start = new ManualResetEventSlim();
            var results = Task.WhenAll(
                Task.Run(() =>
                {
                    start.Wait();
                    return context.Manager.RecordProgress(
                        context.Client, MissionProgressEvent.Creature(82));
                }),
                Task.Run(() =>
                {
                    start.Wait();
                    return context.Manager.RecordProgress(
                        competitor, MissionProgressEvent.Creature(82));
                }));
            start.Set();

            Assert.AreEqual(1, results.GetAwaiter().GetResult().Count(result => result));
            Assert.AreEqual(1, context.Drain()
                .Concat(MissionTestContext.Drain(competitor))
                .OfType<ObjectiveCompletedPacket>().Count());
        }

        [TestMethod]
        public void WaypointAndLogosAdaptersRunOnlyAfterNewDurableGrantPackets()
        {
            using var waypoint = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                    MissionProgressEventKind.WaypointAcquired,
                    new HashSet<uint> { 49 }));
            SeedActive(waypoint);
            var character = new CharacterManager(waypoint, waypoint.Manager);
            var objects = new DynamicObjectManager(
                waypoint,
                updateCharacter: character.UpdateCharacter,
                missionManager: waypoint.Manager);

            objects.CheckPlayerWaypoint(
                waypoint.Client, new WaypointInfo(49, false, WaypointType.Waypoint));
            objects.CheckPlayerWaypoint(
                waypoint.Client, new WaypointInfo(49, false, WaypointType.Waypoint));

            CollectionAssert.AreEqual(
                new[] { typeof(WaypointGainedPacket), typeof(ObjectiveCompletedPacket), typeof(MissionCompleteablePacket) },
                waypoint.Drain().Select(packet => packet.GetType()).ToArray());

            using var logos = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.LogosAcquired, 10));
            SeedActive(logos);
            var logosCharacter = new CharacterManager(logos, logos.Manager);
            Assert.IsTrue(logosCharacter.TryAddLogos(logos.Client, 10));
            Assert.IsFalse(logosCharacter.TryAddLogos(logos.Client, 10));
            CollectionAssert.AreEqual(
                new[] { typeof(LogosStoneAddedPacket), typeof(ObjectiveCompletedPacket), typeof(MissionCompleteablePacket) },
                logos.Drain().Select(packet => packet.GetType()).ToArray());
        }

        [TestMethod]
        public void ProgressFailurePreservesPersistedWaypointAndLogosGrants()
        {
            using var waypoint = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                    MissionProgressEventKind.WaypointAcquired,
                    new HashSet<uint> { 49 }));
            SeedActive(waypoint);
            waypoint.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<CharacterMissionObjectiveEntry>()
                    .Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected waypoint progress failure.");
            };
            var character = new CharacterManager(waypoint, waypoint.Manager);
            var objects = new DynamicObjectManager(
                waypoint,
                updateCharacter: character.UpdateCharacter,
                missionManager: waypoint.Manager);

            objects.CheckPlayerWaypoint(
                waypoint.Client, new WaypointInfo(49, false, WaypointType.Waypoint));

            using (var unit = waypoint.CreateChar())
                Assert.AreEqual(1, unit.CharacterTeleporters.Get(1).Count);
            Assert.AreEqual(1, waypoint.Client.Player.GainedWaypoints.Count);
            CollectionAssert.AreEqual(
                new[] { typeof(WaypointGainedPacket) },
                waypoint.Drain().Select(packet => packet.GetType()).ToArray());

            using var logos = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.LogosAcquired, 10));
            SeedActive(logos);
            logos.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<CharacterMissionObjectiveEntry>()
                    .Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected Logos progress failure.");
            };
            var logosCharacter = new CharacterManager(logos, logos.Manager);

            Assert.IsTrue(logosCharacter.TryAddLogos(logos.Client, 10));

            using (var unit = logos.CreateChar())
                CollectionAssert.AreEqual(
                    new uint[] { 10 },
                    unit.CharacterLogoses.GetLogos(1));
            CollectionAssert.AreEqual(new uint[] { 10 }, logos.Client.Player.Logos);
            CollectionAssert.AreEqual(
                new[] { typeof(LogosStoneAddedPacket) },
                logos.Drain().Select(packet => packet.GetType()).ToArray());
        }

        [TestMethod]
        public void CreatureAdapterRunsOnceAfterExperienceAndLootForAuthoritativeKiller()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(context);
            var creature = AddCreature(context, 82);
            var manager = new CreatureManager(
                context,
                new ManifestationManager(context),
                context.Manager);
            try
            {
                manager.HandleCreatureKill(context.Map, creature, context.Client.Player);
                manager.HandleCreatureKill(context.Map, creature, context.Client.Player);

                Assert.AreEqual(1, context.Map.LootDispensers.Count);
                var packets = context.Drain();
                Assert.AreEqual(1, packets.OfType<ExperienceChangedPacket>().Count());
                Assert.AreEqual(1, packets.OfType<ObjectiveCompletedPacket>().Count());
                Assert.IsTrue(
                    packets.FindIndex(packet => packet is ExperienceChangedPacket) <
                    packets.FindIndex(packet => packet is ObjectiveCompletedPacket));
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.Map, creature);
            }
        }

        [TestMethod]
        public void CreatureProgressFailurePreservesExperienceAndLoot()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(context);
            var before = context.ReadRewardTotals();
            context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<CharacterMissionObjectiveEntry>()
                    .Any(entry => entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected creature progress failure.");
            };
            var creature = AddCreature(context, 82);
            var manager = new CreatureManager(
                context,
                new ManifestationManager(context),
                context.Manager);
            try
            {
                manager.HandleCreatureKill(context.Map, creature, context.Client.Player);

                var after = context.ReadRewardTotals();
                Assert.IsTrue(after.Experience > before.Experience);
                Assert.AreEqual(1, context.Map.LootDispensers.Count);
                Assert.AreEqual(MissionObjectiveState.Incomplete,
                    context.Client.Player.Missions[321].Objectives[1].State);
                var packets = context.Drain();
                Assert.AreEqual(1, packets.OfType<ExperienceChangedPacket>().Count());
                Assert.AreEqual(0, packets.OfType<ObjectiveCompletedPacket>().Count());
            }
            finally
            {
                CellManager.Instance.RemoveCreatureFromWorld(context.Map, creature);
            }
        }

        [TestMethod]
        public void MissionCompletionAdapterRunsAfterRewardAndFailureCannotRollbackReward()
        {
            using var context = CreateCompletionAdapterContext();
            var receiver = context.AddNpc(88);
            var before = context.ReadRewardTotals();
            var baselineSaves = context.SaveAttempts;
            context.BeforeSave = database =>
            {
                if (database.ChangeTracker.Entries<CharacterMissionObjectiveEntry>()
                    .Any(entry => entry.Entity.MissionId == 430 &&
                        entry.State == EntityState.Modified))
                    throw new DbUpdateException("Injected post-reward progress failure.");
            };

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, receiver.EntityId, 429, null, null));

            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience + 100, after.Experience);
            Assert.AreEqual(MissionState.Completed, context.Client.Player.Missions[429].State);
            Assert.AreEqual(MissionObjectiveState.Incomplete,
                context.Client.Player.Missions[430].Objectives[1].State);
            Assert.IsTrue(context.SaveAttempts > baselineSaves);
            var packets = context.Drain();
            Assert.AreEqual(1, packets.OfType<MissionRewardedPacket>().Count());
            Assert.AreEqual(0, packets.OfType<ObjectiveCompletedPacket>().Count());
        }

        [TestMethod]
        public void MissionCompletionAdapterPublishesTargetAfterRewardWithoutSelfRecursion()
        {
            using var context = CreateCompletionAdapterContext();
            var receiver = context.AddNpc(88);

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, receiver.EntityId, 429, null, null));

            var packets = context.Drain();
            Assert.IsTrue(
                packets.FindIndex(packet => packet is MissionRewardedPacket) <
                packets.FindIndex(packet => packet is ObjectiveCompletedPacket));
            Assert.AreEqual(MissionState.Completed, context.Client.Player.Missions[429].State);
            Assert.AreEqual(MissionObjectiveState.Completed,
                context.Client.Player.Missions[430].Objectives[1].State);
            Assert.AreEqual(1, packets.OfType<ObjectiveCompletedPacket>().Count());
        }

        [TestMethod]
        public void RecoveredInactiveRulesNeverReadOrWriteCharacterProgress()
        {
            using var context = MissionTestContext.WithRecoveredDefinitions();
            context.ResetCharUnitCount();
            foreach (var definition in context.Manager.LoadedMissions.Values)
            foreach (var objective in definition.Objectives.Values)
            {
                var rule = objective.ProgressRule;
                var subjects = rule?.Subjects ?? new uint[] { objective.ObjectiveId };
                foreach (var subject in subjects)
                foreach (MissionProgressEventKind kind in Enum.GetValues(typeof(MissionProgressEventKind)))
                {
                    var progress = kind switch
                    {
                        MissionProgressEventKind.WaypointAcquired => MissionProgressEvent.Waypoint(subject),
                        MissionProgressEventKind.LogosAcquired => MissionProgressEvent.Logos(subject),
                        MissionProgressEventKind.CreatureKilled => MissionProgressEvent.Creature(subject),
                        MissionProgressEventKind.MissionCompleted => MissionProgressEvent.Mission(subject),
                        _ => throw new AssertFailedException()
                    };
                    Assert.IsFalse(context.Manager.RecordProgress(context.Client, progress));
                }
            }

            Assert.AreEqual(0, context.CharUnitsCreated);
            Assert.AreEqual(0, context.Drain().Count);
        }

        [TestMethod]
        public void InvalidClientAndDurableOwnerMismatchAreNoOps()
        {
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.CreatureKilled, 82));
            SeedActive(context);
            context.ResetCharUnitCount();
            context.Client.State = ClientState.Disconnected;
            Assert.IsFalse(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(82)));
            Assert.AreEqual(0, context.CharUnitsCreated);

            context.Client.State = ClientState.Ingame;
            context.Client.AccountEntry.Id = 2;
            Assert.IsFalse(context.Manager.RecordProgress(
                context.Client, MissionProgressEvent.Creature(82)));
            Assert.AreEqual(MissionObjectiveState.Incomplete,
                context.Client.Player.Missions[321].Objectives[1].State);
            Assert.AreEqual(0, context.Drain().Count);
        }

        private static void SeedActive(MissionTestContext context, uint missionId = 321)
        {
            context.SeedMission(1, missionId, (uint)MissionState.Active, false);
            context.ReloadPlayerMissions();
            context.Drain();
        }

        private static MissionTestContext CreateCompletionAdapterContext()
        {
            var source = CreateMission(429, null);
            var target = CreateMission(
                430,
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.MissionCompleted, 429));
            var reward = new MissionRewardDefinition(
                100,
                new Dictionary<CurencyType, int>(),
                Array.Empty<MissionRewardItem>(),
                Array.Empty<MissionRewardItem>());
            var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission> { [429] = source, [430] = target },
                new Dictionary<uint, MissionRewardDefinition> { [429] = reward });
            context.SeedMission(1, 429, (uint)MissionState.Active, true);
            context.SeedMission(1, 430, (uint)MissionState.Active, false);
            context.ReloadPlayerMissions();
            context.Drain();
            return context;
        }

        private static Mission CreateMission(uint missionId, MissionProgressRule rule)
        {
            var objective = new MissionObjectiveDefinition(
                1, 1001, 1002, new uint?[] { null, null, null }, 0,
                MissionObjectiveState.Incomplete, true,
                new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                new Dictionary<uint, MissionObjectiveItemCounterDefinition>(),
                Array.Empty<MissionObjectiveConversation>(),
                Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<MissionIndicator>(), rule);
            return new Mission(
                missionId, $"Mission {missionId}", missionId, 77, 88, 5, 1, 2,
                true, false, new[] { objective }, true);
        }

        private static Creature AddCreature(MissionTestContext context, uint dbId)
        {
            var creature = new Creature
            {
                DbId = dbId,
                EntityClass = EntityClasses.HumanBaseMale,
                MapContextId = context.Map.MapInfo.MapContextId,
                Level = 1,
                State = CharacterState.Idle,
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>(),
                Attributes = Enum.GetValues<Attributes>().ToDictionary(
                    id => id, id => new ActorAttributes(id, 100, 100, 0, 0, 0)),
                SpawnPool = new SpawnPool
                {
                    MapContextId = context.Map.MapInfo.MapContextId,
                    AliveCreatures = 1,
                    RespawnTime = 1000,
                    UpdateTimer = 1000
                }
            };
            CellManager.Instance.AddToWorld(context.Map, creature);
            return creature;
        }

        private static void ThrowAtPersistenceBoundary(Exception error) => throw error;
    }
}
