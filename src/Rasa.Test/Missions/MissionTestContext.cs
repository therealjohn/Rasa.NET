using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Rasa.Test.Missions
{
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context.Char;
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Game.Handlers;
    using Rasa.Managers;
    using Rasa.Packets;
    using Rasa.Packets.Protocol;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.Character;
    using Rasa.Repositories.Char.CharacterInventory;
    using Rasa.Repositories.Char.CharacterMission;
    using Rasa.Repositories.Char.Items;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Services.DbContext;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;
    using Rasa.Test.World;

    internal sealed class MissionTestContext : IGameUnitOfWorkFactory, IDisposable
    {
        private readonly string _directory = Path.Combine(
            AppContext.BaseDirectory, "TestDatabases", Guid.NewGuid().ToString("N"));
        private readonly WorldTestContext _world;
        private readonly List<Creature> _npcs = new();
        private readonly HashSet<ulong> _originalItems = EntityManager.Instance.Items.Keys.ToHashSet();
        private readonly List<uint> _addedTemplates = new();
        private string Database => Path.Combine(_directory, "missions");

        internal int SaveAttempts { get; private set; }
        internal Action<SqliteCharContext> BeforeSave { get; set; }
        internal Action<SqliteCharContext> AfterSave { get; set; }
        internal Action BeforeQuery { get; set; }
        internal Client Client { get; }
        internal MapChannel Map => _world.Map;
        internal MissionManager Manager { get; }
        internal Creature Receiver { get; private set; }
        internal MissionRewardDefinition Reward { get; private set; }

        internal MissionTestContext()
        {
            Directory.CreateDirectory(_directory);
            using var context = Open();
            context.Database.Migrate();
        }

        private MissionTestContext(
            IReadOnlyDictionary<uint, Mission> definitions,
            IReadOnlyDictionary<uint, MissionRewardDefinition> rewards = null) : this()
        {
            SeedCharacter(1, 0, 1);
            _world = new WorldTestContext();
            Client = _world.CreateClient(factory: this);
            Client.Player.Id = 1;
            Client.Player.Level = 1;
            Client.Player.Experience = 0;
            Client.Player.Credits[CurencyType.Credits] = 100;
            Client.Player.Credits[CurencyType.Prestige] = 50;
            Client.Player.Inventory.PersonalInventory = Enumerable.Repeat(0UL, 250).ToList();
            typeof(Client).GetProperty(nameof(Client.AccountEntry))!.SetValue(Client,
                new GameAccountEntry { Id = 1, SelectedSlot = 0 });
            CellManager.Instance.AddToWorld(Client);
            Drain();
            Manager = new MissionManager(this, definitions, rewards ?? new Dictionary<uint, MissionRewardDefinition>(),
                new ManifestationManager(this));
        }

        internal static MissionTestContext WithDefinitions(params uint[] missionIds) =>
            new(CreateDefinitions(missionIds));

        private static IReadOnlyDictionary<uint, Mission> CreateDefinitions(params uint[] missionIds) =>
            missionIds.ToDictionary(
                id => id,
                id => new Mission(new NpcMissionEntry
                {
                    Id = id,
                    GiverId = 77,
                    ReciverId = 88,
                    Level = 5,
                    GroupType = 1,
                    CategoryId = 2,
                    Shareable = true,
                    RadioCompleteable = false,
                    Comment = "fixture"
                }));

        internal static MissionTestContext WithCompletableMission(uint missionId)
        {
            var reward = new MissionRewardDefinition(
                experience: 100,
                currencies: new Dictionary<CurencyType, int>
                {
                    [CurencyType.Credits] = 7,
                    [CurencyType.Prestige] = 3
                },
                fixedItems: new[] { new MissionRewardItem(28, 3) },
                selectableItems: new[]
                {
                    new MissionRewardItem(29, 2),
                    new MissionRewardItem(29, 4)
                });
            var context = new MissionTestContext(
                CreateDefinitions(missionId),
                new Dictionary<uint, MissionRewardDefinition> { [missionId] = reward });
            context.Reward = reward;
            context.AddRewardTemplate(28, 3147);
            context.AddRewardTemplate(29, 3147);
            context.SeedMission(context.Client.Player.Id, missionId, (uint)MissionState.Active, true);
            context.ReloadPlayerMissions();
            context.Receiver = context.AddNpc(88);
            context.Drain();
            return context;
        }

        internal void SeedCharacter(uint accountId, byte slot, uint characterId)
        {
            using var context = Open();
            var account = context.GameAccountEntries.Find(accountId);
            if (account == null)
            {
                account = new GameAccountEntry
                {
                    Id = accountId,
                    Email = $"account-{accountId}@example.invalid",
                    Name = $"Account {accountId}",
                    FamilyName = $"Family {accountId}"
                };
                context.GameAccountEntries.Add(account);
            }

            context.CharacterEntries.Add(new CharacterEntry
            {
                Id = characterId,
                AccountId = accountId,
                GameAccount = account,
                Slot = slot,
                Name = $"Character {characterId}",
                Scale = 1,
                Level = 1,
                Credit = 100,
                Prestige = 50
            });
            context.SaveChanges();
        }

        internal void SeedMission(uint characterId, uint missionId, uint state, bool completeable)
        {
            using var context = Open();
            context.CharacterMissionEntries.Add(new CharacterMissionEntry(characterId, missionId, state)
            {
                Completeable = completeable
            });
            context.SaveChanges();
        }

        internal void ReloadPlayerMissions()
        {
            using var unit = CreateChar();
            Manager.Hydrate(Client.Player, unit.CharacterMissions.Get(Client.Player.Id));
        }

        internal CharacterMissionEntry ReadMission(uint missionId)
        {
            using var context = Open();
            return context.CharacterMissionEntries.AsNoTracking().Single(entry =>
                entry.CharacterId == Client.Player.Id && entry.MissionId == missionId);
        }

        internal RewardTotals ReadRewardTotals()
        {
            using var context = Open();
            var character = context.CharacterEntries.AsNoTracking().Single(entry => entry.Id == Client.Player.Id);
            var itemCount = (from inventory in context.CharacterInventoryEntries.AsNoTracking()
                join item in context.ItemEntries.AsNoTracking() on inventory.ItemId equals item.ItemId
                where inventory.CharacterId == Client.Player.Id &&
                    inventory.InventoryType == (uint)InventoryType.Personal
                select item.StackSize).Sum(value => (long)value);
            return new RewardTotals(character.Experience, character.Credit, character.Prestige, itemCount);
        }

        internal void FillRewardCategory()
        {
            var template = EntityClassManager.Instance.LoadedEntityClasses[(EntityClasses)3147].ItemTemplates[28];
            for (uint slot = 50; slot < 100; slot++)
            {
                var item = ItemManager.StageItem(template, 50000, "");
                item.OwnerId = Client.Player.Id;
                item.OwnerSlotId = slot;
                using (var context = Open())
                {
                    item.Id = new ItemRepository(context).CreateItem(item);
                    new CharacterInventoryRepository(context).AddInvItem(
                        Client.AccountEntry.Id, Client.Player.Id, (uint)InventoryType.Personal, slot, item.Id);
                }
                EntityManager.Instance.RegisterEntity(item.EntityId, EntityType.Item);
                EntityManager.Instance.RegisterItem(item.EntityId, item);
                Client.Player.Inventory.PersonalInventory[(int)slot] = item.EntityId;
            }
        }

        private void AddRewardTemplate(uint templateId, uint classId)
        {
            var entityClass = (EntityClasses)classId;
            _world.AddClass(entityClass);
            var classInfo = EntityClassManager.Instance.LoadedEntityClasses[entityClass];
            classInfo.ItemClassInfo ??= new ItemClassInfo(new ItemClassEntry { StackSize = 50000 });
            var template = new ItemTemplate(new ItemTemplateItemClassEntry
            {
                ItemTemplateId = templateId,
                ItemClass = classId
            }) { InventoryCategory = (InventoryCategory)2 };
            ItemManager.Instance.ItemTemplateItemClass[templateId] = entityClass;
            classInfo.ItemTemplates[templateId] = template;
            _addedTemplates.Add(templateId);
        }

        internal Creature AddNpc(uint dbId, MapChannel map = null)
        {
            map ??= Map;
            var npc = new Creature
            {
                DbId = dbId,
                Npc = new Npc(),
                MapContextId = map.MapInfo.MapContextId,
                Position = Vector3.Zero,
                EntityClass = EntityClasses.HumanBaseMale
            };
            _npcs.Add(npc);
            EntityManager.Instance.RegisterEntity(npc.EntityId, EntityType.Creature);
            EntityManager.Instance.RegisterCreature(npc);
            var seed = CellManager.Instance.GetCellSeed(npc.Position);
            npc.Cells = CellManager.Instance.CreateCellMatrix(map, seed & 0xFFFF, seed >> 16);
            CellManager.Instance.GetCell(map, seed & 0xFFFF, seed >> 16).CreatureList.Add(npc);
            return npc;
        }

        internal List<PythonPacket> Drain() => WorldTestContext.Drain(Client)
            .Select(packet => packet.Message).OfType<CallMethodMessage>()
            .Select(packet => packet.Packet).ToList();

        internal static byte[] Encode(PythonPacket packet)
        {
            using var stream = new MemoryStream();
            using var binary = new BinaryWriter(stream);
            using var writer = new Rasa.Memory.PythonWriter(binary);
            packet.Write(writer);
            return stream.ToArray();
        }

        public ICharUnitOfWork CreateChar()
        {
            var context = OpenWithHooks();
            context.SavingChanges += (_, _) =>
            {
                SaveAttempts++;
                BeforeSave?.Invoke(context);
            };
            context.SavedChanges += (_, _) => AfterSave?.Invoke(context);
            return new CharUnitOfWork(context,
                gameAccounts: null, censoredWords: null, characters: new CharacterRepository(context),
                characterAbilityDrawers: null, characterAppearances: null,
                characterInventories: new CharacterInventoryRepository(context),
                characterLockboxes: null, characterLogoses: null,
                characterMissions: new CharacterMissionRepository(context), characterOptions: null,
                characterSkills: null, characterTeleporters: null, characterTitles: null,
                clans: null, clanInventories: null, clanMembers: null, friends: null,
                ignoreds: null, items: new ItemRepository(context), userOptions: null);
        }

        public IWorldUnitOfWork CreateWorld() =>
            throw new InvalidOperationException("Unexpected world database access.");

        private SqliteCharContext Open() =>
            new(
                Options.Create(new DatabaseConfiguration
                {
                    Provider = "Sqlite",
                    Char = new DatabaseConnectionConfiguration { Database = Database }
                }),
                new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory()),
                new SqliteDbContextPropertyModifier());

        private SqliteCharContext OpenWithHooks() =>
            new(
                Options.Create(new DatabaseConfiguration
                {
                    Provider = "Sqlite",
                    Char = new DatabaseConnectionConfiguration { Database = Database }
                }),
                new QueryConfiguration(this),
                new SqliteDbContextPropertyModifier());

        private sealed class QueryConfiguration(MissionTestContext owner) : IDbContextConfigurationService
        {
            public void Configure(DbContextOptionsBuilder builder, DatabaseConnectionConfiguration configuration)
            {
                new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory())
                    .Configure(builder, configuration);
                builder.AddInterceptors(new QueryInterceptor(owner));
            }
        }

        private sealed class QueryInterceptor(MissionTestContext owner) : DbCommandInterceptor
        {
            public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                InterceptionResult<System.Data.Common.DbDataReader> result)
            {
                owner.BeforeQuery?.Invoke();
                return result;
            }
        }

        public void Dispose()
        {
            foreach (var npc in _npcs)
            {
                EntityManager.Instance.UnregisterEntity(npc.EntityId);
                EntityManager.Instance.UnregisterCreature(npc.EntityId);
                EntityManager.Instance.FreeEntity(npc.EntityId);
            }
            foreach (var id in EntityManager.Instance.Items.Keys.Except(_originalItems).ToArray())
                EntityManager.Instance.ReleaseEntity(id, EntityType.Item);
            foreach (var templateId in _addedTemplates)
            {
                if (ItemManager.Instance.ItemTemplateItemClass.TryGetValue(templateId, out var classId) &&
                    EntityClassManager.Instance.LoadedEntityClasses.TryGetValue(classId, out var entityClass))
                    entityClass.ItemTemplates.Remove(templateId);
                ItemManager.Instance.ItemTemplateItemClass.Remove(templateId);
            }
            _world?.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }

        internal readonly struct RewardTotals
        {
            internal uint Experience { get; }
            internal int Credits { get; }
            internal int Prestige { get; }
            internal long ItemCount { get; }

            internal RewardTotals(uint experience, int credits, int prestige, long itemCount)
            {
                Experience = experience;
                Credits = credits;
                Prestige = prestige;
                ItemCount = itemCount;
            }
        }
    }
}
