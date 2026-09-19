using System;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Configuration;
    using Rasa.Configuration.ConnectionStrings;
    using Rasa.Configuration.ContextSetup;
    using Rasa.Context;
    using Rasa.Context.World;
    using Rasa.Services.DbContext;
    using Rasa.Structures.World;

    [TestClass]
    public class MissionContentSchemaTests
    {
        [TestMethod]
        [DataRow(typeof(SqliteWorldContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void WorldMissionContentModelDefinesCompositeKeysAndDeleteBehaviors(Type contextType)
        {
            using var context = CreateContext(contextType, "unused");
            var model = context.Model;

            var content = RequireEntity(model, typeof(MissionContentDefinitionEntry));
            var prerequisite = RequireEntity(model, typeof(MissionPrerequisiteEntry));
            var objective = RequireEntity(model, typeof(MissionObjectiveDefinitionEntry));
            var transition = RequireEntity(model, typeof(MissionObjectiveTransitionEntry));
            var trigger = RequireEntity(model, typeof(MissionTriggerEntry));
            var action = RequireEntity(model, typeof(MissionActionEntry));
            var reward = RequireEntity(model, typeof(MissionRewardDefinitionEntry));
            var rewardItem = RequireEntity(model, typeof(MissionRewardItemEntry));
            var indicator = RequireEntity(model, typeof(MissionIndicatorEntry));
            var area = RequireEntity(model, typeof(MissionAreaEntry));
            var spawnGroup = RequireEntity(model, typeof(MissionSpawnGroupEntry));
            var spawn = RequireEntity(model, typeof(MissionSpawnEntry));
            var scenario = RequireEntity(model, typeof(MissionScenarioEntry));
            var scenarioStep = RequireEntity(model, typeof(MissionScenarioStepEntry));
            var evidence = RequireEntity(model, typeof(MissionEvidenceEntry));
            var legacyReward = RequireEntity(model, typeof(NpcMissionRewardEntry));

            AssertKey(content, "MissionId", "ContentRevision");
            AssertKey(prerequisite, "MissionId", "ContentRevision", "PrerequisiteId");
            AssertKey(objective, "MissionId", "ContentRevision", "ObjectiveId");
            AssertKey(transition, "MissionId", "ContentRevision", "ObjectiveId", "TransitionId");
            AssertKey(trigger, "MissionId", "ContentRevision", "ObjectiveId", "TransitionId", "TriggerId");
            AssertKey(action, "MissionId", "ContentRevision", "ObjectiveId", "TransitionId", "ActionId");
            AssertKey(reward, "MissionId", "ContentRevision", "RewardId");
            AssertKey(rewardItem, "MissionId", "ContentRevision", "RewardId", "ItemId");
            AssertKey(indicator, "MissionId", "ContentRevision", "ObjectiveId", "IndicatorId");
            AssertKey(area, "MissionId", "ContentRevision", "AreaId");
            AssertKey(spawnGroup, "MissionId", "ContentRevision", "SpawnGroupId");
            AssertKey(spawn, "MissionId", "ContentRevision", "SpawnGroupId", "SpawnId");
            AssertKey(scenario, "MissionId", "ContentRevision", "ScenarioId");
            AssertKey(scenarioStep, "MissionId", "ContentRevision", "ScenarioId", "StepId");
            AssertKey(evidence, "MissionId", "ContentRevision", "EvidenceId");

            Assert.IsTrue(content.GetIndexes().Any(index =>
                index.GetDatabaseName() == "mission_content_definition_index_content_revision"));

            AssertDeleteBehavior(prerequisite, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(objective, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(transition, typeof(MissionObjectiveDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(trigger, typeof(MissionObjectiveTransitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(action, typeof(MissionObjectiveTransitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(reward, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(indicator, typeof(MissionObjectiveDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(area, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(spawnGroup, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(scenario, typeof(MissionContentDefinitionEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(scenarioStep, typeof(MissionScenarioEntry), DeleteBehavior.Restrict);
            AssertDeleteBehavior(rewardItem, typeof(MissionRewardDefinitionEntry), DeleteBehavior.Cascade);
            AssertDeleteBehavior(spawn, typeof(MissionSpawnGroupEntry), DeleteBehavior.Cascade);
            AssertDeleteBehavior(evidence, typeof(MissionContentDefinitionEntry), DeleteBehavior.Cascade);

            Assert.AreEqual("content_revision", content.FindProperty("ContentRevision")?.GetColumnName());
            Assert.AreEqual("requirement", content.FindProperty("Requirement")?.GetColumnName());
            Assert.IsNull(legacyReward.FindPrimaryKey(), "Legacy reward rows should remain keyless.");
        }

        [TestMethod]
        public void WorldMissionContentSnapshotsMatchModels()
        {
            foreach (var contextType in new[] { typeof(SqliteWorldContext), typeof(MySqlWorldContext) })
            {
                using var context = CreateContext(contextType, "unused");
                Assert.IsFalse(context.Database.HasPendingModelChanges(), contextType.Name);
                Assert.IsTrue(
                    context.Database.GetMigrations().Last().EndsWith(
                        "MissionContentDefinition",
                        StringComparison.Ordinal),
                    contextType.Name);
            }
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void WorldMissionContentMigrationsGenerateOfflineSql(Type contextType)
        {
            using var context = CreateContext(contextType, "unused");
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript());

            AssertContainsCreateTable(sql, MissionContentDefinitionEntry.TableName);
            AssertContainsCreateTable(sql, MissionPrerequisiteEntry.TableName);
            AssertContainsCreateTable(sql, MissionObjectiveDefinitionEntry.TableName);
            AssertContainsCreateTable(sql, MissionObjectiveTransitionEntry.TableName);
            AssertContainsCreateTable(sql, MissionTriggerEntry.TableName);
            AssertContainsCreateTable(sql, MissionActionEntry.TableName);
            AssertContainsCreateTable(sql, MissionRewardDefinitionEntry.TableName);
            AssertContainsCreateTable(sql, MissionRewardItemEntry.TableName);
            AssertContainsCreateTable(sql, MissionIndicatorEntry.TableName);
            AssertContainsCreateTable(sql, MissionAreaEntry.TableName);
            AssertContainsCreateTable(sql, MissionSpawnGroupEntry.TableName);
            AssertContainsCreateTable(sql, MissionSpawnEntry.TableName);
            AssertContainsCreateTable(sql, MissionScenarioEntry.TableName);
            AssertContainsCreateTable(sql, MissionScenarioStepEntry.TableName);
            AssertContainsCreateTable(sql, MissionEvidenceEntry.TableName);

            StringAssert.Contains(
                sql,
                "primary key (mission_id, content_revision, objective_id, transition_id, trigger_id)");
            StringAssert.Contains(
                sql,
                "primary key (mission_id, content_revision, reward_id, item_id)");
            StringAssert.Contains(
                sql,
                "primary key (mission_id, content_revision, scenario_id, step_id)");
            StringAssert.Contains(sql, "mission_content_definition_index_content_revision");
        }

        private static void AssertContainsCreateTable(string sql, string tableName)
        {
            StringAssert.Contains(sql, $"create table {tableName}");
        }

        private static void AssertDeleteBehavior(
            IEntityType entity,
            Type principalClrType,
            DeleteBehavior expected)
        {
            var foreignKey = entity.GetForeignKeys().Single(fk =>
                fk.PrincipalEntityType.ClrType == principalClrType);
            Assert.AreEqual(expected, foreignKey.DeleteBehavior);
        }

        private static void AssertKey(IEntityType entity, params string[] propertyNames)
        {
            CollectionAssert.AreEqual(
                propertyNames,
                entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        }

        private static IEntityType RequireEntity(IModel model, Type clrType)
        {
            var entity = model.FindEntityType(clrType);
            Assert.IsNotNull(entity, clrType.FullName);
            return entity!;
        }

        private static string NormalizeSql(string sql)
        {
            sql = sql.ToLowerInvariant()
                .Replace("`", string.Empty)
                .Replace("\"", string.Empty)
                .Replace("[", string.Empty)
                .Replace("]", string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");
            return Regex.Replace(sql, "\\s+", " ").Trim();
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
            return (RasaDbContextBase)Activator.CreateInstance(
                contextType,
                options,
                configuration,
                modifier)!;
        }

        private sealed class OfflineMySqlConfigurationService : IDbContextConfigurationService
        {
            public void Configure(
                DbContextOptionsBuilder builder,
                DatabaseConnectionConfiguration configuration)
            {
                builder.UseMySql(
                    "Server=127.0.0.1;Database=unused",
                    new MySqlServerVersion(new Version(8, 4, 0)));
            }
        }
    }
}
