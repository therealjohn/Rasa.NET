extern alias RasaGame;

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ClientState = RasaGame::Rasa.Data.ClientState;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Memory;
    using Rasa.Models;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.Char.CharacterMissionScenario;
    using Rasa.Services.Preloader;
    using Rasa.Structures;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class BootcampCallingForReinforcementsTests
    {
        private const uint MissingScoutAreaId = 435;
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ArrivalDelay = TimeSpan.FromSeconds(2);

        [TestMethod]
        public void AuthoredFinalePositionsKeepTheConfirmedCrashSiteAndGroundedHandoff()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            StartCrashSiteScene(harness);
            var survivor = harness.WorldContext.MissionSpawnEntries.Single(row =>
                row.MissionId == 1995 && row.SpawnGroupId == 1 && row.SpawnId == 1);
            Assert.AreEqual(-101.2, survivor.PosX, 0.0001);
            Assert.AreEqual(86.40231, survivor.PosY, 0.0001);
            Assert.AreEqual(70.8, survivor.PosZ, 0.0001);
            var conrad = FindScenarioObject(harness, "bootcamp-conrad-corpse");
            Assert.IsLessThan(0.001f, Vector3.Distance(
                new Vector3(-102.4f, 86.20677f, 66.8f), conrad.Position));
            var wreck = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.IsLessThan(0.001f, Vector3.Distance(
                new Vector3(-225, 101.12099f, -71), wreck.Position));
            foreach (var missionId in new[] { 1995U, 2005U })
            {
                foreach (var (creatureId, position) in new[]
                         {
                             (39U, new Vector3(-218, 101.08475f, -78)),
                             (50U, new Vector3(-221, 101.2538f, -74)),
                             (510209U, new Vector3(-223, 101.269646f, -76))
                         })
                {
                    var spawn = harness.WorldContext.MissionSpawnEntries.Single(row =>
                        row.MissionId == missionId && row.CreatureId == creatureId);
                    Assert.AreEqual(position.X, spawn.PosX, 0.0001);
                    Assert.AreEqual(position.Y, spawn.PosY, 0.0001);
                    Assert.AreEqual(position.Z, spawn.PosZ, 0.0001);
                }
                var marker = harness.WorldContext.MissionIndicatorEntries.Single(row =>
                    row.MissionId == missionId && row.ObjectiveId == 1 && row.IndicatorId == 432);
                Assert.AreEqual(-225.0, marker.PosX, 0.0001);
                Assert.AreEqual(101.12099, marker.PosY, 0.0001);
                Assert.AreEqual(-71.0, marker.PosZ, 0.0001);
                var exit = harness.WorldContext.MissionAreaEntries.Single(row =>
                    row.MissionId == missionId && row.AreaId == 438);
                Assert.AreEqual(101.1059, exit.PosY, 0.0001);
            }
            var search = harness.WorldContext.MissionAreaEntries.Single(row =>
                row.MissionId == 1995 && row.AreaId == 435);
            Assert.AreEqual(12.0, search.Radius);
        }

        [TestMethod]
        [DataRow(false, true)]
        [DataRow(true, true)]
        [DataRow(false, false)]
        [DataRow(true, false)]
        public void RebuildingLegacyCrateUsesTheWreckWithoutResettingTheBombAttempt(bool planted, bool legacyClass)
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            var legacy = FindScenarioObject(harness, "bootcamp-dropship-debris");
            if (planted)
                harness.UseObjectAndRecover(legacy);
            var deadline = ReadDeadline(harness, 1995);
            var receipts = ReadScenarioKeys(harness, 1995);
            if (legacyClass)
                legacy.EntityClassId = (EntityClasses)24911;
            else
                legacy.Position -= new Vector3(0, 2, 0);
            legacy.StateId = UseObjectState.IdStateActive;
            FindScenarioObject(harness, "bootcamp-conrad-corpse").StateId = UseObjectState.IdStateActive;

            harness.Manager.RebuildScenarioRuntime(harness.Client.Player.Id, harness.BootcampMap);

            var rebuilt = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.AreEqual(24586U, (uint)rebuilt.EntityClassId);
            Assert.IsLessThan(0.001f, Vector3.Distance(
                new Vector3(-225, 101.12099f, -71), rebuilt.Position));
            Assert.AreEqual(UseObjectState.DoorStateClosed, rebuilt.StateId);
            Assert.AreEqual(UseObjectState.TdStateClosed, FindScenarioObject(harness, "bootcamp-conrad-corpse").StateId);
            Assert.AreNotEqual(legacy.EntityId, rebuilt.EntityId);
            Assert.AreEqual(!planted, rebuilt.IsEnabled);
            Assert.AreEqual(deadline.DueAtUtc, ReadDeadline(harness, 1995).DueAtUtc);
            Assert.AreEqual(deadline.State, ReadDeadline(harness, 1995).State);
            CollectionAssert.AreEquivalent(receipts, ReadScenarioKeys(harness, 1995));
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        [DataRow(true, true)]
        public void CompletedLegacyDialoguePreservesDeadlineAndScenarioReceipts(bool planted, bool migrateDatabase)
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            if (planted)
                harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-dropship-debris"));
            var deadline = ReadDeadline(harness, 1995);
            var receipts = ReadScenarioKeys(harness, 1995);
            SeedLegacySurvivorObjective(harness, MissionObjectiveState.Completed);
            if (migrateDatabase)
                ApplyCharacterCompatibilityMigration(harness);

            harness.ReconnectFresh();

            AssertSupportedObjectives(harness);
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1995].Objectives[3].State);
            Assert.AreEqual(deadline.DueAtUtc, ReadDeadline(harness, 1995).DueAtUtc);
            Assert.AreEqual(deadline.State, ReadDeadline(harness, 1995).State);
            CollectionAssert.AreEquivalent(receipts, ReadScenarioKeys(harness, 1995));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.AreEqual(!planted, FindScenarioObject(harness, "bootcamp-dropship-debris").IsEnabled);

            harness.UtcNow = deadline.DueAtUtc + TimeSpan.FromSeconds(1);
            if (planted)
            {
                Assert.IsFalse(harness.Manager.EvaluateDeadlines(harness.Client));
                Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
                harness.UtcNow += ArrivalDelay;
                Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
                Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));
            }
            else
            {
                Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));
                Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[1995].State);
                var youngblood = BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2561);
                Assert.IsNotNull(youngblood);
                Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, youngblood.EntityId, 2005));
                Assert.AreEqual(harness.UtcNow + BombDeadline, ReadDeadline(harness, 2005).DueAtUtc);
            }
        }

        [TestMethod]
        [DataRow(MissionState.Completed)]
        [DataRow(MissionState.Failed)]
        public void LegacyTerminalMissionsRetainTheirStateAndDoNotReplayScenes(MissionState state)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SeedMission(harness.Client.Player.Id, 1995, (uint)state, state == MissionState.Completed);
            SeedLegacySurvivorObjective(harness,
                state == MissionState.Completed ? MissionObjectiveState.Completed : MissionObjectiveState.Incomplete);
            ApplyCharacterCompatibilityMigration(harness);
            harness.ReconnectFresh();
            Assert.AreEqual(state, harness.Client.Player.Missions[1995].State);
            AssertSupportedObjectives(harness);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.AreEqual(0, ReadScenarioKeys(harness, 1995).Length);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void InvalidLegacyProgressFailsExplicitlyWithoutDeletingTheSavedMission(bool inconsistentCompletion)
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            harness.SeedMission(harness.Client.Player.Id, 1990, (uint)MissionState.Active, false);
            harness.SeedMission(harness.Client.Player.Id, 1995, (uint)MissionState.Active, false);
            var youngblood = harness.AddNpc(BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId, 2561);
            SeedLegacySurvivorObjective(harness, MissionObjectiveState.Incomplete);
            using (var unit = harness.Context.CreateChar())
                unit.ExecuteTransaction(() =>
                {
                    if (inconsistentCompletion)
                        unit.CharacterMissions.GetByCharacterAndMission(harness.Client.Player.Id, 1995).Completeable = true;
                    else
                        unit.CharacterMissionProgress.AddObjectives(new[]
                        {
                            new CharacterMissionObjectiveEntry(harness.Client.Player.Id, 1995, 99, (byte)MissionObjectiveState.Incomplete)
                        });
                });
            ApplyCharacterCompatibilityMigration(harness);
            Logger.UpdateConfig(new Logger.LoggerConfig { IsDebugMode = true, LogToFile = false });
            var originalOutput = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                Assert.IsFalse(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Interaction(24990)),
                    "Invalid finale progress must be refused without unwinding the map tick.");
                Assert.IsFalse(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Creature(510210)));
                Assert.IsTrue(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Area(1990, 430)),
                    "An invalid finale must not block unrelated mission progress.");
                Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1990].Objectives[1].State);
                Assert.IsNotNull(harness.Manager.ClassifyNpcConversation(harness.Client.Player, youngblood));
                Assert.IsFalse(harness.Manager.TryCompleteNpcObjective(harness.Client, youngblood.EntityId, 1995, 2, 1));
                harness.Drain();
                harness.Manager.PublishMissionStatus(harness.Client, 1990, "unrelated valid mission");
                Assert.IsTrue(harness.Drain().OfType<MissionStatusInfoPacket>().Single().MissionStatusDict.ContainsKey(1990));
                harness.Manager.PublishMissionStatus(harness.Client, 1995, "invalid save rejection");
                harness.Manager.PublishInitialState(harness.Client);
                Assert.AreEqual(0, harness.Drain().OfType<MissionStatusInfoPacket>().Count(),
                    "Reject invalid snapshots; do not publish an empty or success-shaped fallback.");
                Assert.ThrowsExactly<GameplayRejectionException>(() => harness.Manager.BuildStatusSnapshot(harness.Client.Player));
            }
            finally
            {
                Console.SetOut(originalOutput);
            }
            StringAssert.Contains(output.ToString(), "Cannot migrate legacy mission 1995");
            StringAssert.Contains(output.ToString(), "saved data was preserved");
            Assert.IsTrue(harness.Client.Player.Missions.ContainsKey(1995));
            using var verify = harness.Context.CreateChar();
            Assert.IsNotNull(verify.CharacterMissions.GetByCharacterAndMission(harness.Client.Player.Id, 1995));
            Assert.IsNotNull(verify.CharacterMissionProgress.Get(harness.Client.Player.Id, 1995, 10));
            if (!inconsistentCompletion)
                Assert.IsNotNull(verify.CharacterMissionProgress.Get(harness.Client.Player.Id, 1995, 99));
        }

        private static void ApplyCharacterCompatibilityMigration(BootcampRuntimeTestHarness.Harness harness)
        {
            var migration = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
            BootcampReinforcementsContent.UpCharacters(migration);
            using var context = harness.Context.Open();
            using var transaction = context.Database.BeginTransaction();
            foreach (var operation in migration.Operations.Cast<SqlOperation>())
                context.Database.ExecuteSqlRaw(operation.Sql);
            transaction.Commit();
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(false, true)]
        public void LegacySurvivorSaveRecoversDurablyBeforeReconnectOrLiveSnapshot(bool liveReload, bool migrateDatabase)
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            var youngblood = harness.AddNpc(BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId, 2561);
            harness.SeedMission(harness.Client.Player.Id, 1994, (uint)MissionState.Completed, true);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, youngblood.EntityId, 1995));
            Assert.IsTrue(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Area(1995, MissingScoutAreaId)));
            SeedLegacySurvivorObjective(harness, MissionObjectiveState.Incomplete);
            var before = ReadScenarioKeys(harness, 1995);
            if (migrateDatabase)
                ApplyCharacterCompatibilityMigration(harness);

            if (liveReload)
            {
                harness.Manager.LoadMissions();
                harness.Manager.PublishInitialState(harness.Client);
            }
            else
                harness.ReconnectFresh();

            AssertSupportedObjectives(harness);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(MissionObjectiveState.Inactive, harness.Client.Player.Missions[1995].Objectives[3].State);
            CollectionAssert.AreEquivalent(before, ReadScenarioKeys(harness, 1995));
            using (var unit = harness.Context.CreateChar())
            {
                Assert.IsNull(unit.CharacterMissionProgress.Get(harness.Client.Player.Id, 1995, 10));
                Assert.AreEqual((byte)MissionObjectiveState.Incomplete,
                    unit.CharacterMissionProgress.Get(harness.Client.Player.Id, 1995, 2).ObjectiveState);
            }
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584);
            Assert.IsNotNull(survivor);
            Assert.AreEqual((EntityClasses)3846, survivor.EntityClass,
                "Reconnect must load the authored survivor model, not fixture class 4001.");
            CompleteNativeObjective(harness, survivor, 2);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[3].State);
            harness.ReconnectFresh();
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-dropship-debris"));
            AssertSupportedObjectives(harness);
        }

        private static void SeedLegacySurvivorObjective(
            BootcampRuntimeTestHarness.Harness harness, MissionObjectiveState survivorState)
        {
            using var unit = harness.Context.CreateChar();
            unit.ExecuteTransaction(() =>
            {
                var search = unit.CharacterMissionProgress.Get(harness.Client.Player.Id, 1995, 2);
                search.ObjectiveState = (byte)MissionObjectiveState.Completed;
                unit.CharacterMissionProgress.AddObjectives(new[]
                {
                    new CharacterMissionObjectiveEntry(harness.Client.Player.Id, 1995, 10, (byte)survivorState)
                });
            });
            var mission = harness.Client.Player.Missions[1995];
            var objectives = mission.Objectives.ToDictionary(pair => pair.Key, pair => pair.Value);
            objectives[2].State = MissionObjectiveState.Completed;
            objectives[10] = new MissionObjectiveLog(10, survivorState, null, null);
            harness.Client.Player.Missions[1995] = new MissionLog(1995, mission.State, mission.Completeable, objectives);
        }

        [TestMethod]
        public void WorldMovementAndNativeSurvivorDialogueUseOnlyClientObjectivesThenReachExtraction()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            var youngblood = harness.AddNpc(BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId, 2561);
            harness.SeedMission(harness.Client.Player.Id, 1994, (uint)MissionState.Completed, true);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(harness.Client, youngblood.EntityId, 1995));
            AssertSupportedObjectives(harness);

            var area = harness.WorldContext.MissionAreaEntries.Single(row => row.MissionId == 1995 && row.AreaId == 435);
            var center = new Vector3((float)area.PosX, (float)area.PosY, (float)area.PosZ);
            harness.MovePlayerTo(center + new Vector3((float)area.Radius.Value + 0.25f, 0, 0));
            harness.Client.Movement = new Movement(harness.Client.Player.Position, 1, 0, Vector2.Zero);
            harness.Client.Player.MoveBudget = 10;
            harness.Client.MissionAreaService = new MissionAreaService(() => harness.Manager);
            Assert.IsTrue(harness.Client.HandleMovement(new Movement(
                center + new Vector3((float)area.Radius.Value - 0.25f, 0, 0), 1, 0, Vector2.Zero)));

            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(MissionObjectiveState.Inactive, harness.Client.Player.Missions[1995].Objectives[3].State);
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584);
            Assert.IsNotNull(survivor);
            Assert.AreEqual((EntityClasses)3846, survivor.EntityClass,
                "The real-content regression must load the authored survivor model.");
            CompleteNativeObjective(harness, survivor, 2);
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[3].State);
            AssertSupportedObjectives(harness);

            var conrad = FindScenarioObject(harness, "bootcamp-conrad-corpse");
            Assert.AreEqual(UseObjectState.TdStateClosed, conrad.StateId);
            Assert.IsTrue(Vector3.Distance(harness.Client.Player.Position,
                conrad.Position + new Vector3(0.16736676f, 0.033820882f, 0.19178998f)) <= 6,
                "Conrad's native Damage1 point must be usable from the connected survivor ground.");
            UseNativeObject(harness, conrad);
            Assert.AreEqual(harness.UtcNow + BombDeadline, ReadDeadline(harness, 1995).DueAtUtc);
            var wreck = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.AreEqual(24586U, (uint)wreck.EntityClassId,
                "Plant the bomb on the tutorial dropship wreck, not the dropship crate.");
            Assert.AreEqual(UseObjectState.DoorStateClosed, wreck.StateId);
            Assert.IsFalse(harness.Manager.RecordProgress(harness.Client, MissionProgressEvent.Interaction(24911)),
                "Using an unrelated dropship crate must not plant the mission bomb.");
            var damageOffset = Vector3.Transform(new Vector3(3.60532f, 3.21575f, 0.4568534f),
                new Quaternion(-0.20722169f, 0, 0, 0.97829396f));
            var approach = harness.BootcampMap.NavMesh.Nearest(wreck.Position + new Vector3(4, 0, damageOffset.Z));
            Assert.IsTrue(approach.HasValue);
            // Verified mesh Damage1 connector and east collision face, not the server's generous use radius.
            var damagePoint = wreck.Position + damageOffset;
            Assert.IsTrue(approach.Value.X > wreck.Position.X + 2.933772f);
            Assert.IsTrue(Vector3.Distance(approach.Value, damagePoint) <= 6,
                "The ground approach must satisfy the native client's six-metre use check.");
            var path = harness.BootcampMap.NavMesh.FindPath(harness.Client.Player.Position, approach.Value, out var complete);
            Assert.IsNotNull(path);
            Assert.IsTrue(complete, "The actual dialogue/bomb-recovery ground must connect to the wreck-side approach.");
            harness.MovePlayerTo(approach.Value);
            CellManager.Instance.UpdateVisibility(harness.Client);
            UseNativeObject(harness, wreck);
            Assert.AreEqual(CharacterMissionDeadlineState.Satisfied, ReadDeadline(harness, 1995).State);
            harness.UtcNow += TimeSpan.FromSeconds(5);
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            var van = BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564);
            Assert.IsNotNull(van);
            Assert.AreEqual((EntityClasses)3846, van.EntityClass,
                "The real-content regression must load the authored handoff model.");
            CompleteNativeObjective(harness, van, 4);
            Assert.AreEqual(MissionObjectiveState.Completed, harness.Client.Player.Missions[1995].Objectives[4].State);
            Assert.IsTrue(harness.Client.Player.Missions[1995].Completeable);
            AssertSupportedObjectives(harness);
        }

        private static void UseNativeObject(BootcampRuntimeTestHarness.Harness harness, DynamicObject target)
        {
            DynamicObjectManager.Instance.RequestUseObjectPacket(harness.Client, new RequestUseObjectPacket
            {
                ActionId = ActionId.UseObject,
                ActionArgId = DynamicObjectManager.FootlockerUseArgId,
                EntityId = target.EntityId
            });
            harness.AdvanceRecovery(target.WindupTime);
        }

        private static void CompleteNativeObjective(
            BootcampRuntimeTestHarness.Harness harness, Creature npc, uint objectiveId)
        {
            harness.MovePlayerTo(npc);
            CellManager.Instance.UpdateVisibility(harness.Client);
            using var stream = new MemoryStream();
            using (var writer = new PythonWriter(new BinaryWriter(stream, System.Text.Encoding.UTF8, true)))
            {
                writer.WriteTuple(4);
                writer.WriteULong(npc.EntityId);
                writer.WriteUInt(1995);
                writer.WriteUInt(objectiveId);
                writer.WriteUInt(1);
            }
            stream.Position = 0;
            using var reader = new PythonReader(new BinaryReader(stream));
            var packet = new CompleteNPCObjectivePacket();
            packet.Read(reader);
            Assert.AreEqual(stream.Length, stream.Position);
            new NpcManager(harness.Context, harness.Manager).CompleteNPCObjective(harness.Client, packet);
        }

        private static void AssertSupportedObjectives(BootcampRuntimeTestHarness.Harness harness)
        {
            var supported = new[] { 2U, 3U, 1U, 4U };
            CollectionAssert.AreEquivalent(supported, harness.Client.Player.Missions[1995].Objectives.Keys.ToArray());
            harness.Manager.PublishInitialState(harness.Client);
            var packets = harness.Drain();
            foreach (var packet in packets)
            {
                if (packet is MissionStatusInfoPacket status && status.MissionStatusDict.TryGetValue(1995, out var info))
                    AssertVisibleSupportedObjectives(info);
                if (packet is MissionGainedPacket gained && gained.MissionId == 1995)
                {
                    AssertVisibleSupportedObjectives(gained.MissionInfo);
                    CollectionAssert.AreEqual(new[] { 2U },
                        gained.MissionInfo.ObjectivesList.Select(objective => objective.ObjectiveId).ToArray());
                }
                if (packet is ObjectiveRevealedPacket revealed && revealed.MissionId == 1995)
                    CollectionAssert.Contains(supported, revealed.ObjectiveId);
                if (packet is ObjectiveActivatedPacket activated && activated.MissionId == 1995)
                    CollectionAssert.Contains(supported, activated.ObjectiveId);
                if (packet is ObjectiveRevealedPacket or ObjectiveActivatedPacket or
                    ObjectiveCompletedPacket or ObjectiveFailedPacket)
                {
                    using var stream = new MemoryStream(MissionTestContext.Encode(packet));
                    using var reader = new PythonReader(new BinaryReader(stream));
                    Assert.AreEqual(packet is ObjectiveRevealedPacket ? 3 : 2, reader.ReadTuple());
                    var missionId = reader.ReadUInt();
                    var objectiveId = reader.ReadUInt();
                    if (missionId == 1995)
                        CollectionAssert.Contains(supported, objectiveId);
                }
            }
            var latest = packets.OfType<MissionStatusInfoPacket>().Last();
            if (latest.MissionStatusDict.TryGetValue(1995, out var current))
                CollectionAssert.AreEquivalent(
                    harness.Client.Player.Missions[1995].Objectives.Values
                        .Where(objective => objective.State != MissionObjectiveState.Inactive)
                        .Select(objective => objective.ObjectiveId).ToArray(),
                    current.ObjectivesList.Select(objective => objective.ObjectiveId).ToArray());

            void AssertVisibleSupportedObjectives(MissionInfo info)
            {
                var ids = info.ObjectivesList.Select(objective => objective.ObjectiveId).ToArray();
                CollectionAssert.IsSubsetOf(ids, supported);
                Assert.AreEqual(ids.Length, ids.Distinct().Count());
                Assert.IsTrue(info.ObjectivesList.All(objective => objective.State != MissionObjectiveState.Inactive));
            }
        }

        [TestMethod]
        public void ConradInteractionStartsTheDeadlineAndDropshipUsesConfiguredWindup()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartCrashSiteScene(harness);

            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995));

            var conrad = FindScenarioObject(harness, "bootcamp-conrad-corpse");
            harness.UseObjectAndRecover(conrad);

            using (var unit = harness.Context.CreateChar())
            {
                var deadline = unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(deadline);
                Assert.AreEqual(CharacterMissionDeadlineState.Active, deadline.State);
                Assert.AreEqual(harness.UtcNow + BombDeadline, deadline.DueAtUtc);
            }

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.AreEqual(1400U, dropship.WindupTime);
            Assert.AreEqual(MissionObjectiveState.Incomplete, harness.Client.Player.Missions[1995].Objectives[1].State);
        }

        [TestMethod]
        public void MissingScoutAreaStartsOnlyTheSurvivorSceneAndReconnectRebuildsTheCorrectActorsAcrossTheConversation()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, completeable: true);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                1995));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(1995, MissingScoutAreaId)));

            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(
                MissionObjectiveState.Inactive,
                harness.Client.Player.Missions[1995].Objectives[3].State);

            var foreignMap = harness.Maps.GetOrCreatePrivateInstance(
                BootcampRuntimeTestHarness.BootcampMapContextId,
                999);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(foreignMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(foreignMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(foreignMap, "bootcamp-dropship-debris"));

            harness.ReconnectFresh();

            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));

            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                1995,
                2,
                1));

            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[1995].Objectives[2].State);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[3].State);

            harness.ReconnectFresh();

            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2584));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
        }

        [TestMethod]
        public void DisconnectDuringIncompletePlantCancelsTheUseButPreservesTheExistingDeadline()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            using (var unit = harness.Context.CreateChar())
            {
                var before = unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(before);
                harness.BeginUseObject(dropship);
                harness.AdvanceRecovery(Math.Max(0L, dropship.WindupTime - 1L));

                Assert.AreEqual(1, harness.BootcampMap.PerformRecovery.Count);
                Assert.AreEqual(
                    MissionObjectiveState.Incomplete,
                    harness.Client.Player.Missions[1995].Objectives[1].State);

                harness.ReconnectFresh();

                using var reloaded = harness.Context.CreateChar();
                var after = reloaded.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995);
                Assert.IsNotNull(after);
                Assert.AreEqual(before.DueAtUtc, after.DueAtUtc);
                Assert.AreEqual(CharacterMissionDeadlineState.Active, after.State);
            }

            Assert.AreEqual(0, harness.BootcampMap.PerformRecovery.Count);
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            Assert.IsNotNull(FindScenarioObject(harness, "bootcamp-dropship-debris"));
        }

        [TestMethod]
        public void PlantCompletionWithFiveSecondsRemainingAllowsTheFuseToFinishAfterExpiry()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            var dueAt = ReadDeadline(harness, 1995).DueAtUtc;
            harness.UtcNow = dueAt - TimeSpan.FromSeconds(5);

            harness.UseObjectAndRecover(dropship);

            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.AreEqual(CharacterMissionDeadlineState.Satisfied, ReadDeadline(harness, 1995).State);

            harness.UtcNow = dueAt + TimeSpan.FromSeconds(1);

            Assert.IsFalse(harness.Manager.EvaluateDeadlines(harness.Client));
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Completed,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.AreEqual(
                MissionObjectiveState.Inactive,
                harness.Client.Player.Missions[1995].Objectives[4].State);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 2564));

            harness.UtcNow += ArrivalDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            Assert.AreEqual(
                MissionObjectiveState.Incomplete,
                harness.Client.Player.Missions[1995].Objectives[4].State);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                2564));
        }

        [TestMethod]
        public void AbandoningBeforeScoutAreaFailsTheMissionAndMakesRetryAvailableWithoutLeavingStaleActors()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                2561);

            harness.SeedMission(1, 1994, (uint)MissionState.Completed, completeable: true);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                1995));

            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995));

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[1995].State);
            Assert.AreEqual(
                MissionObjectiveState.Failed,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-conrad-corpse"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, "bootcamp-dropship-debris"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 39));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 50));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);

            var classification = harness.Manager.ClassifyNpcConversation(harness.Client.Player, youngblood);
            Assert.IsTrue(classification.TryGetStatus(out _, out var missionIds));
            CollectionAssert.Contains(missionIds, 2005U);
        }

        [TestMethod]
        public void AbandoningAfterSurvivorConversationFailsTheMissionBeforeAnyDeadlineRowExists()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartCrashSiteScene(harness);
            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");

            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995));

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[1995].State);
            Assert.AreEqual(
                MissionObjectiveState.Failed,
                harness.Client.Player.Missions[1995].Objectives[1].State);
            using (var unit = harness.Context.CreateChar())
                Assert.IsNull(unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, 1995));
            Assert.IsTrue(dropship.IsEnabled);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-dropship-debris"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 39));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 50));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);

            var classification = harness.Manager.ClassifyNpcConversation(harness.Client.Player, youngblood);
            Assert.IsTrue(classification.TryGetStatus(out _, out var missionIds));
            CollectionAssert.Contains(missionIds, 2005U);
        }

        [TestMethod]
        public void AbandoningDuringActiveDeadlineFailsAndResetsMission1995OnceWhileUnlockingRetry()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));

            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            Assert.IsTrue(dropship.IsEnabled);

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            AssertFailedMissionReset(
                harness,
                missionId: 1995,
                objectiveId: 1,
                resetScenarioId: 5,
                dropship,
                expectRetryAvailable: true,
                retryMissionId: 2005,
                retryNpc: youngblood);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));
            var retryDeadline = ReadDeadline(harness, 2005);

            Assert.IsFalse(harness.Manager.TryAbandon(harness.Client, 1995));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);
            Assert.AreEqual(CharacterMissionDeadlineState.Active, ReadDeadline(harness, 2005).State);
            Assert.AreEqual(retryDeadline.DueAtUtc, ReadDeadline(harness, 2005).DueAtUtc);
        }

        [TestMethod]
        public void AbandoningDuringTheFuseFailsTheMissionAndMakesRetryAvailable()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var youngblood = StartCrashSiteScene(harness);
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            var dropship = FindScenarioObject(harness, "bootcamp-dropship-debris");
            harness.UseObjectAndRecover(dropship);

            Assert.IsFalse(dropship.IsEnabled);

            Assert.IsTrue(harness.Manager.TryAbandon(harness.Client, 1995));

            AssertFailedMissionReset(
                harness,
                missionId: 1995,
                objectiveId: 1,
                resetScenarioId: 5,
                dropship,
                expectRetryAvailable: true,
                retryMissionId: 2005,
                retryNpc: youngblood);

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                2005));
            var retryDeadline = ReadDeadline(harness, 2005);

            Assert.IsFalse(harness.Manager.TryAbandon(harness.Client, 1995));
            AssertResetScenarioRecordedOnce(harness, 1995, 5, stepCount: 4);
            Assert.AreEqual(CharacterMissionDeadlineState.Active, ReadDeadline(harness, 2005).State);
            Assert.AreEqual(retryDeadline.DueAtUtc, ReadDeadline(harness, 2005).DueAtUtc);
        }

        private static Creature StartCrashSiteScene(BootcampRuntimeTestHarness.Harness harness)
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
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId)
                ?? throw new AssertFailedException("Missing wounded survivor.");
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                1995,
                2,
                1));

            return youngblood;
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

        private static void AssertFailedMissionReset(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId,
            uint objectiveId,
            uint resetScenarioId,
            DynamicObject dropship,
            bool expectRetryAvailable,
            uint retryMissionId,
            Creature retryNpc)
        {
            Assert.AreEqual(MissionState.Failed, harness.Client.Player.Missions[missionId].State);
            Assert.AreEqual(
                MissionObjectiveState.Failed,
                harness.Client.Player.Missions[missionId].Objectives[objectiveId].State);
            Assert.AreEqual(CharacterMissionDeadlineState.Cancelled, ReadDeadline(harness, missionId).State);
            Assert.IsTrue(dropship.IsEnabled);
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-conrad-corpse"));
            Assert.IsNotNull(BootcampRuntimeTestHarness.FindScenarioObject(
                harness.BootcampMap,
                "bootcamp-dropship-debris"));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 39));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(harness.BootcampMap, 50));
            Assert.IsNull(BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CorporalVanValkenbergPackageId));
            AssertResetScenarioRecordedOnce(harness, missionId, resetScenarioId, stepCount: 4);

            var classification = harness.Manager.ClassifyNpcConversation(harness.Client.Player, retryNpc);
            if (!classification.TryGetStatus(out _, out var missionIds))
                missionIds = new System.Collections.Generic.List<uint>();

            if (expectRetryAvailable)
                CollectionAssert.Contains(missionIds, retryMissionId);
            else
                CollectionAssert.DoesNotContain(missionIds, retryMissionId);
        }

        private static void AssertResetScenarioRecordedOnce(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId,
            uint resetScenarioId,
            uint stepCount)
        {
            var keys = ReadScenarioKeys(harness, missionId);
            for (var stepId = 1U; stepId <= stepCount; stepId++)
                Assert.AreEqual(
                    1,
                    keys.Count(key => key == $"scenario:{resetScenarioId}:step:{stepId}"),
                    $"Expected reset scenario {resetScenarioId} step {stepId} exactly once.");
        }

        private static string[] ReadScenarioKeys(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId)
        {
            using var unit = harness.Context.CreateChar();
            return unit.CharacterMissionScenario.Get(harness.Client.Player.Id, missionId)
                .Select(entry => entry.StepKey)
                .ToArray();
        }
    }
}
