using System;
using System.Linq;
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
        [DataRow(typeof(MySqlAuthContext))]
        [DataRow(typeof(MySqlCharContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void CleanDatabaseMigratesAndReopens(Type contextType)
        {
            using var database = DisposableDatabase.Create(contextType);
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
        [DataRow(typeof(MySqlAuthContext), "20221231011802_Add_test_test_account",
            "INSERT INTO account (email, username, password, salt) VALUES ('p0@example.invalid', 'p0_user', 'p0_hash', 'p0_salt')",
            "SELECT username AS Value FROM account WHERE email = 'p0@example.invalid'", "p0_user")]
        [DataRow(typeof(MySqlCharContext), "20230202081208_edited_character_teleporter",
            "INSERT INTO character_teleporter (character_id, waypointId, waypoint_type) VALUES (123, 456, 0)",
            "SELECT CAST(waypointId AS CHAR) AS Value FROM character_teleporter WHERE character_id = 123", "456")]
        [DataRow(typeof(MySqlWorldContext), "20230119210702_Add_data_to_world",
            "INSERT INTO player_random_name (name, type, gender) VALUES ('P0Preserved', 1, 1)",
            "SELECT name AS Value FROM player_random_name WHERE name = 'P0Preserved'", "P0Preserved")]
        public void UpgradePreservesHistoricalRows(Type contextType, string migration,
            string insert, string select, string expected)
        {
            using var database = DisposableDatabase.Create(contextType);
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

            public static DisposableDatabase Create(Type contextType)
            {
                var connectionString = Environment.GetEnvironmentVariable("RASA_TEST_MYSQL_CONNECTION");
                if (string.IsNullOrWhiteSpace(connectionString))
                    Assert.Inconclusive("Set RASA_TEST_MYSQL_CONNECTION to a disposable local MySQL instance on an ephemeral port.");

                var builder = new MySqlConnectionStringBuilder(connectionString);
                Assert.IsTrue(builder.Server == "127.0.0.1" || builder.Server == "localhost",
                    "Integration tests require an explicitly supplied local disposable MySQL instance.");
                Assert.AreNotEqual(3306U, builder.Port, "Use an isolated ephemeral host port, not a shared MySQL port.");
                Assert.IsTrue(string.IsNullOrEmpty(builder.Database), "Do not supply an existing database.");

                var configuration = new DatabaseConnectionConfiguration
                {
                    Host = builder.Server,
                    Port = builder.Port,
                    User = builder.UserID,
                    Password = builder.Password,
                    Database = "rasa_p0_" + Guid.NewGuid().ToString("N"),
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
