extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using ClientState = RasaGame::Rasa.Data.ClientState;

namespace Rasa.Test.Missions
{
    using Configuration;
    using Configuration.ConnectionStrings;
    using Configuration.ContextSetup;
    using Context;
    using Context.World;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Services.DbContext;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;

    internal static class BootcampRuntimeTestHarness
    {
        internal const uint BootcampMapContextId = 1985;
        internal const uint WildernessMapContextId = 1220;
        internal const uint MissionInitiation = 1990;
        internal const uint MissionGearingUp = 1992;

        internal const uint MajorMcAllisterCreatureId = 510203;
        internal const uint CaptainDelessioCreatureId = 510204;
        internal const uint CorporalHartmannCreatureId = 510205;
        internal const uint CorporalDeSimoneCreatureId = 510206;
        internal const uint PracticeDummyCreatureId = 510211;
        internal const uint LightningDummyCreatureId = 510212;

        internal const uint CaptainDelessioPackageId = 2560;
        internal const uint CorporalHartmannPackageId = 2563;
        internal const uint CorporalDeSimonePackageId = 2562;

        internal static Harness Create()
        {
            var databaseDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "TestDatabases",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(databaseDirectory);
            var worldDatabase = Path.Combine(databaseDirectory, "world");
            var worldContext = (SqliteWorldContext)CreateContext(typeof(SqliteWorldContext), worldDatabase);
            worldContext.Database.Migrate();

            var context = MissionTestContext.WithCustomDefinitions(new Dictionary<uint, Mission>());
            context.Map.MapInfo = new MapInfo(BootcampMapContextId, "bootcamp_runtime", 1556, 0);
            context.Client.Player.MapContextId = BootcampMapContextId;
            context.Client.Player.AppearanceData = new Dictionary<EquipmentData, AppearanceData>();
            context.Client.Player.Attributes[Attributes.Body] =
                new ActorAttributes(Attributes.Body, 10, 10, 10, 0, 0);
            context.Client.Player.Attributes[Attributes.Mind] =
                new ActorAttributes(Attributes.Mind, 10, 10, 10, 0, 0);
            context.Client.Player.Attributes[Attributes.Spirit] =
                new ActorAttributes(Attributes.Spirit, 10, 10, 10, 0, 0);
            context.Client.Player.Attributes[Attributes.Health] =
                new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
            context.Client.Player.Attributes[Attributes.Chi] =
                new ActorAttributes(Attributes.Chi, 100, 100, 100, 0, 0);
            context.Client.Player.Attributes[Attributes.Power] =
                new ActorAttributes(Attributes.Power, 100, 100, 100, 0, 0);
            context.Client.Player.Attributes[Attributes.Regen] =
                new ActorAttributes(Attributes.Regen, 0, 0, 0, 0, 0);
            context.Client.Player.Attributes[Attributes.Armor] =
                new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0);

            PrepareBootcampScenarioClasses();
            PrepareBootcampRewardTemplates(context);

            var factory = new RuntimeLoadingFactory(context, worldContext);
            MissionManager manager = null;
            var now = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
            var creatures = new CreatureManager(factory, new ManifestationManager(context));
            foreach (var creatureId in new[]
                     {
                         39U,
                         50U,
                         PracticeDummyCreatureId,
                         LightningDummyCreatureId
                     })
            {
                creatures.LoadedCreatures[creatureId] = new Creature
                {
                    DbId = creatureId,
                    EntityClass = (EntityClasses)4001,
                    Npc = new Npc { NpcPackageId = creatureId },
                    AppearanceData = new Dictionary<EquipmentData, AppearanceData>()
                };
            }

            MapChannelManager maps = null;
            var objects = new DynamicObjectManager(null, maps);
            var manifestation = new ManifestationManager(context);
            var deadlineService = new MissionDeadlineService(
                () => factory,
                () => manager,
                () => now);
            var scenarioService = new MissionScenarioService(
                () => factory,
                () => manager,
                manifestation,
                () => maps,
                () => creatures,
                () => objects,
                () => CommunicatorManager.Instance,
                () => now);
            maps = new MapChannelManager(
                factory,
                privateInstances: new PrivateMapInstanceService(),
                scenarioService: scenarioService);
            maps.MapChannelArray.Add(BootcampMapContextId, context.Map);
            maps.MapChannelArray.Add(WildernessMapContextId, new MapChannel
            {
                MapInfo = new MapInfo(WildernessMapContextId, "alia_das_fixture", 1556, 0),
                ClientList = new List<Client>(),
                PlayerLimit = 128
            });
            objects = new DynamicObjectManager(null, maps);
            manager = new MissionManager(
                factory,
                new Dictionary<uint, Mission>(),
                new Dictionary<uint, MissionRewardDefinition>(),
                manifestation,
                deadlineService: deadlineService,
                scenarioService: scenarioService);
            var report = manager.LoadMissions();
            if (report.BlocksReadiness)
                throw new InvalidOperationException(
                    string.Join(" | ", report.Diagnostics.Select(diagnostic => diagnostic.Code)));
            var singletons = new ManagerInstances(maps, objects, creatures, manager);
            var bootcampMap = maps.GetOrCreatePrivateInstance(BootcampMapContextId, context.Client.Player.Id);
            AttachClientToMap(context.Client, bootcampMap);

            return new Harness(
                context,
                worldContext,
                manager,
                maps,
                bootcampMap,
                singletons,
                () => now,
                value => now = value);
        }

        internal static void AssertObjectiveStates(
            MissionLog mission,
            params (uint ObjectiveId, MissionObjectiveState State)[] expected)
        {
            var actual = expected
                .Select(entry => (entry.ObjectiveId, mission.Objectives[entry.ObjectiveId].State))
                .ToArray();
            Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert.AreEqual(expected, actual);
        }

        internal static Creature FindNpcByPackage(MapChannel map, uint npcPackageId) =>
            map.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList)
                .SingleOrDefault(creature => creature.Npc?.NpcPackageId == npcPackageId);

        internal static Creature FindCreature(MapChannel map, uint creatureDbId) =>
            map.MapCellInfo.Cells.Values
                .SelectMany(cell => cell.CreatureList)
                .SingleOrDefault(creature => creature.DbId == creatureDbId);

        internal static DynamicObject FindScenarioObject(MapChannel map, string key) =>
            map.DynamicObjects.SingleOrDefault(dynamicObject =>
                string.Equals(dynamicObject.ScenarioKey, key, StringComparison.Ordinal) ||
                dynamicObject.ScenarioKey?.EndsWith($":object:{key}", StringComparison.Ordinal) == true);

        internal static void AdvanceScenarioCorpseAndRespawn(
            Harness harness,
            Creature creature,
            long corpseMilliseconds,
            long respawnMilliseconds)
        {
            if (LootDispenserManager.Instance.AdvanceCorpseLifetime(
                    harness.BootcampMap,
                    creature,
                    corpseMilliseconds))
            {
                CellManager.Instance.RemoveCreatureFromWorld(harness.BootcampMap, creature);
            }

            SpawnPoolManager.Instance.SpawnPoolWorker(harness.BootcampMap, respawnMilliseconds);
        }

        internal static void PrepareDirectDamageClient(Client client)
        {
            client.Player.Attributes[Attributes.Health] =
                new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
            client.Player.Attributes[Attributes.Armor] =
                new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0);
            client.Player.Attributes[Attributes.Power] =
                new ActorAttributes(Attributes.Power, 100, 100, 100, 0, 0);
        }

        internal static ActionLevelInfo LightningInfo(int primaryDamage)
        {
            var info = new ActionLevelInfo
            {
                ActionId = ActionId.AaRecruitLightning,
                Level = 1,
                MaxRange = 20
            };
            info.Properties[AbilityProperty.DamageAmountMin] = primaryDamage;
            info.Properties[AbilityProperty.DamageAmountMax] = primaryDamage;
            info.Properties[AbilityProperty.RadiusAroundTarget] = 1;
            return info;
        }

        internal static ActionInfo LightningAction(ActionLevelInfo info)
        {
            var action = new ActionInfo
            {
                ActionId = ActionId.AaRecruitLightning,
                Module = "abilities.lightning"
            };
            action.Levels[info.Level] = info;
            return action;
        }

        internal static void InvokeResolveDirectDamage(
            AbilityManager manager,
            MapChannel map,
            Client client,
            ActionInfo actionInfo,
            ActionLevelInfo info,
            ActionData action)
        {
            var method = typeof(AbilityManager).GetMethod(
                "ResolveDirectDamage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var arguments = new List<object>
            {
                map,
                client,
                client.Player,
                actionInfo,
                info,
                action
            };
            if (method!.GetParameters().Length == 7)
                arguments.Add(null);
            method.Invoke(manager, arguments.ToArray());
        }

        private static RasaDbContextBase CreateContext(Type contextType, string database)
        {
            var connection = new DatabaseConnectionConfiguration { Database = database };
            var options = Options.Create(new DatabaseConfiguration
            {
                Provider = "Sqlite",
                Auth = connection,
                Char = connection,
                World = connection
            });
            return (RasaDbContextBase)Activator.CreateInstance(
                contextType,
                options,
                new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory()),
                new SqliteDbContextPropertyModifier())!;
        }

        private static void PrepareBootcampScenarioClasses()
        {
            var classes = EntityClassManager.Instance.LoadedEntityClasses;
            foreach (var entityClassId in new uint[] { 24911, 24990, 7862 })
                if (!classes.ContainsKey((EntityClasses)entityClassId))
                    classes.Add((EntityClasses)entityClassId, new EntityClass(
                        entityClassId,
                        $"scenario_object_{entityClassId}",
                        0,
                        0,
                        new List<AugmentationType>(),
                        true));

            if (!classes.TryGetValue((EntityClasses)4001, out var creatureClass))
            {
                creatureClass = new EntityClass(4001, "scenario_creature", 0, 0, new List<AugmentationType>(), true);
                classes.Add((EntityClasses)4001, creatureClass);
            }

            if (!creatureClass.Augmentations.Contains(AugmentationType.Creature))
                creatureClass.Augmentations.Add(AugmentationType.Creature);
        }

        private static void PrepareBootcampRewardTemplates(MissionTestContext context)
        {
            ConfigureRewardTemplate(context, 13066, 15542, (EquipmentData)2);
            ConfigureRewardTemplate(context, 13096, 15572, (EquipmentData)3);
            ConfigureRewardTemplate(context, 13156, 15632, (EquipmentData)16);
            ConfigureRewardTemplate(context, 13186, 15662, (EquipmentData)15);
            ConfigureRewardTemplate(context, 13713, 27220, (EquipmentData)13);
        }

        private static void ConfigureRewardTemplate(
            MissionTestContext context,
            uint templateId,
            uint classId,
            EquipmentData equipmentSlot)
        {
            context.AddRewardTemplate(templateId, classId);
            EntityClassManager.Instance.LoadedEntityClasses[(EntityClasses)classId]
                .EquipableClassInfo = new EquipableClassInfo(equipmentSlot);
        }

        private static void AttachClientToMap(Client client, MapChannel destination)
        {
            client.Player.MapChannel = destination;
            client.Player.RuntimeMapChannel = destination;
            client.Player.MapContextId = destination.MapInfo.MapContextId;
            destination.ClientList.Add(client);
            CellManager.Instance.AddToWorld(client);
            client.State = RasaGame::Rasa.Data.ClientState.Ingame;
        }

        internal sealed class Harness : IDisposable
        {
            private readonly Func<DateTime> _getUtcNow;
            private readonly Action<DateTime> _setUtcNow;
            private ManagerInstances _singletons;
            private readonly List<(uint CreatureId, uint? PackageId)> _npcs = new();

            internal Harness(
                MissionTestContext context,
                SqliteWorldContext worldContext,
                MissionManager manager,
                MapChannelManager maps,
                MapChannel bootcampMap,
                IDisposable singletons,
                Func<DateTime> getUtcNow,
                Action<DateTime> setUtcNow)
            {
                Context = context;
                WorldContext = worldContext;
                Manager = manager;
                Maps = maps;
                BootcampMap = bootcampMap;
                Client = context.Client;
                _singletons = (ManagerInstances)singletons;
                _getUtcNow = getUtcNow;
                _setUtcNow = setUtcNow;
            }

            internal MissionTestContext Context { get; }
            internal SqliteWorldContext WorldContext { get; }
            internal MissionManager Manager { get; private set; }
            internal MapChannelManager Maps { get; private set; }
            internal Client Client { get; private set; }
            internal MapChannel BootcampMap { get; private set; }

            internal DateTime UtcNow
            {
                get => _getUtcNow();
                set => _setUtcNow(value);
            }

            internal void SeedMission(uint characterId, uint missionId, uint state, bool completeable)
            {
                using (var unit = Context.CreateChar())
                {
                    unit.ExecuteTransaction(() =>
                    {
                        unit.CharacterMissions.Add(new CharacterMissionEntry(characterId, missionId, state)
                        {
                            Completeable = completeable
                        });
                        if (Manager.LoadedMissions.TryGetValue(missionId, out var definition) &&
                            definition.IsOperational)
                        {
                            unit.CharacterMissionProgress.AddObjectives(
                                definition.Objectives.Values.Select(objective =>
                                {
                                    var row = new CharacterMissionObjectiveEntry(
                                        characterId,
                                        missionId,
                                        objective.ObjectiveId,
                                        (byte)(completeable && objective.IsRequired.Value
                                            ? MissionObjectiveState.Completed
                                            : objective.InitialState.Value));
                                    foreach (var counter in objective.Counters)
                                        row.Counters.Add(new CharacterMissionObjectiveCounterEntry(
                                            characterId,
                                            missionId,
                                            objective.ObjectiveId,
                                            counter.Key,
                                            counter.Value.InitialValue));
                                    foreach (var counter in objective.ItemCounters)
                                        row.ItemCounters.Add(new CharacterMissionObjectiveItemCounterEntry(
                                            characterId,
                                            missionId,
                                            objective.ObjectiveId,
                                            counter.Key,
                                            counter.Value.InitialValue));
                                    return row;
                                }));
                        }
                    });
                }

                using var reload = Context.CreateChar();
                Manager.Hydrate(
                    Client.Player,
                    reload.CharacterMissions.Get(Client.Player.Id),
                    reload.CharacterMissionProgress.Get(Client.Player.Id));
            }

            internal Creature AddNpc(uint dbId, uint? npcPackageId = null)
            {
                _npcs.Add((dbId, npcPackageId));
                var npc = Context.AddNpc(dbId, BootcampMap, npcPackageId);
                npc.AppearanceData ??= new Dictionary<EquipmentData, AppearanceData>();
                if (npc.Attributes.Count == 0)
                {
                    npc.State = CharacterState.Normal;
                    npc.Attributes[Attributes.Body] = new ActorAttributes(Attributes.Body, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Mind] = new ActorAttributes(Attributes.Mind, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Spirit] = new ActorAttributes(Attributes.Spirit, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Health] = new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
                    npc.Attributes[Attributes.Chi] = new ActorAttributes(Attributes.Chi, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Power] = new ActorAttributes(Attributes.Power, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Aware] = new ActorAttributes(Attributes.Aware, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Armor] = new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Speed] = new ActorAttributes(Attributes.Speed, 1, 1, 1, 0, 0);
                    npc.Attributes[Attributes.Regen] = new ActorAttributes(Attributes.Regen, 0, 0, 0, 0, 0);
                }
                return npc;
            }

            internal void Reconnect()
            {
                var characterId = Client.Player.Id;
                Maps.ReleaseOwnedPrivateInstances(characterId);
                var rebuilt = Maps.GetOrCreatePrivateInstance(BootcampMapContextId, characterId);
                BootcampMap = rebuilt;
                foreach (var npc in _npcs)
                    AddNpcToCurrentMap(npc.CreatureId, npc.PackageId);
                AttachClientToMap(Client, BootcampMap);
            }

            internal void ReconnectFresh()
            {
                var characterId = Client.Player.Id;
                var accountEntry = CloneAccountEntry(Client.AccountEntry);
                DetachClientFromCurrentMap(Client);
                Maps.ReleaseOwnedPrivateInstances(characterId);

                var factory = new RuntimeLoadingFactory(Context, WorldContext);
                var manifestation = new ManifestationManager(Context);
                MissionManager manager = null;
                var creatures = new CreatureManager(factory, manifestation);
                foreach (var creatureId in new[]
                         {
                             39U,
                             50U,
                             PracticeDummyCreatureId,
                             LightningDummyCreatureId
                         })
                {
                    creatures.LoadedCreatures[creatureId] = new Creature
                    {
                        DbId = creatureId,
                        EntityClass = (EntityClasses)4001,
                        Npc = new Npc { NpcPackageId = creatureId },
                        AppearanceData = new Dictionary<EquipmentData, AppearanceData>()
                    };
                }

                MapChannelManager maps = null;
                var objects = new DynamicObjectManager(null, maps);
                var deadlineService = new MissionDeadlineService(
                    () => factory,
                    () => manager,
                    _getUtcNow);
                var scenarioService = new MissionScenarioService(
                    () => factory,
                    () => manager,
                    manifestation,
                    () => maps,
                    () => creatures,
                    () => objects,
                    () => CommunicatorManager.Instance,
                    _getUtcNow);
                maps = new MapChannelManager(
                    factory,
                    privateInstances: new PrivateMapInstanceService(),
                    scenarioService: scenarioService);
                maps.MapChannelArray.Add(BootcampMapContextId, Context.Map);
                maps.MapChannelArray.Add(WildernessMapContextId, new MapChannel
                {
                    MapInfo = new MapInfo(WildernessMapContextId, "alia_das_fixture", 1556, 0),
                    ClientList = new List<Client>(),
                    PlayerLimit = 128
                });
                objects = new DynamicObjectManager(null, maps);
                manager = new MissionManager(
                    factory,
                    new Dictionary<uint, Mission>(),
                    new Dictionary<uint, MissionRewardDefinition>(),
                    manifestation,
                    deadlineService: deadlineService,
                    scenarioService: scenarioService);
                var report = manager.LoadMissions();
                if (report.BlocksReadiness)
                    throw new InvalidOperationException(
                        string.Join(" | ", report.Diagnostics.Select(diagnostic => diagnostic.Code)));

                _singletons.Dispose();
                _singletons = new ManagerInstances(maps, objects, creatures, manager);
                Manager = manager;
                Maps = maps;
                BootcampMap = Maps.GetOrCreatePrivateInstance(BootcampMapContextId, characterId);
                foreach (var npc in _npcs)
                    AddNpcToCurrentMap(npc.CreatureId, npc.PackageId);

                var freshClient = Context.CreateCompetingClient(Manager);
                typeof(Client).GetProperty(nameof(Client.AccountEntry))!
                    .SetValue(freshClient, accountEntry);
                freshClient.Player.AppearanceData ??=
                    new Dictionary<EquipmentData, AppearanceData>();
                new InventoryManager(Context, Manager).InitCharacterInventory(freshClient);
                freshClient.Player.Skills = Maps.GetPlayerSkills(characterId);
                freshClient.Player.Abilities = Maps.GetPlayerAbilities(characterId);
                MoveClientToMap(freshClient, freshClient.Player.MapChannel, BootcampMap);
                freshClient.State = RasaGame::Rasa.Data.ClientState.Ingame;
                Client = freshClient;
                MissionTestContext.Drain(freshClient);
            }

            internal IReadOnlyDictionary<uint, int> ReadOwnedTemplateCounts(params uint[] templateIds)
            {
                var requested = new HashSet<uint>(templateIds);
                using var unit = Context.CreateChar();
                return unit.CharacterInventories
                    .GetItems(Client.AccountEntry.Id)
                    .Where(entry =>
                        entry.CharacterId == Client.Player.Id &&
                        ((InventoryType)entry.InventoryType == InventoryType.Personal ||
                         (InventoryType)entry.InventoryType == InventoryType.EquipedInventory ||
                         (InventoryType)entry.InventoryType == InventoryType.WeaponDrawerInventory))
                    .Select(entry => unit.Items.GetItem(entry.ItemId)?.ItemTemplateId)
                    .Where(templateId => templateId.HasValue && requested.Contains(templateId.Value))
                    .GroupBy(templateId => templateId!.Value)
                    .ToDictionary(group => group.Key, group => group.Count());
            }

            internal (int SkillCount, int TrayCount) ReadLightningGrantCounts()
            {
                using var unit = Context.CreateChar();
                return (
                    unit.CharacterSkills.GetCharacterSkills(Client.Player.Id)
                        .Count(entry =>
                            entry.SkillId == (uint)SkillId.Lightning &&
                            entry.AbilityId == (int)ActionId.AaRecruitLightning &&
                            entry.SkillLevel == 1),
                    unit.CharacterAbilityDrawers.GetCharacterAbilities(Client.Player.Id)
                        .Count(entry =>
                            entry.AbilityId == (int)ActionId.AaRecruitLightning &&
                            entry.AbilityLevel == 1));
            }

            private void AddNpcToCurrentMap(uint dbId, uint? npcPackageId)
            {
                var npc = Context.AddNpc(dbId, BootcampMap, npcPackageId);
                npc.AppearanceData ??= new Dictionary<EquipmentData, AppearanceData>();
                if (npc.Attributes.Count == 0)
                {
                    npc.State = CharacterState.Normal;
                    npc.Attributes[Attributes.Body] = new ActorAttributes(Attributes.Body, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Mind] = new ActorAttributes(Attributes.Mind, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Spirit] = new ActorAttributes(Attributes.Spirit, 10, 10, 10, 0, 0);
                    npc.Attributes[Attributes.Health] = new ActorAttributes(Attributes.Health, 100, 100, 100, 0, 0);
                    npc.Attributes[Attributes.Chi] = new ActorAttributes(Attributes.Chi, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Power] = new ActorAttributes(Attributes.Power, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Aware] = new ActorAttributes(Attributes.Aware, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Armor] = new ActorAttributes(Attributes.Armor, 0, 0, 0, 0, 0);
                    npc.Attributes[Attributes.Speed] = new ActorAttributes(Attributes.Speed, 1, 1, 1, 0, 0);
                    npc.Attributes[Attributes.Regen] = new ActorAttributes(Attributes.Regen, 0, 0, 0, 0, 0);
                }
            }

            private static GameAccountEntry CloneAccountEntry(GameAccountEntry source) =>
                new()
                {
                    Id = source.Id,
                    Email = source.Email,
                    Name = source.Name,
                    Level = source.Level,
                    FamilyName = source.FamilyName,
                    SelectedSlot = source.SelectedSlot,
                    CanSkipBootcamp = source.CanSkipBootcamp,
                    LastIp = source.LastIp,
                    LastLogin = source.LastLogin,
                    CreatedAt = source.CreatedAt,
                    Characters = source.Characters?.ToList() ?? new List<CharacterEntry>()
                };

            private static void DetachClientFromCurrentMap(Client client)
            {
                var map = client.Player.MapChannel;
                if (map == null)
                    return;

                CellManager.Instance.RemoveFromWorld(client);
                map.ClientList.Remove(client);
            }

            private static void MoveClientToMap(
                Client client,
                MapChannel origin,
                MapChannel destination)
            {
                if (origin != null)
                {
                    CellManager.Instance.RemoveFromWorld(client);
                    origin.ClientList.Remove(client);
                }

                client.Player.MapChannel = destination;
                client.Player.RuntimeMapChannel = destination;
                client.Player.MapContextId = destination.MapInfo.MapContextId;
                destination.ClientList.Add(client);
                CellManager.Instance.AddToWorld(client);
            }

            public void Dispose()
            {
                _singletons.Dispose();
                var directory = Path.GetDirectoryName(WorldContext.Database.GetDbConnection().DataSource);
                WorldContext.Dispose();
                SqliteConnection.ClearAllPools();
                Context.Dispose();
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private sealed class RuntimeLoadingFactory : IGameUnitOfWorkFactory
        {
            private readonly MissionTestContext _charFactory;
            private readonly SqliteWorldContext _worldContext;

            internal RuntimeLoadingFactory(
                MissionTestContext charFactory,
                SqliteWorldContext worldContext)
            {
                _charFactory = charFactory;
                _worldContext = worldContext;
            }

            public Repositories.Char.ICharUnitOfWork CreateChar() => _charFactory.CreateChar();

            public IWorldUnitOfWork CreateWorld() =>
                new RepositoryBackedWorldUnitOfWork(_worldContext);
        }

        private sealed class RepositoryBackedWorldUnitOfWork : IWorldUnitOfWork
        {
            internal RepositoryBackedWorldUnitOfWork(SqliteWorldContext context)
            {
                Actions = null;
                Equipment = new EquipmentRepository(context);
                Creatures = new CreatureRepository(context);
                EntityClasses = new EntityClassRepository(context);
                Footlockers = null;
                Logoses = null;
                MapInfos = new MapInfoRepository(context);
                MapLinks = null;
                Kraftwerks = null;
                MapRegions = null;
                MapMarkers = null;
                Recipes = null;
                NpcMissions = null;
                NpcMissionRewards = null;
                MissionContent = new MissionContentRepository(context);
                NpcPackages = new NpcPackageRepository(context);
                RandomNames = null;
                Spawnpools = null;
                Teleporters = new TeleporterRepository(context);
            }

            public IActionRepository Actions { get; }
            public IEquipmentRepository Equipment { get; }
            public ICreatureRepository Creatures { get; }
            public IEntityClassRepository EntityClasses { get; }
            public IFootlockerRepository Footlockers { get; }
            public ILogosRepository Logoses { get; }
            public IMapInfoRepository MapInfos { get; }
            public IMapLinkRepository MapLinks { get; }
            public IKraftwerksRepository Kraftwerks { get; }
            public IMapRegionRepository MapRegions { get; }
            public IMapMarkerRepository MapMarkers { get; }
            public IRecipeRepository Recipes { get; }
            public INpcMissionRepository NpcMissions { get; }
            public INpcMissionRewardRepository NpcMissionRewards { get; }
            public IMissionContentRepository MissionContent { get; }
            public INpcPackageRepository NpcPackages { get; }
            public IPlayerRandomNameRepository RandomNames { get; }
            public ISpawnpoolRepository Spawnpools { get; }
            public ITeleporterRepository Teleporters { get; }
            public void Complete() { }
            public void Reject() { }
            public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginTransaction() =>
                throw new NotSupportedException();
            public void Dispose() { }
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
