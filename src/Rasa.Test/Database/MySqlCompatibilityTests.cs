using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;

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

    [TestClass]
    [TestCategory("MySql")]
    public class MySqlCompatibilityTests
    {
        [TestMethod]
        [DataRow(typeof(MySqlAuthContext), 45)]
        [DataRow(typeof(MySqlAuthContext), 46)]
        [DataRow(typeof(MySqlAuthContext), 64)]
        [DataRow(typeof(MySqlCharContext), 64)]
        [DataRow(typeof(MySqlWorldContext), 64)]
        public void CleanDatabaseMigratesAndReopens(Type contextType, int databaseNameLength)
        {
            using var database = DisposableDatabase.Create(contextType, databaseNameLength);
            using (var context = database.CreateContext())
            {
                context.Database.Migrate();
                Assert.IsFalse(context.Database.GetPendingMigrations().Any());
            }
            using (var reopened = database.CreateContext())
            {
                reopened.Database.Migrate();
                CollectionAssert.AreEqual(reopened.Database.GetMigrations().ToArray(),
                    reopened.Database.GetAppliedMigrations().ToArray());
            }
        }

        [TestMethod]
        [DataRow(typeof(MySqlAuthContext), 45, "20221231011802_Add_test_test_account",
            "INSERT INTO account (email, username, password, salt) VALUES ('p0@example.invalid', 'p0_user', 'p0_hash', 'p0_salt')",
            "SELECT username AS Value FROM account WHERE email = 'p0@example.invalid'", "p0_user")]
        [DataRow(typeof(MySqlAuthContext), 46, "20221231011802_Add_test_test_account",
            "INSERT INTO account (email, username, password, salt) VALUES ('p0@example.invalid', 'p0_user', 'p0_hash', 'p0_salt')",
            "SELECT username AS Value FROM account WHERE email = 'p0@example.invalid'", "p0_user")]
        [DataRow(typeof(MySqlAuthContext), 64, "20221231011802_Add_test_test_account",
            "INSERT INTO account (email, username, password, salt) VALUES ('p0@example.invalid', 'p0_user', 'p0_hash', 'p0_salt')",
            "SELECT username AS Value FROM account WHERE email = 'p0@example.invalid'", "p0_user")]
        [DataRow(typeof(MySqlCharContext), 64, "20230202081208_edited_character_teleporter",
            "INSERT INTO character_teleporter (character_id, waypointId, waypoint_type) VALUES (123, 456, 0)",
            "SELECT CAST(waypointId AS CHAR) AS Value FROM character_teleporter WHERE character_id = 123", "456")]
        [DataRow(typeof(MySqlWorldContext), 64, "20230119210702_Add_data_to_world",
            "INSERT INTO player_random_name (name, type, gender) VALUES ('P0Preserved', 1, 1)",
            "SELECT name AS Value FROM player_random_name WHERE name = 'P0Preserved'", "P0Preserved")]
        public void UpgradePreservesHistoricalRows(Type contextType, int databaseNameLength, string migration,
            string insert, string select, string expected)
        {
            using var database = DisposableDatabase.Create(contextType, databaseNameLength);
            string[] applied;
            using (var historical = database.CreateContext())
            {
                historical.GetService<IMigrator>().Migrate(migration);
                historical.Database.ExecuteSqlRaw(insert);
                applied = historical.Database.GetAppliedMigrations().ToArray();
            }
            using (var upgraded = database.CreateContext())
            {
                upgraded.Database.Migrate();

                Assert.AreEqual(expected, upgraded.Database.SqlQueryRaw<string>(select).Single());
                CollectionAssert.IsSubsetOf(applied, upgraded.Database.GetAppliedMigrations().ToArray());
                Assert.IsFalse(upgraded.Database.GetPendingMigrations().Any());
            }
        }

        [TestMethod]
        [DataRow(45)]
        [DataRow(46)]
        [DataRow(64)]
        public async Task MigrationLocksCoordinateAndReleaseForDistinctDatabases(int databaseNameLength)
        {
            using var database = DisposableDatabase.Create(typeof(MySqlAuthContext), databaseNameLength);
            using var first = database.CreateContext();
            using var second = database.CreateContext();
            await first.Database.OpenConnectionAsync();
            await second.Database.OpenConnectionAsync();
            Assert.AreEqual(databaseNameLength, first.Database.GetDbConnection().Database.Length);
            string name;
            using (first.GetService<IHistoryRepository>().AcquireDatabaseLock())
            {
                name = ReadHeldLockName(first);
                Assert.IsTrue(name.Length <= 64);
                if (databaseNameLength == 45)
                {
                    var legacyName = $"__{first.Database.GetDbConnection().Database}_EFMigrationsLock";
                    Assert.IsTrue(string.Equals(legacyName, name, StringComparison.OrdinalIgnoreCase));
                    Assert.AreEqual(0, ExecuteLockScalar(second, "SELECT GET_LOCK(@name, 0)", legacyName),
                        "Existing short-name clients must coordinate with the provider lock.");
                }
                Assert.AreEqual(0, ExecuteLockScalar(second, "SELECT GET_LOCK(@name, 0)", name),
                    "A second connection must not acquire a held migration lock.");
            }
            Assert.AreEqual(1, ExecuteLockScalar(second, "SELECT IS_FREE_LOCK(@name)", name));

            await using (await second.GetService<IHistoryRepository>().AcquireDatabaseLockAsync())
            {
                Assert.AreEqual(name, ReadHeldLockName(second), "Sync/async instances must coordinate on one name.");
                Assert.AreEqual(0, ExecuteLockScalar(first, "SELECT GET_LOCK(@name, 0)", name));
            }
            Assert.AreEqual(1, ExecuteLockScalar(first, "SELECT IS_FREE_LOCK(@name)", name));

            var firstDatabaseName = first.Database.GetDbConnection().Database;
            var otherDatabaseName = firstDatabaseName.Substring(0, firstDatabaseName.Length - 1) + "y";
            using var otherDatabase = DisposableDatabase.Create(typeof(MySqlAuthContext), databaseNameLength, otherDatabaseName);
            using var other = otherDatabase.CreateContext();
            await other.Database.OpenConnectionAsync();
            using (other.GetService<IHistoryRepository>().AcquireDatabaseLock())
            {
                var otherName = ReadHeldLockName(other);
                Assert.IsTrue(otherName.Length <= 64);
                Assert.AreNotEqual(name, otherName, "Distinct databases need distinct migration locks.");
                using (first.GetService<IHistoryRepository>().AcquireDatabaseLock())
                    Assert.AreEqual(name, ReadHeldLockName(first));
            }
        }

        private static string ReadHeldLockName(RasaDbContextBase context)
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT OBJECT_NAME FROM performance_schema.metadata_locks " +
                "WHERE OBJECT_TYPE = 'USER LEVEL LOCK' AND OWNER_THREAD_ID = " +
                "(SELECT THREAD_ID FROM performance_schema.threads WHERE PROCESSLIST_ID = CONNECTION_ID())";
            var name = command.ExecuteScalar() as string;
            Assert.IsNotNull(name, "The provider must hold an actual MySQL named lock.");
            return name;
        }

        private static int ExecuteLockScalar(RasaDbContextBase context, string sql, string name)
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = name;
            command.Parameters.Add(parameter);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private sealed class DisposableDatabase : IDisposable
        {
            private readonly Type _contextType;
            private readonly MySqlConnection _administration;
            private readonly DatabaseConnectionConfiguration _configuration;

            private DisposableDatabase(Type contextType, MySqlConnection administration,
                DatabaseConnectionConfiguration configuration)
            {
                _contextType = contextType;
                _administration = administration;
                _configuration = configuration;
            }

            public static DisposableDatabase Create(Type contextType, int databaseNameLength, string databaseName = null)
            {
                var connectionString = Environment.GetEnvironmentVariable("RASA_TEST_MYSQL_CONNECTION");
                if (string.IsNullOrWhiteSpace(connectionString))
                    Assert.Inconclusive("Set RASA_TEST_MYSQL_CONNECTION to a disposable local MySQL instance on an ephemeral port.");

                var builder = new MySqlConnectionStringBuilder(connectionString);
                Assert.IsTrue(builder.Server == "127.0.0.1" || builder.Server == "localhost",
                    "Integration tests require an explicitly supplied local disposable MySQL instance.");
                Assert.AreNotEqual(3306U, builder.Port, "Use an isolated ephemeral host port, not a shared MySQL port.");
                Assert.IsTrue(string.IsNullOrEmpty(builder.Database), "Do not supply an existing database.");
                Assert.IsTrue(databaseNameLength >= 40 && databaseNameLength <= 64);
                databaseName ??= ("rasa_p0_" + Guid.NewGuid().ToString("N")).PadRight(databaseNameLength, 'x');
                Assert.AreEqual(databaseNameLength, databaseName.Length);
                Assert.IsTrue(databaseName.StartsWith("rasa_p0_", StringComparison.Ordinal) &&
                    databaseName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));

                var configuration = new DatabaseConnectionConfiguration
                {
                    Host = builder.Server,
                    Port = builder.Port,
                    User = builder.UserID,
                    Password = builder.Password,
                    Database = databaseName,
                    TimeoutInMilliseconds = 60000
                };
                var administration = new MySqlConnection(builder.ConnectionString);
                try
                {
                    administration.Open();
                    using var command = administration.CreateCommand();
                    command.CommandText = $"CREATE DATABASE `{configuration.Database}`";
                    command.ExecuteNonQuery();
                    return new DisposableDatabase(contextType, administration, configuration);
                }
                catch
                {
                    administration.Dispose();
                    throw;
                }
            }

            public RasaDbContextBase CreateContext()
            {
                var options = Options.Create(new DatabaseConfiguration
                {
                    Provider = "MySql",
                    Auth = _configuration,
                    Char = _configuration,
                    World = _configuration
                });
                return (RasaDbContextBase)Activator.CreateInstance(_contextType, options,
                    new MySqlDbContextConfigurationService(new MySqlConnectionStringFactory()),
                    new MySqlDbContextPropertyModifier());
            }

            public void Dispose()
            {
                try
                {
                    using var command = _administration.CreateCommand();
                    command.CommandText = $"DROP DATABASE `{_configuration.Database}`";
                    command.ExecuteNonQuery();
                }
                finally
                {
                    _administration.Dispose();
                    MySqlConnection.ClearAllPools();
                }
            }
        }
    }
}
