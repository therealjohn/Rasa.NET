using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Rasa.Test.Missions
{
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context.Char;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.Char.CharacterMission;
    using Rasa.Services.DbContext;
    using Rasa.Structures.Char;

    internal sealed class MissionTestContext : IDisposable
    {
        private readonly string _directory = Path.Combine(
            AppContext.BaseDirectory, "TestDatabases", Guid.NewGuid().ToString("N"));
        private string Database => Path.Combine(_directory, "missions");

        internal MissionTestContext()
        {
            Directory.CreateDirectory(_directory);
            using var context = Open();
            context.Database.Migrate();
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

        internal ICharUnitOfWork CreateChar()
        {
            var context = Open();
            return new CharUnitOfWork(context,
                gameAccounts: null, censoredWords: null, characters: null,
                characterAbilityDrawers: null, characterAppearances: null,
                characterInventories: null, characterLockboxes: null, characterLogoses: null,
                characterMissions: new CharacterMissionRepository(context), characterOptions: null,
                characterSkills: null, characterTeleporters: null, characterTitles: null,
                clans: null, clanInventories: null, clanMembers: null, friends: null,
                ignoreds: null, items: null, userOptions: null);
        }

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
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, true);
        }
    }
}
