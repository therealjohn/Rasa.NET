using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Packets;
    using Rasa.Packets.Game.Server;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.Mission.Server;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Test.Gameplay;

    [TestClass]
    [DoNotParallelize]
    public class BootcampProtocolTests
    {
        private const uint MissionCaptureTheFlag = 1994;
        private const uint MissionCallingForReinforcements = 1995;
        private const uint MissingScoutAreaId = 435;
        private const uint CaveInAreaId = 439;
        private static readonly TimeSpan BombDeadline = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan YoungbloodDelay = TimeSpan.FromSeconds(7);

        [TestMethod]
        public void SnapshotAndMissionGainPacketsReflectTheAuthoritativeBootcampState()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var mcAllister = harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId);
            harness.Drain();

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                mcAllister.EntityId,
                BootcampRuntimeTestHarness.MissionInitiation));
            AssertPacketTypes(
                harness.Drain(),
                typeof(MissionGainedPacket),
                typeof(NPCConversationStatusPacket));

            harness.Manager.PublishInitialState(harness.Client);
            var snapshot = harness.Drain().OfType<MissionStatusInfoPacket>().Single();
            Assert.AreEqual(
                BootcampRuntimeTestHarness.MissionInitiation,
                snapshot.MissionStatusDict.Single().Key);
            CollectionAssert.AreEqual(
                new[] { 1U, 2U },
                snapshot.MissionStatusDict[BootcampRuntimeTestHarness.MissionInitiation]
                    .ObjectivesList
                    .Select(objective => objective.ObjectiveId)
                    .ToArray());
        }

        [TestMethod]
        public void DeadlineMissionInfoPublishesTheCountdownThenClearsItWhenTheChargeIsPlanted()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            PrepareCallingForReinforcementsDeadline(harness);

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(24990)));
            var startPackets = harness.Drain();
            AssertRelativeOrder(
                startPackets,
                typeof(ObjectiveCompletedPacket),
                typeof(ObjectiveRevealedPacket),
                typeof(ObjectiveActivatedPacket),
                typeof(MissionStatusInfoPacket));
            var startStatus = startPackets
                .Single(packet => packet.GetType() == typeof(MissionStatusInfoPacket)) as MissionStatusInfoPacket;
            Assert.IsNotNull(startStatus);
            Assert.AreEqual(1, startStatus.MissionStatusDict.Count);
            CollectionAssert.AreEqual(
                new[] { MissionCallingForReinforcements },
                startStatus.MissionStatusDict.Keys.ToArray());
            var dueAt = ReadDeadline(harness, MissionCallingForReinforcements).DueAtUtc;
            AssertObjectiveTimer(
                startStatus.MissionStatusDict[MissionCallingForReinforcements],
                objectiveId: 1,
                expectedState: MissionObjectiveState.Incomplete,
                expectedSeconds: checked((uint)Math.Ceiling((dueAt - harness.UtcNow).TotalSeconds)));

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(24911)));
            var satisfactionPackets = harness.Drain();
            AssertRelativeOrder(
                satisfactionPackets,
                typeof(MissionStatusInfoPacket));
            Assert.AreEqual(
                CharacterMissionDeadlineState.Satisfied,
                ReadDeadline(harness, MissionCallingForReinforcements).State);
            var satisfactionStatus = satisfactionPackets
                .Single(packet => packet.GetType() == typeof(MissionStatusInfoPacket)) as MissionStatusInfoPacket;
            Assert.IsNotNull(satisfactionStatus);
            Assert.AreEqual(1, satisfactionStatus.MissionStatusDict.Count);
            AssertObjectiveTimerCleared(
                satisfactionStatus.MissionStatusDict[MissionCallingForReinforcements],
                objectiveId: 1,
                expectedState: MissionObjectiveState.Incomplete);
        }

        [TestMethod]
        public void DeadlineFailurePublishesObjectiveMissionAndStatusPacketsInOrderWithTheTimerCleared()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            PrepareCallingForReinforcementsDeadline(harness);
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Interaction(24990)));
            harness.Drain();

            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));
            var failurePackets = harness.Drain();
            AssertRelativeOrder(
                failurePackets,
                typeof(ObjectiveFailedPacket),
                typeof(MissionFailedPacket),
                typeof(MissionStatusInfoPacket));
            Assert.AreEqual(
                CharacterMissionDeadlineState.Expired,
                ReadDeadline(harness, MissionCallingForReinforcements).State);
            var failureStatus = failurePackets
                .Single(packet => packet.GetType() == typeof(MissionStatusInfoPacket)) as MissionStatusInfoPacket;
            Assert.IsNotNull(failureStatus);
            Assert.AreEqual(1, failureStatus.MissionStatusDict.Count);
            AssertObjectiveTimerCleared(
                failureStatus.MissionStatusDict[MissionCallingForReinforcements],
                objectiveId: 1,
                expectedState: MissionObjectiveState.Failed);
        }

        [TestMethod]
        public void ObjectiveCounterCompletionRevealActivationAndTutorialPacketsStayInProductionOrder()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var actors = SeedActors(harness);
            harness.Drain();

            harness.SeedMission(
                harness.Client.Player.Id,
                BootcampRuntimeTestHarness.MissionInitiation,
                (uint)MissionState.Completed,
                false);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                actors.McAllister.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp));
            harness.Drain();

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                4,
                1));
            harness.Drain();
            BootcampRuntimeTestHarness.LootEquipmentCrate(harness);
            harness.Drain();
            PrepareEquipping(harness);
            Assert.IsTrue(RecordTemplateEquipProgress(harness, 13066));
            harness.Drain();
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Delessio.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                5,
                1));
            harness.Drain();
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                6,
                1));
            harness.Drain();

            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.ObjectHit(
                    PracticeTargetManager.EntityClassId,
                    (uint)ActionId.WeaponAttack)));
            AssertRelativeOrder(
                harness.Drain(),
                typeof(ObjectiveCompletedPacket),
                typeof(ObjectiveRevealedPacket),
                typeof(ObjectiveActivatedPacket));

            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.Hartmann.EntityId,
                BootcampRuntimeTestHarness.MissionGearingUp,
                9,
                1));
            AssertRelativeOrder(
                harness.Drain(),
                typeof(ObjectiveCompletedPacket),
                typeof(ObjectiveRevealedPacket),
                typeof(ObjectiveActivatedPacket),
                typeof(SkillsPacket),
                typeof(AbilityDrawerPacket),
                typeof(DisplayPlayerTutorialNotificationPacket),
                typeof(PlayTutorialAudioPacket));
        }

        [TestMethod]
        public void CounterPacketsPublishBeforeCompletionOnTheProtocolBoundary()
        {
            var counters = new Dictionary<uint, MissionObjectiveCounterDefinition>
            {
                [0] = new MissionObjectiveCounterDefinition(0, 2, 4)
            };
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.IncrementCounterOnExactSubject(
                    MissionProgressEventKind.CreatureKilled,
                    75,
                    0,
                    2,
                    4),
                counters: counters);
            context.SeedMission(context.Client.Player.Id, 321, (uint)MissionState.Active, completeable: false);
            context.ReloadPlayerMissions();
            context.Drain();

            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client,
                MissionProgressEvent.Creature(75)));
            AssertPacketTypes(context.Drain(), typeof(UpdateObjectiveCounterPacket));

            Assert.IsTrue(context.Manager.RecordProgress(
                context.Client,
                MissionProgressEvent.Creature(75)));
            AssertPacketTypes(
                context.Drain(),
                typeof(UpdateObjectiveCounterPacket),
                typeof(ObjectiveCompletedPacket),
                typeof(MissionCompleteablePacket));
        }

        [TestMethod]
        public void TimeoutCompletionRewardConversationAndTransferPacketsFollowTheBootcampClientContract()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var actors = SeedActors(harness);
            harness.Drain();

            harness.SeedMission(
                harness.Client.Player.Id,
                BootcampRuntimeTestHarness.MissionGearingUp,
                (uint)MissionState.Completed,
                false);
            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                actors.DeSimone.EntityId,
                MissionCaptureTheFlag));
            harness.Drain();
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                actors.DeSimone.EntityId,
                MissionCaptureTheFlag,
                4,
                1));
            harness.Drain();
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCaptureTheFlag, CaveInAreaId)));
            harness.Drain();

            var tizzik = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.TizzikGiCreatureId);
            Assert.IsNotNull(tizzik);
            new CreatureManager(null, new ManifestationManager(harness.Context), harness.Manager)
                .HandleCreatureKill(harness.BootcampMap, tizzik, harness.Client.Player);
            harness.UtcNow += YoungbloodDelay;
            Assert.IsTrue(harness.Manager.TickScenarios(harness.Client));
            harness.Drain();

            var youngblood = BootcampRuntimeTestHarness.FindCreature(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId);
            Assert.IsNotNull(youngblood);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                3,
                1));
            harness.Drain();
            Assert.IsTrue(harness.Manager.TryCompleteNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                selectionIndex: null,
                rating: null));
            var capturePackets = harness.Drain();
            AssertPacketTypes(
                capturePackets,
                typeof(MissionCompleteablePacket),
                typeof(MissionCompletedPacket),
                typeof(NPCConversationStatusPacket),
                typeof(NPCConversationStatusPacket),
                typeof(NPCConversationStatusPacket),
                typeof(NPCConversationStatusPacket));
            Assert.IsFalse(capturePackets
                .OfType<MissionCompleteablePacket>()
                .Single()
                .IsCompleteable);

            Assert.IsTrue(harness.Manager.TryRewardNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCaptureTheFlag,
                selectionIndex: null,
                rating: null));
            AssertRelativeOrder(
                harness.Drain(),
                typeof(ExperienceChangedPacket),
                typeof(MissionRewardedPacket));

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCallingForReinforcements));
            harness.Drain();
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCallingForReinforcements, MissingScoutAreaId)));
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                MissionCallingForReinforcements,
                10,
                1));
            harness.UseObjectAndRecover(FindScenarioObject(harness, "bootcamp-conrad-corpse"));
            harness.Drain();

            harness.UtcNow += BombDeadline + TimeSpan.FromSeconds(1);
            Assert.IsTrue(harness.Manager.EvaluateDeadlines(harness.Client));
            AssertRelativeOrder(
                harness.Drain(),
                typeof(ObjectiveFailedPacket),
                typeof(MissionFailedPacket));

            var npcManager = new NpcManager(harness.Context, harness.Manager);
            npcManager.RequestNpcConverse(
                harness.Client,
                new RequestNPCConversePacket { EntityId = youngblood.EntityId });
            AssertPacketTypes(harness.Drain(), typeof(ConversePacket));

            using var context = new BootcampSelectionTestContext();
            context.SeedAccount(901);
            var characterId = context.SeedCharacter(
                901,
                1,
                "Transfer",
                mapContextId: BootcampSelectionTestContext.BootcampMapContextId,
                x: -255.3125,
                y: 101.05078,
                z: -70.4375,
                experience: 49250,
                level: 5);
            context.SeedStartingExperience(characterId, CharacterStartingExperienceState.Bootcamp);
            context.SeedMission(characterId, BootcampSelectionTestContext.MissionFinale, MissionState.Active, true);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.ExitPadWaypointId, WaypointType.Dropship);
            context.SeedLightningGrant(characterId);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasWaypointId, WaypointType.Waypoint);
            context.SeedWaypoint(characterId, BootcampSelectionTestContext.AliaDasHospitalId, WaypointType.Hospital);
            context.SeedPersonalInventory(901, characterId, 13066, 13096, 13156, 13186, 13713, 28);

            var client = context.CreateSelectionClient(901);
            context.Characters.RequestSwitchToCharacterInSlot(
                client,
                new Rasa.Packets.Game.Client.RequestSwitchToCharacterInSlotPacket
                {
                    SlotNum = 1,
                    SkipBootcamp = false
                });
            context.MaterializeLoadedClient(client);
            MissionTestContext.Drain(client);

            var privateMap = context.Maps.FindOwnedPrivateInstance(
                BootcampSelectionTestContext.BootcampMapContextId,
                characterId);
            Assert.IsNotNull(privateMap);
            context.Objects.SelectWaypoint(
                client,
                new SelectWaypointPacket
                {
                    WaypointId = BootcampSelectionTestContext.ExitPadWaypointId,
                    MapInstanceId = privateMap.InstanceId
                });
            AssertRelativeOrder(
                MissionTestContext.Drain(client),
                typeof(PreWonkavatePacket),
                typeof(WonkavatePacket));
        }

        private static BootcampActors SeedActors(BootcampRuntimeTestHarness.Harness harness) =>
            new(
                harness.AddNpc(BootcampRuntimeTestHarness.MajorMcAllisterCreatureId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CaptainDelessioCreatureId,
                    BootcampRuntimeTestHarness.CaptainDelessioPackageId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CorporalHartmannCreatureId,
                    BootcampRuntimeTestHarness.CorporalHartmannPackageId),
                harness.AddNpc(
                    BootcampRuntimeTestHarness.CorporalDeSimoneCreatureId,
                    BootcampRuntimeTestHarness.CorporalDeSimonePackageId));

        private static DynamicObject FindScenarioObject(
            BootcampRuntimeTestHarness.Harness harness,
            string key) =>
            BootcampRuntimeTestHarness.FindScenarioObject(harness.BootcampMap, key)
            ?? throw new AssertFailedException($"Missing scenario object {key}.");

        private static void PrepareEquipping(BootcampRuntimeTestHarness.Harness harness)
        {
            harness.Client.Player.AppearanceData ??= new Dictionary<EquipmentData, AppearanceData>();
            typeof(ManifestationManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, new ManifestationManager(harness.Context));
        }

        private static bool RecordTemplateEquipProgress(
            BootcampRuntimeTestHarness.Harness harness,
            uint templateId)
        {
            for (var slot = 0; slot < harness.Client.Player.Inventory.PersonalInventory.Count; slot++)
            {
                var entityId = harness.Client.Player.Inventory.PersonalInventory[slot];
                if (entityId == 0)
                    continue;

                var item = EntityManager.Instance.GetItem(entityId);
                if (item?.ItemTemplate?.ItemTemplateId != templateId)
                    continue;

                return harness.Manager.RecordProgress(
                    harness.Client,
                    MissionProgressEvent.ItemEquipped(
                        (uint)item.ItemTemplate.Class,
                        item.ItemTemplate.ItemTemplateId));
            }

            throw new AssertFailedException($"Could not find inventory template {templateId}.");
        }

        private static void AssertPacketTypes(
            IReadOnlyList<PythonPacket> packets,
            params Type[] expected) =>
            CollectionAssert.AreEqual(
                expected,
                packets.Select(packet => packet.GetType()).ToArray());

        private static void AssertRelativeOrder(
            IReadOnlyList<PythonPacket> packets,
            params Type[] orderedTypes)
        {
            var start = 0;
            foreach (var type in orderedTypes)
            {
                var index = packets
                    .Select((packet, packetIndex) => (packet, packetIndex))
                    .Where(entry => entry.packetIndex >= start && type.IsInstanceOfType(entry.packet))
                    .Select(entry => entry.packetIndex)
                    .DefaultIfEmpty(-1)
                    .First();
                Assert.IsTrue(index >= start, $"Missing packet {type.Name} in sequence {DescribePackets(packets)}.");
                start = index + 1;
            }
        }

        private static string DescribePackets(IEnumerable<PythonPacket> packets) =>
            string.Join(" -> ", packets.Select(packet => packet.GetType().Name));

        private static void PrepareCallingForReinforcementsDeadline(
            BootcampRuntimeTestHarness.Harness harness)
        {
            var youngblood = harness.AddNpc(
                BootcampRuntimeTestHarness.CaptainYoungbloodCreatureId,
                BootcampRuntimeTestHarness.CaptainYoungbloodPackageId);
            harness.SeedMission(
                harness.Client.Player.Id,
                MissionCaptureTheFlag,
                (uint)MissionState.Completed,
                true);
            harness.Drain();

            Assert.IsTrue(harness.Manager.TryAcceptNpcMission(
                harness.Client,
                youngblood.EntityId,
                MissionCallingForReinforcements));
            harness.Drain();
            Assert.IsTrue(harness.Manager.RecordProgress(
                harness.Client,
                MissionProgressEvent.Area(MissionCallingForReinforcements, MissingScoutAreaId)));
            var survivor = BootcampRuntimeTestHarness.FindNpcByPackage(
                harness.BootcampMap,
                BootcampRuntimeTestHarness.WoundedSurvivorPackageId);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(harness.Manager.TryCompleteNpcObjective(
                harness.Client,
                survivor.EntityId,
                MissionCallingForReinforcements,
                10,
                1));
            harness.Drain();
        }

        private static CharacterMissionDeadlineEntry ReadDeadline(
            BootcampRuntimeTestHarness.Harness harness,
            uint missionId)
        {
            using var unit = harness.Context.CreateChar();
            return unit.CharacterMissionDeadlines.Get(harness.Client.Player.Id, missionId)
                   ?? throw new AssertFailedException($"Missing deadline row for mission {missionId}.");
        }

        private static void AssertObjectiveTimer(
            MissionInfo info,
            uint objectiveId,
            MissionObjectiveState expectedState,
            uint expectedSeconds)
        {
            var objective = info.ObjectivesList.Single(entry => entry.ObjectiveId == objectiveId);
            Assert.AreEqual(expectedState, objective.State);
            Assert.IsTrue(objective.TimeRemaining.HasValue);
            Assert.AreEqual(expectedSeconds, objective.TimeRemaining.Value);
        }

        private static void AssertObjectiveTimerCleared(
            MissionInfo info,
            uint objectiveId,
            MissionObjectiveState expectedState)
        {
            var objective = info.ObjectivesList.Single(entry => entry.ObjectiveId == objectiveId);
            Assert.AreEqual(expectedState, objective.State);
            Assert.IsFalse(objective.TimeRemaining.HasValue);
        }

        private sealed record BootcampActors(
            Creature McAllister,
            Creature Delessio,
            Creature Hartmann,
            Creature DeSimone);
    }
}
