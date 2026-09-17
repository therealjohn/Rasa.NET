using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    using Rasa.Repositories.Char.CharacterMission;
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
        private string Database => Path.Combine(_directory, "missions");

        internal Action<SqliteCharContext> BeforeSave { get; set; }
        internal Client Client { get; }
        internal MapChannel Map => _world.Map;
        internal MissionManager Manager { get; }

        internal MissionTestContext()
        {
            Directory.CreateDirectory(_directory);
            using var context = Open();
            context.Database.Migrate();
        }

        private MissionTestContext(IReadOnlyDictionary<uint, Mission> definitions) : this()
        {
            SeedCharacter(1, 0, 1);
            _world = new WorldTestContext();
            Client = _world.CreateClient(factory: this);
            Client.Player.Id = 1;
            typeof(Client).GetProperty(nameof(Client.AccountEntry))!.SetValue(Client,
                new GameAccountEntry { Id = 1, SelectedSlot = 0 });
            CellManager.Instance.AddToWorld(Client);
            Drain();
            Manager = new MissionManager(this, definitions);
        }

        internal static MissionTestContext WithDefinitions(params uint[] missionIds) =>
            new(missionIds.ToDictionary(
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
                })));

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
                Level = 1
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
            var context = Open();
            context.SavingChanges += (_, _) => BeforeSave?.Invoke(context);
            return new CharUnitOfWork(context,
                gameAccounts: null, censoredWords: null, characters: null,
                characterAbilityDrawers: null, characterAppearances: null, characterInventories: null,
                characterLockboxes: null, characterLogoses: null,
                characterMissions: new CharacterMissionRepository(context), characterOptions: null,
                characterSkills: null, characterTeleporters: null, characterTitles: null,
                clans: null, clanInventories: null, clanMembers: null, friends: null,
                ignoreds: null, items: null, userOptions: null);
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

        public void Dispose()
        {
            foreach (var npc in _npcs)
            {
                EntityManager.Instance.UnregisterEntity(npc.EntityId);
                EntityManager.Instance.UnregisterCreature(npc.EntityId);
                EntityManager.Instance.FreeEntity(npc.EntityId);
            }
            _world?.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }
    }
}
