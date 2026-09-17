using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Database
{
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context;
    using Rasa.Context.Auth;
    using Rasa.Context.Char;
    using Rasa.Context.World;
    using Rasa.Services.DbContext;
    using Rasa.Services.Random;
    using Rasa.Repositories.Auth.Account;

    [TestClass]
    public class CompatibilityTests
    {
        [TestMethod]
        [DataRow(typeof(SqliteAuthContext))]
        [DataRow(typeof(SqliteCharContext))]
        [DataRow(typeof(SqliteWorldContext))]
        [DataRow(typeof(MySqlAuthContext))]
        [DataRow(typeof(MySqlCharContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void MigrationSnapshotMatchesCurrentModel(Type contextType)
        {
            using var context = CreateContext(contextType, "unused");

            Assert.IsFalse(context.Database.HasPendingModelChanges(), contextType.Name);
        }

        [TestMethod]
        [DataRow(typeof(MySqlAuthContext))]
        [DataRow(typeof(MySqlCharContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void MySqlSnapshotRefreshPreservesHistoricalIdentityColumns(Type contextType)
        {
            using var context = CreateContext(contextType, "unused");
            var assembly = context.GetService<IMigrationsAssembly>();
            var migrations = assembly.Migrations.Values
                .Select(type => assembly.CreateMigration(type, context.Database.ProviderName)).ToArray();
            var refreshIndex = Array.FindIndex(migrations, m => m.GetType().Name == "Net10IdentityMetadata");
            Assert.IsTrue(refreshIndex >= 0);
            var refresh = migrations[refreshIndex];
            Assert.AreEqual(0, refresh.UpOperations.Count);
            Assert.AreEqual(0, refresh.DownOperations.Count);

            var columns = new Dictionary<string, ColumnOperation>();
            foreach (var operation in migrations.Take(refreshIndex).SelectMany(m => m.UpOperations))
            {
                if (operation is CreateTableOperation table)
                    foreach (var column in table.Columns)
                        columns[table.Name + "." + column.Name] = column;
                if (operation is ColumnOperation changed)
                    columns[changed.Table + "." + changed.Name] = changed;
                if (operation is DropColumnOperation dropped)
                    columns.Remove(dropped.Table + "." + dropped.Name);
            }

            foreach (var entity in refresh.TargetModel.GetEntityTypes())
            {
                var table = StoreObjectIdentifier.Table(entity.GetTableName(), entity.GetSchema());
                foreach (var property in entity.GetProperties().Where(p =>
                    p.GetValueGenerationStrategy() == MySqlValueGenerationStrategy.IdentityColumn))
                {
                    var key = table.Name + "." + property.GetColumnName(table);
                    Assert.IsTrue(columns.ContainsKey(key), key);
                    Assert.AreEqual(MySqlValueGenerationStrategy.IdentityColumn,
                        columns[key]["MySql:ValueGenerationStrategy"], key);
                }
            }

        }

        [TestMethod]
        [DataRow(typeof(MySqlAuthContext))]
        [DataRow(typeof(MySqlCharContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void MySqlMigrationsGenerateUpgradeSql(Type contextType)
        {
            using var context = CreateContext(contextType, "unused");
            var script = context.GetService<IMigrator>().GenerateScript();

            StringAssert.Contains(script, "CREATE TABLE");
            StringAssert.Contains(script, "__EFMigrationsHistory");
            StringAssert.Contains(script, "Net10IdentityMetadata");
            StringAssert.Contains(script, "AUTO_INCREMENT");
        }

        [TestMethod]
        public void MySqlMissionMigrationRemovesIdentityBeforeChangingThePrimaryKey()
        {
            using var context = CreateContext(typeof(MySqlCharContext), "unused");
            var assembly = context.GetService<IMigrationsAssembly>();
            var migration = assembly.Migrations.Values
                .Select(type => assembly.CreateMigration(type, context.Database.ProviderName))
                .Single(candidate => candidate.GetType().Name == "MissionCharacterState");

            var alterUp = migration.UpOperations
                .Select((operation, index) => (operation, index))
                .Single(item => item.operation is AlterColumnOperation column &&
                    column.Table == "character_mission" && column.Name == "character_id").index;
            var dropPrimaryKeyUp = migration.UpOperations
                .Select((operation, index) => (operation, index))
                .Single(item => item.operation is DropPrimaryKeyOperation primaryKey &&
                    primaryKey.Table == "character_mission").index;
            Assert.IsTrue(alterUp < dropPrimaryKeyUp,
                "MySQL requires AUTO_INCREMENT to be removed while character_id is still indexed.");

            var addPrimaryKeyDown = migration.DownOperations
                .Select((operation, index) => (operation, index))
                .Single(item => item.operation is AddPrimaryKeyOperation primaryKey &&
                    primaryKey.Table == "character_mission").index;
            var alterDown = migration.DownOperations
                .Select((operation, index) => (operation, index))
                .Single(item => item.operation is AlterColumnOperation column &&
                    column.Table == "character_mission" && column.Name == "character_id").index;
            Assert.IsTrue(addPrimaryKeyDown < alterDown,
                "MySQL requires character_id to be indexed before restoring AUTO_INCREMENT.");
        }

        [TestMethod]
        [DataRow(typeof(SqliteAuthContext), "20201211023733_Initial",
            "INSERT INTO account (email, username, password, salt) VALUES ('p0@example.invalid', 'p0_user', 'p0_hash', 'p0_salt')",
            "SELECT username AS Value FROM account WHERE email = 'p0@example.invalid'", "p0_user")]
        [DataRow(typeof(SqliteCharContext), "20230119213323_Fix_CharacterLogos_table",
            "INSERT INTO character_teleporter (character_id, waypointId) VALUES (123, 456)",
            "SELECT CAST(waypointId AS TEXT) AS Value FROM character_teleporter WHERE character_id = 123", "456")]
        [DataRow(typeof(SqliteWorldContext), "20230119210546_Add_all_tables",
            "INSERT INTO player_random_name (name, type, gender) VALUES ('P0Preserved', 1, 1)",
            "SELECT name AS Value FROM player_random_name WHERE name = 'P0Preserved'", "P0Preserved")]
        public void SqliteUpgradePreservesHistoricalRows(Type contextType, string migration,
            string insert, string select, string expected)
        {
            WithDisposableSqlite(contextType, (context, database) =>
            {
                context.GetService<IMigrator>().Migrate(migration);
                var applied = context.Database.GetAppliedMigrations().ToArray();
                context.Database.ExecuteSqlRaw(insert);

                context.Database.Migrate();

                Assert.AreEqual(expected, context.Database.SqlQueryRaw<string>(select).Single());
                CollectionAssert.IsSubsetOf(applied, context.Database.GetAppliedMigrations().ToArray());
                Assert.IsFalse(context.Database.GetPendingMigrations().Any());
            });
        }

        [TestMethod]
        public void SqliteMissionUpgradePreservesHistoricalRowAndAddsCharacterState()
        {
            WithDisposableSqlite(typeof(SqliteCharContext), (context, database) =>
            {
                context.GetService<IMigrator>().Migrate("20260917130621_AbilityTraySelection");
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO account (id, email, name, family_name) " +
                    "VALUES (17, 'p4@example.invalid', 'P4Account', 'P4Family')");
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO character (id, account_id, slot, name, race, class, gender, scale, experience, level, " +
                    "credit, body, mind, spirit, map_context_id, coord_x, coord_y, coord_z, rotation) " +
                    "VALUES (123, 17, 1, 'P4Preserved', 1, 1, 0, 1, 0, 1, 0, 0, 0, 0, 1220, 1, 2, 3, 0)");
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO character_mission (character_id, mission_id, mission_state) VALUES (123, 321, 4)");

                context.Database.Migrate();

                var row = context.Database.SqlQueryRaw<MissionUpgradeRow>(
                    "SELECT character_id AS CharacterId, mission_id AS MissionId, " +
                    "mission_state AS MissionState, completeable AS Completeable " +
                    "FROM character_mission WHERE character_id = 123").Single();
                Assert.AreEqual(123L, row.CharacterId);
                Assert.AreEqual(321L, row.MissionId);
                Assert.AreEqual(4L, row.MissionState);
                Assert.AreEqual(0L, row.Completeable);
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO character_mission (character_id, mission_id, mission_state) VALUES (123, 429, 0)");
                Assert.AreEqual(2, context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM character_mission WHERE character_id = 123").Single());
                Assert.IsFalse(context.Database.GetPendingMigrations().Any());
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteAuthContext))]
        [DataRow(typeof(SqliteCharContext))]
        [DataRow(typeof(SqliteWorldContext))]
        public void SqliteMigrationsCreateAndReopenDatabase(Type contextType)
        {
            WithDisposableSqlite(contextType, (context, database) =>
            {
                context.Database.Migrate();
                var applied = context.Database.GetAppliedMigrations().ToArray();
                CollectionAssert.AreEqual(context.Database.GetMigrations().ToArray(), applied);

                context.Database.ExecuteSqlRaw("CREATE TABLE p0_sentinel (value TEXT NOT NULL)");
                context.Database.ExecuteSqlRaw("INSERT INTO p0_sentinel VALUES ('preserved')");
                using var reopened = CreateContext(contextType, database);
                reopened.Database.Migrate();

                CollectionAssert.AreEqual(applied, reopened.Database.GetAppliedMigrations().ToArray());
                Assert.AreEqual("preserved", reopened.Database.SqlQueryRaw<string>(
                    "SELECT value AS Value FROM p0_sentinel").Single());
            });
        }

        [TestMethod]
        public void SqliteAccountCreationAndLoginPreserveGeneratedValues()
        {
            WithDisposableSqlite(typeof(SqliteAuthContext), (context, database) =>
            {
                context.Database.Migrate();
                using var random = new RandomNumberService();
                var repository = new AuthAccountRepository((AuthContext)context, random);
                repository.Create("p0@example.invalid", "p0_user", "password123");

                var account = repository.GetByUserName("p0_user", "password123");
                Assert.IsTrue(account.Id > 0);
                Assert.IsTrue(account.JoinDate > DateTime.MinValue);
                Assert.IsFalse(account.Locked);
                Assert.IsFalse(account.Validated);
                Assert.AreEqual(40, account.Salt.Length);
                repository.UpdateLoginData(account.Id, IPAddress.Loopback);
                repository.UpdateLastServer(account.Id, 234);

                using var reopened = (AuthContext)CreateContext(typeof(SqliteAuthContext), database);
                var saved = reopened.AuthAccountEntries.Single(a => a.Id == account.Id);
                Assert.AreEqual("127.0.0.1", saved.LastIp);
                Assert.AreEqual((byte)234, saved.LastServerId);
                Assert.IsNotNull(saved.LastLogin);
                Assert.AreEqual(account.Password, saved.Password);
            });
        }

        private static void WithDisposableSqlite(Type contextType, Action<RasaDbContextBase, string> action)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "TestDatabases", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var database = Path.Combine(path, "database");
                using var context = CreateContext(contextType, database);
                action(context, database);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, true);
            }
        }

        private static RasaDbContextBase CreateContext(Type contextType, string database)
        {
            var connection = new DatabaseConnectionConfiguration { Database = database };
            var isSqlite = contextType.Name.StartsWith("Sqlite", StringComparison.Ordinal);
            var options = Options.Create(new DatabaseConfiguration
            {
                Provider = isSqlite ? "Sqlite" : "MySql",
                Auth = connection,
                Char = connection,
                World = connection
            });
            IDbContextConfigurationService configuration = isSqlite
                ? new SqliteDbContextConfigurationService(new SqliteConnectionStringFactory())
                : new OfflineMySqlConfigurationService();
            IDbContextPropertyModifier modifier = isSqlite
                ? new SqliteDbContextPropertyModifier()
                : new MySqlDbContextPropertyModifier();
            return (RasaDbContextBase)Activator.CreateInstance(contextType, options, configuration, modifier);
        }

        private sealed class OfflineMySqlConfigurationService : IDbContextConfigurationService
        {
            public void Configure(DbContextOptionsBuilder builder, DatabaseConnectionConfiguration configuration)
            {
                // Model checks never connect to MySQL or use a developer's configuration.
                builder.UseMySql("Server=127.0.0.1;Database=unused", new MySqlServerVersion(new Version(8, 4, 0)));
            }
        }

        private sealed class MissionUpgradeRow
        {
            public long CharacterId { get; set; }
            public long MissionId { get; set; }
            public long MissionState { get; set; }
            public long Completeable { get; set; }
        }
    }
}
