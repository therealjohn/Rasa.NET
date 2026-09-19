using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Database
{
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context.Char;
    using Rasa.Repositories.Char.Character;
    using Rasa.Repositories.Char.CharacterQualification;
    using Rasa.Repositories.Char.CharacterStartingExperience;
    using Rasa.Services.DbContext;
    using Rasa.Structures.Char;

    [TestClass]
    [DoNotParallelize]
    public class StartingExperiencePersistenceTests
    {
        [TestMethod]
        public void StartingExperienceAndQualificationsRoundTripAcrossReconnect()
        {
            WithDisposableSqlite((context, database) =>
            {
                context.Database.Migrate();
                SeedCharacter(context, 17, 123, 1, 1220);

                var experience = new CharacterStartingExperienceRepository(context);
                var qualifications = new CharacterQualificationRepository(context);

                experience.Add(new CharacterStartingExperienceEntry(
                    123,
                    "deployment_11",
                    CharacterStartingExperienceState.Pending));
                qualifications.Add(new CharacterQualificationEntry(
                    123,
                    CharacterQualificationKey.BootcampComplete));

                using var reopened = Open(database);
                var savedExperience = new CharacterStartingExperienceRepository(reopened).Get(123);
                Assert.IsNotNull(savedExperience);
                Assert.AreEqual("deployment_11", savedExperience.ContentRevision);
                Assert.AreEqual(CharacterStartingExperienceState.Pending, savedExperience.State);

                Assert.IsTrue(new CharacterQualificationRepository(reopened)
                    .HasQualification(123, CharacterQualificationKey.BootcampComplete));
            });
        }

        [TestMethod]
        public void MigrationMarksExistingCharactersLegacyWithoutMovingThem()
        {
            WithDisposableSqlite((context, database) =>
            {
                context.GetService<IMigrator>()
                    .Migrate("20260918001335_MissionObjectiveProgress");
                SeedCharacter(context, 17, 123, 1, 7777);

                context.Database.Migrate();

                using var reopened = Open(database);
                var character = new CharacterRepository(reopened).Get(123);
                var experience = new CharacterStartingExperienceRepository(reopened).Get(123);

                Assert.AreEqual(7777U, character.MapContextId);
                Assert.IsNotNull(experience);
                Assert.AreEqual("deployment_11", experience.ContentRevision);
                Assert.AreEqual(CharacterStartingExperienceState.Legacy, experience.State);
            });
        }

        [TestMethod]
        public void DeletingCharacterCleansStartingExperienceAndQualifications()
        {
            WithDisposableSqlite((context, database) =>
            {
                context.Database.Migrate();
                SeedCharacter(context, 17, 123, 1, 1220);
                new CharacterStartingExperienceRepository(context).Add(new CharacterStartingExperienceEntry(
                    123,
                    "deployment_11",
                    CharacterStartingExperienceState.Pending));
                new CharacterQualificationRepository(context).Add(new CharacterQualificationEntry(
                    123,
                    CharacterQualificationKey.BootcampComplete));

                using (var deleteContext = Open(database))
                {
                    var characters = new CharacterRepository(deleteContext);
                    characters.Delete(123);
                    deleteContext.SaveChanges();
                }

                using var reopened = Open(database);
                Assert.AreEqual(0, reopened.CharacterEntries.Count(entry => entry.Id == 123));
                Assert.AreEqual(0, reopened.Set<CharacterStartingExperienceEntry>().Count(entry => entry.CharacterId == 123));
                Assert.AreEqual(0, reopened.Set<CharacterQualificationEntry>().Count(entry => entry.CharacterId == 123));
            });
        }

        private static void WithDisposableSqlite(Action<SqliteCharContext, string> action)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "TestDatabases",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var database = Path.Combine(path, "database");
                using var context = Open(database);
                action(context, database);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(path, true);
            }
        }

        private static SqliteCharContext Open(string database) =>
            new(
                Options.Create(new DatabaseConfiguration
                {
                    Provider = "Sqlite",
                    Char = new DatabaseConnectionConfiguration { Database = database }
                }),
                new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory()),
                new SqliteDbContextPropertyModifier());

        private static void SeedCharacter(
            SqliteCharContext context,
            uint accountId,
            uint characterId,
            byte slot,
            uint mapContextId)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO account (id, email, name, family_name) VALUES " +
                $"({accountId}, 'task4-{accountId}@example.invalid', " +
                $"'Task4Account{accountId}', 'Task4Family{accountId}')");
            context.Database.ExecuteSqlRaw(
                "INSERT INTO character (id, account_id, slot, name, race, class, gender, scale, " +
                "experience, level, credit, prestige, body, mind, spirit, map_context_id, coord_x, " +
                "coord_y, coord_z, rotation) VALUES " +
                $"({characterId}, {accountId}, {slot}, 'Task4Character{characterId}', " +
                $"1, 1, 0, 1, 4000, 9, 100, 50, 0, 0, 0, {mapContextId}, 1, 2, 3, 0)");
        }
    }
}
