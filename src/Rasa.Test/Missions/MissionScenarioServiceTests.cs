extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using ClientState = RasaGame::Rasa.Data.ClientState;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Packets;
    using Rasa.Packets.ClientMethod.Server;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionScenarioServiceTests
    {
        [TestMethod]
        public void RewardGrantPublicationFailureLeavesDurableScenarioRewardsCommittedOnce()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateRewardScenarioFixture();
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            var maps = new MapChannelManager(
                null,
                privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
            using var managers = CreateManagers(maps);
            var manager = LoadManager(
                context,
                fixture,
                () => now,
                maps,
                beforeRewardItemPublication: _ => throw new InvalidOperationException("publish failed"));
            context.AddRewardTemplate(28, 3147);
            context.AddRewardTemplate(29, 3147);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();

            var before = context.ReadRewardTotals();

            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));
            Assert.IsFalse(manager.TryExecuteScenario(context.Client, 321, 60));

            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience + 125U, after.Experience);
            Assert.AreEqual(before.Credits + 75, after.Credits);
            Assert.AreEqual(before.Prestige + 10, after.Prestige);
            Assert.AreEqual(before.ItemCount + 3, after.ItemCount);
            Assert.IsTrue(context.Client.AccountEntry.CanSkipBootcamp);
            using var unit = context.CreateChar();
            var learnedSkill = unit.CharacterSkills.GetCharacterSkills(context.Client.Player.Id)
                .Single(skill => skill.SkillId == 901);
            Assert.AreEqual(194, learnedSkill.AbilityId);
            Assert.AreEqual(2, learnedSkill.SkillLevel);
            var slotted = unit.CharacterAbilityDrawers.GetCharacterAbilities(context.Client.Player.Id)
                .Single(entry => entry.AbilitySlot == 3);
            Assert.AreEqual(194, slotted.AbilityId);
            Assert.AreEqual(2U, slotted.AbilityLevel);
            Assert.IsTrue(unit.CharacterQualifications.HasQualification(
                context.Client.Player.Id,
                CharacterQualificationKey.BootcampComplete));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "scenario:60:step:1",
                    "scenario:60:step:2",
                    "scenario:60:step:3",
                    "scenario:60:step:4"
                },
                unit.CharacterMissionScenario.Get(context.Client.Player.Id, 321)
                    .Select(entry => entry.StepKey)
                    .ToArray());
        }

        [TestMethod]
        public void TickExecutesScheduledScenarioExactlyOnceWhenDelayElapses()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateScheduledScenarioFixture();
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            var maps = new MapChannelManager(
                null,
                privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
            using var managers = CreateManagers(maps);
            MissionManager manager = null;
            var service = new MissionScenarioService(
                () => context,
                () => manager,
                new ManifestationManager(context),
                () => maps,
                () => CreatureManager.Instance,
                () => DynamicObjectManager.Instance,
                () => CommunicatorManager.Instance,
                () => now);
            manager = LoadManager(context, fixture, () => now, maps, scenarioService: service);
            context.AddRewardTemplate(28, 3147);
            context.AddRewardTemplate(29, 3147);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();

            var before = context.ReadRewardTotals();

            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));
            Assert.IsFalse(manager.TickScenarios(context.Client));

            now = now.AddMilliseconds(5000);

            Assert.IsTrue(manager.TickScenarios(context.Client));
            Assert.IsFalse(manager.TickScenarios(context.Client));

            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.Experience + 125U, after.Experience);
            Assert.AreEqual(before.Credits + 75, after.Credits);
            Assert.AreEqual(before.Prestige + 10, after.Prestige);
            Assert.AreEqual(before.ItemCount + 3, after.ItemCount);
        }

        [TestMethod]
        public void RebuildRestoresTransientScenarioRuntimeOnOwnedPrivateReconnect()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            PrepareScenarioCreatureClass();
            var fixture = CreateRuntimeActorFixture();
            context.Map.MapInfo = new MapInfo(1985, "bootcamp_fixture", 1556, 0);
            context.Client.Player.MapContextId = 1985;
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            MissionManager manager = null;
            MapChannelManager maps = null;
            var objects = new DynamicObjectManager(null, maps);
            var creatures = new CreatureManager(null, new ManifestationManager(context));
            creatures.LoadedCreatures[501] = new Creature
            {
                DbId = 501,
                EntityClass = (EntityClasses)4001,
                Npc = new Npc { NpcPackageId = 501 },
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>()
            };
            var service = new MissionScenarioService(
                () => context,
                () => manager,
                new ManifestationManager(context),
                () => maps,
                () => creatures,
                () => objects,
                () => CommunicatorManager.Instance,
                () => now);
            maps = new MapChannelManager(
                null,
                privateInstances: new PrivateMapInstanceService(),
                scenarioService: service);
            maps.MapChannelArray.Add(1985, context.Map);
            objects = new DynamicObjectManager(null, maps);
            manager = LoadManager(context, fixture, () => now, maps, objects, creatures, service);
            context.AddRewardTemplate(28, 3147);
            using var singletons = new ManagerInstances(maps, objects, creatures, manager);
            var owned = maps.GetOrCreatePrivateInstance(1985, context.Client.Player.Id);
            MoveClientToMap(context.Client, context.Map, owned);
            var giver = context.AddNpc(101, owned);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();
            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));

            Assert.AreEqual(1, CountScenarioCreatures(owned));
            Assert.AreEqual(1, CountScenarioObjects(owned));

            maps.ReleaseOwnedPrivateInstances(context.Client.Player.Id);
            var rebuilt = maps.GetOrCreatePrivateInstance(1985, context.Client.Player.Id);

            Assert.AreNotSame(owned, rebuilt);
            Assert.AreEqual(1, CountScenarioCreatures(rebuilt));
            Assert.AreEqual(1, CountScenarioObjects(rebuilt));
            Assert.IsFalse(GetScenarioObject(rebuilt, "bootcamp-crate").IsEnabled);
        }

        [TestMethod]
        public void RebuildRecreatesMissingRuntimeActorsWithoutRepeatingDurableSteps()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            PrepareScenarioCreatureClass();
            var fixture = CreateRuntimeActorFixture();
            context.Map.MapInfo = new MapInfo(1985, "bootcamp_fixture", 1556, 0);
            context.Client.Player.MapContextId = 1985;
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            MissionManager manager = null;
            MapChannelManager maps = null;
            var objects = new DynamicObjectManager(null, maps);
            var creatures = new CreatureManager(null, new ManifestationManager(context));
            creatures.LoadedCreatures[501] = new Creature
            {
                DbId = 501,
                EntityClass = (EntityClasses)4001,
                Npc = new Npc { NpcPackageId = 501 },
                AppearanceData = new Dictionary<EquipmentData, AppearanceData>()
            };
            var service = new MissionScenarioService(
                () => context,
                () => manager,
                new ManifestationManager(context),
                () => maps,
                () => creatures,
                () => objects,
                () => CommunicatorManager.Instance,
                () => now);
            maps = new MapChannelManager(
                null,
                privateInstances: new PrivateMapInstanceService(),
                scenarioService: service);
            maps.MapChannelArray.Add(1985, context.Map);
            objects = new DynamicObjectManager(null, maps);
            manager = LoadManager(context, fixture, () => now, maps, objects, creatures, service);
            context.AddRewardTemplate(28, 3147);
            using var singletons = new ManagerInstances(maps, objects, creatures, manager);
            var owned = maps.GetOrCreatePrivateInstance(1985, context.Client.Player.Id);
            MoveClientToMap(context.Client, context.Map, owned);
            var giver = context.AddNpc(101, owned);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();
            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));

            var creature = owned.MapCellInfo.Cells.Values.SelectMany(cell => cell.CreatureList)
                .Single(current => current.SpawnPool?.ScenarioKey == "scenario:60:step:1");
            var dynamicObject = GetScenarioObject(owned, "bootcamp-crate");
            CellManager.Instance.RemoveCreatureFromWorld(owned, creature);
            CellManager.Instance.RemoveFromWorld(owned, dynamicObject);
            owned.DynamicObjects.Remove(dynamicObject);

            manager.RebuildScenarioRuntime(context.Client.Player.Id, owned);

            Assert.AreEqual(1, CountScenarioCreatures(owned));
            Assert.AreEqual(1, CountScenarioObjects(owned));
        }

        [TestMethod]
        public void ObjectiveDeadlineTransferAndScenarioEventStepsApplyThroughAuthoritativeMissionFlows()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateObjectiveTransferFixture();
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            var maps = new MapChannelManager(
                null,
                () => 1000,
                updateCharacter: (_, _, _) => { },
                disconnect: _ => Assert.Fail("Transfer should not disconnect."),
                refreshStats: (_, _) => { },
                assignPlayer: _ => { },
                enterMapChannels: _ => { },
                privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
            maps.MapChannelArray.Add(1221, new MapChannel
            {
                MapInfo = new MapInfo(1221, "mission_fixture_target", 1556, 0),
                ClientList = new List<Client>(),
                PlayerLimit = 128
            });
            using var managers = CreateManagers(maps);
            var manager = LoadManager(context, fixture, () => now, maps);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();

            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));

            Assert.AreEqual(MissionObjectiveState.Completed,
                context.Client.Player.Missions[321].Objectives[10].State);
            Assert.AreEqual(MissionObjectiveState.Failed,
                context.Client.Player.Missions[321].Objectives[11].State);
            using var unit = context.CreateChar();
            var deadline = unit.CharacterMissionDeadlines.Get(context.Client.Player.Id, 321);
            Assert.IsNotNull(deadline);
            Assert.AreEqual(CharacterMissionDeadlineState.Cancelled, deadline.State);
            Assert.AreEqual(ClientState.Teleporting, context.Client.State);
            Assert.IsNotNull(context.Client.PendingTransfer);
            Assert.AreEqual(1221U, context.Client.PendingTransfer.DestinationMap.MapInfo.MapContextId);
            Assert.AreEqual(
                1,
                context.Drain().OfType<ObjectiveCompletedPacket>().Count(packet => packet.ObjectiveId == 10));
            CollectionAssert.Contains(
                unit.CharacterMissionScenario.Get(context.Client.Player.Id, 321)
                    .Select(entry => entry.StepKey)
                    .ToList(),
                "scenario:60:step:7");
        }

        [TestMethod]
        public void ResetScenarioAttemptByTargetScenarioAllowsACompletedScenarioToRunAgain()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var fixture = CreateResetFixture();
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            var maps = new MapChannelManager(
                null,
                privateInstances: new PrivateMapInstanceService());
            maps.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
            using var managers = CreateManagers(maps);
            var manager = LoadManager(context, fixture, () => now, maps);
            var giver = context.AddNpc(101);

            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();

            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 61));
            Assert.IsFalse(manager.TryExecuteScenario(context.Client, 321, 61));
            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 60));
            Assert.IsTrue(manager.TryExecuteScenario(context.Client, 321, 61));

            using var unit = context.CreateChar();
            var keys = unit.CharacterMissionScenario.Get(context.Client.Player.Id, 321)
                .Select(entry => entry.StepKey)
                .ToArray();
            Assert.AreEqual(1, keys.Count(key => key == "scenario:61:step:1"));
        }

        private static MissionManager LoadManager(
            MissionTestContext context,
            MissionContentFixture fixture,
            Func<DateTime> utcNow,
            MapChannelManager maps,
            DynamicObjectManager objects = null,
            CreatureManager creatures = null,
            IMissionScenarioService scenarioService = null,
            Action<Item> beforeRewardItemPublication = null,
            Action<PythonPacket> beforeMissionPacketPublication = null)
        {
            MissionManager manager = null;
            var manifestation = new ManifestationManager(context);
            var deadlineService = new MissionDeadlineService(
                () => context,
                () => manager,
                utcNow);
            manager = new MissionManager(
                new MissionContentLoadingFactory(context, fixture.CreateWorldUnitOfWork()),
                new Dictionary<uint, Mission>(),
                new Dictionary<uint, MissionRewardDefinition>(),
                manifestation,
                beforeRewardItemPublication,
                beforeMissionPacketPublication,
                deadlineService,
                scenarioService);

            var report = manager.LoadMissions();
            Assert.IsFalse(
                report.BlocksReadiness,
                string.Join(" | ", report.Diagnostics.Select(diagnostic => diagnostic.Code)));
            return manager;
        }

        private static ManagerInstances CreateManagers(MapChannelManager maps)
        {
            var objects = new DynamicObjectManager(null, maps);
            var creatures = new CreatureManager(null, new ManifestationManager(null));
            return new ManagerInstances(maps, objects, creatures, null);
        }

        private static void MoveClientToMap(Client client, MapChannel origin, MapChannel destination)
        {
            CellManager.Instance.RemoveFromWorld(client);
            origin.ClientList.Remove(client);
            client.Player.MapChannel = destination;
            client.Player.RuntimeMapChannel = destination;
            client.Player.MapContextId = destination.MapInfo.MapContextId;
            destination.ClientList.Add(client);
            CellManager.Instance.AddToWorld(client);
        }

        private static int CountScenarioCreatures(MapChannel map) =>
            map.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList)
                .Count(creature => creature.SpawnPool?.ScenarioKey == "scenario:60:step:1");

        private static int CountScenarioObjects(MapChannel map) =>
            map.DynamicObjects.Count(dynamicObject => dynamicObject.ScenarioKey == "bootcamp-crate");

        private static DynamicObject GetScenarioObject(MapChannel map, string key) =>
            map.DynamicObjects.Single(dynamicObject => dynamicObject.ScenarioKey == key);

        private static void PrepareScenarioCreatureClass()
        {
            var classes = EntityClassManager.Instance.LoadedEntityClasses;
            if (!classes.TryGetValue((EntityClasses)4001, out var entityClass))
            {
                entityClass = new EntityClass(4001, "scenario_creature", 0, 0,
                    new List<AugmentationType>(), true);
                classes.Add((EntityClasses)4001, entityClass);
            }

            if (!entityClass.Augmentations.Contains(AugmentationType.Creature))
                entityClass.Augmentations.Add(AugmentationType.Creature);
        }

        private static MissionContentFixture CreateRewardScenarioFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.AddRange(
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.GrantRewardPackage,
                    Sequence = 1,
                    RewardId = 40,
                    Comment = "Grant authored reward"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 2,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.GrantSkillAbility,
                    Sequence = 2,
                    SkillId = 901,
                    AbilityId = 194,
                    SkillLevel = 2,
                    AbilitySlot = 3,
                    Comment = "Grant skill"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 3,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.SetQualification,
                    Sequence = 3,
                    QualificationKey = CharacterQualificationKey.BootcampComplete,
                    QualificationValue = MissionScenarioStepEntry.GrantedQualificationValue,
                    Comment = "Grant qualification"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 4,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.SetAccountSkipEntitlement,
                    Sequence = 4,
                    AccountSkipEntitlement = true,
                    Comment = "Grant skip entitlement"
                });
            return fixture;
        }

        private static MissionContentFixture CreateScheduledScenarioFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Scenarios.Add(new MissionScenarioEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ScenarioId = 61,
                Requirement = MissionContentRequirement.Required,
                Name = "DelayedReward",
                Comment = "Delayed reward scenario"
            });
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.Add(new MissionScenarioStepEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ScenarioId = 60,
                StepId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionScenarioStepKind.ScheduleScenario,
                Sequence = 1,
                TargetScenarioId = 61,
                DelayMilliseconds = 5000,
                Comment = "Schedule reward"
            });
            fixture.ScenarioSteps.Add(new MissionScenarioStepEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ScenarioId = 61,
                StepId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionScenarioStepKind.GrantRewardPackage,
                Sequence = 1,
                RewardId = 40,
                Comment = "Delayed reward"
            });
            return fixture;
        }

        private static MissionContentFixture CreateRuntimeActorFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.MapContextIds.Add(1985);
            fixture.SpawnGroups.Clear();
            fixture.Spawns.Clear();
            fixture.SpawnGroups.Add(new MissionSpawnGroupEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                SpawnGroupId = 50,
                Requirement = MissionContentRequirement.Required,
                AreaId = null,
                MapContextId = 1985,
                Enabled = false,
                RespawnSeconds = null,
                Comment = "Scenario spawn group"
            });
            fixture.Spawns.Add(new MissionSpawnEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                SpawnGroupId = 50,
                SpawnId = 1,
                CreatureId = 501,
                PosX = 8,
                PosY = 9,
                PosZ = 10,
                Rotation = 0.25,
                Quantity = 1
            });
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.AddRange(
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.SpawnGroup,
                    Sequence = 1,
                    SpawnGroupId = 50,
                    Comment = "Spawn creatures"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 2,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.SpawnDynamicObject,
                    Sequence = 2,
                    DynamicObjectKey = "bootcamp-crate",
                    EntityClassId = 3147,
                    PosX = 12,
                    PosY = 0,
                    PosZ = 6,
                    Orientation = 0.5,
                    InitialInteractionEnabled = true,
                    Comment = "Spawn crate"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 3,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.DisableInteraction,
                    Sequence = 3,
                    EntityClassId = 3147,
                    Comment = "Disable crate interaction"
                });
            return fixture;
        }

        private static MissionContentFixture CreateObjectiveTransferFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Objectives.Clear();
            fixture.Actions.Clear();
            fixture.Rewards.Clear();
            fixture.RewardItems.Clear();
            fixture.MapContextIds.Add(1221);
            fixture.Objectives.AddRange(
                new MissionObjectiveDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 10,
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 2101,
                    ClientBodyTextId = 2102,
                    Ordinal = 1,
                    InitialState = (byte)MissionObjectiveState.Incomplete,
                    IsRequired = true,
                    Comment = "Primary objective"
                },
                new MissionObjectiveDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 11,
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 2201,
                    ClientBodyTextId = 2202,
                    Ordinal = 2,
                    InitialState = (byte)MissionObjectiveState.Inactive,
                    IsRequired = true,
                    Comment = "Follow-up objective"
                },
                new MissionObjectiveDefinitionEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ObjectiveId = 12,
                    Requirement = MissionContentRequirement.Required,
                    ClientNameTextId = 2301,
                    ClientBodyTextId = 2302,
                    Ordinal = 3,
                    InitialState = (byte)MissionObjectiveState.Incomplete,
                    IsRequired = false,
                    Comment = "Deadline objective"
                });
            fixture.Transitions.Clear();
            fixture.Actions.Clear();
            fixture.Triggers.Clear();
            fixture.Transitions.Add(new MissionObjectiveTransitionEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 12,
                TransitionId = 30,
                Requirement = MissionContentRequirement.Required,
                Sequence = 1,
                FromState = (byte)MissionObjectiveState.Incomplete,
                ToState = (byte)MissionObjectiveState.Completed,
                Comment = "Deadline completes objective"
            });
            fixture.Triggers.Add(new MissionTriggerEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ObjectiveId = 12,
                TransitionId = 30,
                TriggerId = 1,
                Requirement = MissionContentRequirement.Required,
                Kind = MissionTriggerKind.TimerElapsed,
                Sequence = 1,
                DurationSeconds = 30,
                Comment = "Deadline"
            });
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.AddRange(
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.RevealObjective,
                    Sequence = 1,
                    TargetObjectiveId = 11,
                    Comment = "Reveal 11"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 2,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.ActivateObjective,
                    Sequence = 2,
                    TargetObjectiveId = 11,
                    Comment = "Activate 11"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 3,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.StartDeadline,
                    Sequence = 3,
                    DelayMilliseconds = 30000,
                    Comment = "Start deadline"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 4,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.CompleteObjective,
                    Sequence = 4,
                    TargetObjectiveId = 10,
                    Comment = "Complete objective 10"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 5,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.FailObjective,
                    Sequence = 5,
                    TargetObjectiveId = 11,
                    Comment = "Fail objective 11"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 6,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.CancelDeadline,
                    Sequence = 6,
                    Comment = "Cancel deadline"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 7,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.EmitScenarioEvent,
                    Sequence = 7,
                    ScenarioEventId = 1,
                    Comment = "Emit scenario event"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 8,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.TransferPlayer,
                    Sequence = 8,
                    MapContextId = 1221,
                    PosX = 4,
                    PosY = 0,
                    PosZ = 8,
                    Orientation = 1.5,
                    Comment = "Transfer player"
                });
            return fixture;
        }

        private static MissionContentFixture CreateResetFixture()
        {
            var fixture = MissionContentFixture.CreateValid();
            fixture.Scenarios.Add(new MissionScenarioEntry
            {
                MissionId = 321,
                ContentRevision = "deployment_11",
                ScenarioId = 61,
                Requirement = MissionContentRequirement.Required,
                Name = "Repeatable",
                Comment = "Repeatable scenario"
            });
            fixture.ScenarioSteps.Clear();
            fixture.ScenarioSteps.AddRange(
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 60,
                    StepId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.ResetAttempt,
                    Sequence = 1,
                    TargetScenarioId = 61,
                    Comment = "Reset scenario 61"
                },
                new MissionScenarioStepEntry
                {
                    MissionId = 321,
                    ContentRevision = "deployment_11",
                    ScenarioId = 61,
                    StepId = 1,
                    Requirement = MissionContentRequirement.Required,
                    Kind = MissionScenarioStepKind.EmitScenarioEvent,
                    Sequence = 1,
                    ScenarioEventId = 1,
                    Comment = "Repeatable step"
                });
            return fixture;
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

        private sealed class ManagerInstances : IDisposable
        {
            private readonly FieldInfo _mapsField = typeof(MapChannelManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly FieldInfo _objectsField = typeof(DynamicObjectManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly FieldInfo _creaturesField = typeof(CreatureManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly FieldInfo _missionsField = typeof(MissionManager)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly object _previousMaps;
            private readonly object _previousObjects;
            private readonly object _previousCreatures;
            private readonly object _previousMissions;

            internal ManagerInstances(
                MapChannelManager maps,
                DynamicObjectManager objects,
                CreatureManager creatures,
                MissionManager missions)
            {
                _previousMaps = _mapsField.GetValue(null);
                _previousObjects = _objectsField.GetValue(null);
                _previousCreatures = _creaturesField.GetValue(null);
                _previousMissions = _missionsField.GetValue(null);
                _mapsField.SetValue(null, maps);
                _objectsField.SetValue(null, objects);
                _creaturesField.SetValue(null, creatures);
                _missionsField.SetValue(null, missions);
            }

            public void Dispose()
            {
                _mapsField.SetValue(null, _previousMaps);
                _objectsField.SetValue(null, _previousObjects);
                _creaturesField.SetValue(null, _previousCreatures);
                _missionsField.SetValue(null, _previousMissions);
            }
        }
    }
}
