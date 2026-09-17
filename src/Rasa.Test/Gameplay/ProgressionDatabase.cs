using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Test.Gameplay
{
    using Rasa.Context.Char;
    using Rasa.Game;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.Character;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Structures.Char;
    using Rasa.Test.World;

    internal sealed class ProgressionDatabase : IGameUnitOfWorkFactory, IDisposable
    {
        private readonly string _directory = Path.Combine(
            AppContext.BaseDirectory, "TestDatabases", Guid.NewGuid().ToString("N"));
        private string Database => Path.Combine(_directory, "progression");

        internal int SaveAttempts { get; private set; }
        internal int OpenAttempts { get; private set; }
        internal Action<SqliteCharContext> BeforeSave { get; set; }

        internal ProgressionDatabase()
        {
            Directory.CreateDirectory(_directory);
            using var context = WaypointPersistenceTests.Open(Database);
            context.Database.Migrate();
        }

        internal void Seed(Client client)
        {
            var player = client.Player;
            using var context = WaypointPersistenceTests.Open(Database);
            var account = new GameAccountEntry
            {
                Id = player.Id,
                Name = "progression",
                Email = "progression@example.invalid",
                FamilyName = player.FamilyName
            };
            context.GameAccountEntries.Add(account);
            context.CharacterEntries.Add(new CharacterEntry
            {
                Id = player.Id,
                GameAccount = account,
                Name = player.Name,
                Level = player.Level,
                Experience = player.Experience,
                Race = (byte)player.Race,
                Body = player.SpentBody,
                Mind = player.SpentMind,
                Spirit = player.SpentSpirit,
                MapContextId = player.MapContextId
            });
            context.SaveChanges();
        }

        internal CharacterEntry Read(uint id)
        {
            using var context = WaypointPersistenceTests.Open(Database);
            return new CharacterRepository(context).Get(id);
        }

        public ICharUnitOfWork CreateChar()
        {
            OpenAttempts++;
            var context = WaypointPersistenceTests.Open(Database);
            context.SavingChanges += (_, _) =>
            {
                SaveAttempts++;
                BeforeSave?.Invoke(context);
            };
            return new CharUnitOfWork(context,
                gameAccounts: null, censoredWords: null, characters: new CharacterRepository(context),
                characterAbilityDrawers: null, characterAppearances: null, characterInventories: null,
                characterLockboxes: null, characterLogoses: null, characterMissions: null,
                characterOptions: null, characterSkills: null, characterTeleporters: null,
                characterTitles: null, clans: null, clanInventories: null, clanMembers: null,
                friends: null, ignoreds: null, items: null, userOptions: null);
        }

        public IWorldUnitOfWork CreateWorld() => throw new InvalidOperationException("Unexpected world database access.");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }
    }
}
