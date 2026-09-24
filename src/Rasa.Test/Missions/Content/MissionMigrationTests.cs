using System;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Options;
using Rasa.Configuration;
using Rasa.Configuration.ConnectionStrings;
using Rasa.Configuration.ContextSetup;
using Rasa.Context.World;
using Rasa.Services.DbContext;
using Rasa.Missions.Content;
using Rasa.Missions.Runtime;
using Rasa.Missions.Scenes;

namespace Rasa.Test.Missions.Content
{
    using Rasa.Services.Preloader.Missions;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionMigrationTests
    {
        [TestMethod]
        public void FreshSqliteInitializationInstallsRunnableBootcampWithoutPublishing()
        {
            using var harness = BootcampRuntimeTestHarness.Create(useWorldContent: true);
            CollectionAssert.AreEquivalent(new uint[] { 1990, 1992, 1994, 1995, 2005 },
                harness.WorldContext.MissionContentDefinitionEntries.Where(entry => entry.Enabled)
                    .Select(entry => entry.MissionId).ToArray());
            Assert.AreEqual(5, harness.WorldContext.Set<MissionSceneBindingEntry>().Count());
            Assert.AreEqual(1, harness.WorldContext.Set<MissionExperienceBindingEntry>().Count(entry => entry.Enabled));
            harness.WorldContext.Database.OpenConnection();
            using (var command = harness.WorldContext.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('mission_active_release', 'mission_release_member')";
                Assert.AreEqual(0L, Convert.ToInt64(command.ExecuteScalar()));
            }
            harness.WorldContext.Database.CloseConnection();
            Assert.IsFalse(harness.Manager.LoadMissions().BlocksReadiness);
            Assert.IsTrue(harness.Manager.Scenes.OwnsExperience(1985));
            var before = harness.WorldContext.Database.GetAppliedMigrations().ToArray();

            harness.WorldContext.Initialize();

            CollectionAssert.AreEqual(before, harness.WorldContext.Database.GetAppliedMigrations().ToArray());
            Assert.IsFalse(harness.WorldContext.Database.GetPendingMigrations().Any());
            var conrad = harness.WorldContext.MissionIndicatorEntries.Single(entry =>
                entry.MissionId == 1995 && entry.ObjectiveId == 3 && entry.IndicatorId == 436);
            Assert.AreEqual(-99, conrad.PosX);
            Assert.AreEqual(74, conrad.PosZ);
            Assert.IsTrue(harness.WorldContext.Set<ItemTemplateWeaponEntry>().Any(entry => entry.Id == 17131));
        }

        [TestMethod]
        public void MigratedScenesUseTheSameDataForSqliteAndMySql()
        {
            var sqlite = new Rasa.Migrations.SqliteWorld.SeedMigratedBootcamp().UpOperations;
            var mysql = new Rasa.Migrations.MySqlWorld.SeedMigratedBootcamp().UpOperations;
            CollectionAssert.AreEqual(sqlite.Select(operation => operation.GetType().Name).ToArray(),
                mysql.Select(operation => operation.GetType().Name).ToArray());
            CollectionAssert.AreEqual(sqlite.OfType<InsertDataOperation>().SelectMany(operation =>
                    operation.Values.Cast<object>().Select(value => value?.ToString())).ToArray(),
                mysql.OfType<InsertDataOperation>().SelectMany(operation =>
                    operation.Values.Cast<object>().Select(value => value?.ToString())).ToArray());
            CollectionAssert.AreEqual(sqlite.OfType<UpdateDataOperation>().SelectMany(operation =>
                    operation.Values.Cast<object>().Select(value => value?.ToString())).ToArray(),
                mysql.OfType<UpdateDataOperation>().SelectMany(operation =>
                    operation.Values.Cast<object>().Select(value => value?.ToString())).ToArray());
        }

        [TestMethod]
        public void MigratedMissingScriptIsRejectedBeforeGameplay()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            MissionContentTestSupport.ConfigureScenes(harness.WorldContext, scenes => scenes[1990].Script = "missing.script");
            Assert.ThrowsExactly<MissionRuleException>(() => harness.Manager.LoadMissions());
        }

        [TestMethod]
        public void MissingEnabledDataReportsTheNormalMigrationRequirement()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            foreach (var definition in harness.WorldContext.MissionContentDefinitionEntries.Where(entry => entry.Enabled))
                definition.Enabled = false;
            harness.WorldContext.SaveChanges();
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Manager.LoadMissions());
            StringAssert.Contains(error.Message, "World data migrations");
        }

        [TestMethod]
        public void MissingMigratedPublicSpawnIsRejectedAtStartup()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            MissionContentTestSupport.ConfigureScenes(harness.WorldContext, scenes =>
                scenes[1990].Actors["missing"] = new SceneActorDefinition("missing", SceneActorKind.PublicSpawn, 999999));
            var error = Assert.ThrowsExactly<MissionRuleException>(() => harness.Manager.LoadMissions());
            StringAssert.Contains(error.Message, "public spawn 999999");
        }

        [TestMethod]
        public void MissingRequiredSceneBindingIsRejectedAtStartup()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            var binding = harness.WorldContext.Set<MissionSceneBindingEntry>().Single(entry => entry.MissionId == 1992);
            harness.WorldContext.Remove(binding);
            harness.WorldContext.SaveChanges();
            var error = Assert.ThrowsExactly<MissionRuleException>(() => harness.Manager.LoadMissions());
            StringAssert.Contains(error.Message, "requires a migrated scene script binding");
        }

        [TestMethod]
        public void MySqlStartupDoesNotApplyMigrationsOrOpenAConnection()
        {
            var configured = 0;
            using var context = new MySqlWorldContext(
                Options.Create(new DatabaseConfiguration
                {
                    Provider = "MySql",
                    World = new DatabaseConnectionConfiguration { Database = "not-created-by-startup" }
                }),
                new MySqlDbContextConfigurationService(new MySqlConnectionStringFactory(), _ =>
                {
                    configured++;
                    throw new InvalidOperationException("Startup must not connect to migrate MySQL.");
                }),
                new MySqlDbContextPropertyModifier());

            context.Initialize();

            Assert.AreEqual(0, configured);
        }

        [TestMethod]
        public void MigrationCanUpdateSceneDataWithoutAPublishOrNewRelease()
        {
            using var harness = BootcampRuntimeTestHarness.Create();
            MissionContentTestSupport.ConfigureScenes(harness.WorldContext, scenes =>
                scenes[1995].Actors["bootcamp-conrad-corpse"] = scenes[1995].Actors["bootcamp-conrad-corpse"]
                    with { Position = new ScenePosition(-98, 86.4f, 74) });
            Assert.IsFalse(harness.Manager.LoadMissions().BlocksReadiness);
            var row = harness.WorldContext.Set<MissionSceneBindingEntry>().AsNoTracking()
                .Single(entry => entry.MissionId == 1995);
            var scene = JsonSerializer.Deserialize<MissionSceneDefinition>(row.Bindings, MissionContentCodec.Options);
            Assert.AreEqual(-98, scene.Actors["bootcamp-conrad-corpse"].Position.X);
        }

        [TestMethod]
        public void SharedSceneSerializationRejectsUnknownFieldsAndPreservesTypedIntents()
        {
            var scene = BootcampMissionDataV1.Mission1995();
            var decoded = JsonSerializer.Deserialize<MissionSceneDefinition>(
                JsonSerializer.Serialize(scene, MissionContentCodec.Options), MissionContentCodec.Options);
            Assert.IsInstanceOfType<EnsureActorIntent>(decoded.Sequences[1].World[0]);
            Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<MissionSceneDefinition>(
                "{\"scrpit\":\"data.sequence\"}", MissionContentCodec.Options));
        }
    }
}
