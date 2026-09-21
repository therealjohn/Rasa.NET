using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Configuration;
    using Configuration.ConnectionStrings;
    using Configuration.ContextSetup;
    using Context;
    using Context.World;
    using Rasa.Data;
    using Microsoft.Data.Sqlite;
    using Rasa.Navigation;
    using Repositories.World;
    using Rasa.Services.Preloader;
    using Services.DbContext;
    using Structures.Missions;
    using Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class BootcampMissionContentTests
    {
        private const string BootcampRevision = "deployment_11";
        private const string SqliteMigrationId = "20260919110000_BootcampMissionContent";
        private const string MySqlMigrationId = "20260919110000_BootcampMissionContent";
        private static readonly uint[] RequiredMissionIds = { 1990, 1992, 1994, 1995, 2005 };
        private static readonly uint[] AllMissionIds = { 1990, 1992, 1994, 1995, 2005 };
        private static readonly uint[] BootcampNpcIds =
        {
            510203, 510204, 510205, 510206, 510207, 510208, 510209, 510210, 510211, 510212
        };

        [TestMethod]
        public void SqliteBootcampMissionContentLoadsAndValidatesAgainstFreshMigratedDatabase()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var report = Validate(snapshot, context);

                CollectionAssert.AreEquivalent(
                    AllMissionIds,
                    snapshot.Definitions.Keys
                        .Where(id => AllMissionIds.Contains(id))
                        .OrderBy(id => id)
                        .ToArray());
                Assert.IsFalse(report.BlocksReadiness);
                Assert.AreEqual(
                    0,
                    report.Diagnostics.Count(diagnostic =>
                        diagnostic.MissionId.HasValue &&
                        RequiredMissionIds.Contains(diagnostic.MissionId.Value)));

                Assert.AreEqual(BootcampRevision, snapshot.Definitions[1992].ContentRevision);
                Assert.AreEqual(1250U, snapshot.Definitions[1992].Rewards[1].Experience);
                Assert.AreEqual(200U, snapshot.Definitions[1992].Rewards[1].Credits);
                Assert.AreEqual(5000U, snapshot.Definitions[1994].Rewards[1].Experience);
                Assert.AreEqual(0U, snapshot.Definitions[1994].Rewards[1].Credits);
                Assert.AreEqual(MissionContentRequirement.Required, snapshot.Definitions[2005].Requirement);
            });
        }

        [TestMethod]
        public void BootcampMissionContentMarksTheRetryAsRequiredAndDefersDepartureThroughNormalizedMetadata()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var initiation = snapshot.Definitions[1990];
                var gearingUp = snapshot.Definitions[1992];
                var finalMission = snapshot.Definitions[1995];
                var retryMission = snapshot.Definitions[2005];

                Assert.AreEqual(MissionAbandonmentPolicy.Prohibited, initiation.AbandonmentPolicy);
                Assert.AreEqual(MissionContentRequirement.Required, retryMission.Requirement);
                Assert.AreEqual(MissionContentRequirement.Required, retryMission.Prerequisites.Single().Requirement);
                Assert.IsTrue(retryMission.Objectives.Values.All(objective =>
                    objective.Requirement == MissionContentRequirement.Required));
                Assert.IsTrue(retryMission.Transitions.Values.All(transition =>
                    transition.Requirement == MissionContentRequirement.Required &&
                    transition.Triggers.All(trigger => trigger.Requirement == MissionContentRequirement.Required) &&
                    transition.Actions.All(action => action.Requirement == MissionContentRequirement.Required)));
                Assert.IsTrue(retryMission.Scenarios.Values.All(scenario =>
                    scenario.Requirement == MissionContentRequirement.Required &&
                    scenario.Steps.All(step => step.Requirement == MissionContentRequirement.Required)));
                Assert.AreEqual(MissionScenarioStartPolicy.PlayerTriggered, finalMission.Scenarios[6].StartPolicy);
                Assert.AreEqual(MissionScenarioStartPolicy.PlayerTriggered, retryMission.Scenarios[5].StartPolicy);
                CollectionAssert.Contains(
                    gearingUp.Rewards[58].FixedItems.Select(item => item.ItemTemplateId).ToArray(),
                    28U);
            });
        }

        [TestMethod]
        public void BootcampMissionContentResolvesReferencesAndSnapsMeasuredRowsToNavmesh()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var report = Validate(snapshot, context);
                Assert.AreEqual(
                    0,
                    report.Diagnostics.Count(diagnostic =>
                        diagnostic.MissionId.HasValue &&
                        RequiredMissionIds.Contains(diagnostic.MissionId.Value)));

                var creatureIds = context.CreatureEntries.Select(entry => entry.Id).ToHashSet();
                var packageIds = context.NpcPackageEntries.Select(entry => entry.PackageId).ToHashSet();
                var entityClassIds = context.EntityClassEntries.Select(entry => entry.Id).ToHashSet();
                var itemTemplateIds = context.ItemTemplateItemClassEntries.Select(entry => entry.ItemTemplateId).ToHashSet();
                var mapContextIds = context.MapInfoEntries.Select(entry => entry.Id).ToHashSet();

                foreach (var definition in snapshot.Definitions.Values.Where(definition => AllMissionIds.Contains(definition.MissionId)))
                {
                    if (definition.Mission.MissionGiver.HasValue && definition.Mission.MissionGiver.Value != 0)
                        Assert.IsTrue(creatureIds.Contains(definition.Mission.MissionGiver.Value),
                            $"Missing giver creature {definition.Mission.MissionGiver.Value} for mission {definition.MissionId}.");
                    if (definition.Mission.MissionReciver.HasValue && definition.Mission.MissionReciver.Value != 0)
                        Assert.IsTrue(creatureIds.Contains(definition.Mission.MissionReciver.Value),
                            $"Missing receiver creature {definition.Mission.MissionReciver.Value} for mission {definition.MissionId}.");

                    foreach (var transition in definition.Transitions.Values)
                    foreach (var trigger in transition.Triggers.Where(trigger => trigger.NpcPackageId.HasValue))
                        Assert.IsTrue(packageIds.Contains(trigger.NpcPackageId.Value),
                            $"Missing npc_package.package_id {trigger.NpcPackageId.Value} for mission {definition.MissionId} objective {transition.ObjectiveId}.");

                    foreach (var reward in definition.Rewards.Values)
                    foreach (var item in reward.FixedItems.Concat(reward.SelectableItems))
                        Assert.IsTrue(itemTemplateIds.Contains(item.ItemTemplateId),
                            $"Missing item template {item.ItemTemplateId} for mission {definition.MissionId} reward {reward.RewardId}.");

                    foreach (var area in definition.Areas.Values)
                        Assert.IsTrue(mapContextIds.Contains(area.MapContextId),
                            $"Missing map {area.MapContextId} for mission {definition.MissionId} area {area.AreaId}.");

                    foreach (var spawnGroup in definition.SpawnGroups.Values)
                    {
                        Assert.IsTrue(mapContextIds.Contains(spawnGroup.MapContextId),
                            $"Missing map {spawnGroup.MapContextId} for mission {definition.MissionId} spawn group {spawnGroup.SpawnGroupId}.");
                        foreach (var spawn in spawnGroup.Spawns)
                            Assert.IsTrue(creatureIds.Contains(spawn.CreatureId),
                                $"Missing creature {spawn.CreatureId} for mission {definition.MissionId} spawn group {spawnGroup.SpawnGroupId}.");
                    }

                    foreach (var scenario in definition.Scenarios.Values)
                    foreach (var step in scenario.Steps)
                    {
                        if (step.EntityClassId.HasValue)
                            Assert.IsTrue(entityClassIds.Contains(step.EntityClassId.Value),
                                $"Missing entity class {step.EntityClassId.Value} for mission {definition.MissionId} scenario {scenario.ScenarioId} step {step.StepId}.");
                        if (step.MapContextId.HasValue)
                            Assert.IsTrue(mapContextIds.Contains(step.MapContextId.Value),
                                $"Missing destination map {step.MapContextId.Value} for mission {definition.MissionId} scenario {scenario.ScenarioId} step {step.StepId}.");
                        if (step.RewardId.HasValue)
                            Assert.IsTrue(definition.Rewards.ContainsKey(step.RewardId.Value),
                                $"Missing reward {step.RewardId.Value} for mission {definition.MissionId} scenario {scenario.ScenarioId} step {step.StepId}.");
                    }
                }

                CollectionAssert.IsSubsetOf(BootcampNpcIds, creatureIds.OrderBy(id => id).ToArray());

                var bootcampNav = new NavMeshQuery(NavMeshFile.Read(
                    NavMeshFile.PathFor(Path.Combine(FindRepositoryRoot(), "navmesh"), "adv_bootcamp")));
                var wildernessNav = new NavMeshQuery(NavMeshFile.Read(
                    NavMeshFile.PathFor(Path.Combine(FindRepositoryRoot(), "navmesh"), "adv_foreas_concordia_wilderness")));

                foreach (var spawn in context.SpawnPoolEntries
                             .Where(entry => entry.Id >= 510203 && entry.Id <= 510206))
                    AssertOnMesh(bootcampNav, spawn.PosX, spawn.PosY, spawn.PosZ, $"static npc spawn {spawn.Id}");

                foreach (var area in context.MissionAreaEntries
                             .Where(entry => AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision))
                    AssertOnMesh(ResolveNav(area.MapContextId, bootcampNav, wildernessNav), area.PosX, area.PosY, area.PosZ,
                        $"mission {area.MissionId} area {area.AreaId}");

                foreach (var indicator in context.MissionIndicatorEntries
                             .Where(entry => AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision))
                    AssertOnMesh(bootcampNav, indicator.PosX, indicator.PosY, indicator.PosZ,
                        $"mission {indicator.MissionId} indicator {indicator.IndicatorId}");

                foreach (var spawn in context.MissionSpawnEntries
                             .Where(entry => AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision))
                    AssertOnMesh(bootcampNav, spawn.PosX, spawn.PosY, spawn.PosZ,
                        $"mission {spawn.MissionId} spawn group {spawn.SpawnGroupId} spawn {spawn.SpawnId}");

                foreach (var step in context.MissionScenarioStepEntries
                             .Where(entry => AllMissionIds.Contains(entry.MissionId) &&
                                             entry.ContentRevision == BootcampRevision &&
                                             entry.PosX.HasValue &&
                                             entry.PosY.HasValue &&
                                             entry.PosZ.HasValue))
                    AssertOnMesh(
                        ResolveNav(step.MapContextId.GetValueOrDefault(1985), bootcampNav, wildernessNav),
                        step.PosX.Value,
                        step.PosY.Value,
                        step.PosZ.Value,
                        $"mission {step.MissionId} scenario {step.ScenarioId} step {step.StepId}");
            });
        }

        [TestMethod]
        public void BootcampMissionContentSeedsOnlyEntryObjectivesAsInitiallyIncomplete()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);

                AssertObjectiveStates(snapshot, 1990, (1U, MissionObjectiveState.Incomplete), (2U, MissionObjectiveState.Inactive));
                AssertObjectiveStates(snapshot, 1992,
                    (10U, MissionObjectiveState.Completed),
                    (4U, MissionObjectiveState.Incomplete),
                    (1U, MissionObjectiveState.Inactive),
                    (2U, MissionObjectiveState.Inactive),
                    (5U, MissionObjectiveState.Inactive),
                    (6U, MissionObjectiveState.Inactive),
                    (3U, MissionObjectiveState.Inactive),
                    (9U, MissionObjectiveState.Inactive),
                    (8U, MissionObjectiveState.Inactive),
                    (7U, MissionObjectiveState.Inactive));
                AssertObjectiveStates(snapshot, 1994,
                    (4U, MissionObjectiveState.Incomplete),
                    (2U, MissionObjectiveState.Inactive),
                    (1U, MissionObjectiveState.Inactive),
                    (3U, MissionObjectiveState.Inactive));
                AssertObjectiveStates(snapshot, 1995,
                    (2U, MissionObjectiveState.Incomplete),
                    (10U, MissionObjectiveState.Inactive),
                    (3U, MissionObjectiveState.Inactive),
                    (1U, MissionObjectiveState.Inactive),
                    (4U, MissionObjectiveState.Inactive));
                AssertObjectiveStates(snapshot, 2005,
                    (1U, MissionObjectiveState.Incomplete),
                    (4U, MissionObjectiveState.Inactive));
            });
        }

        [TestMethod]
        public void BootcampMissionContentUsesAreaDiscoveryAndSatisfiedDeadlinesForTheFinale()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var finalMission = snapshot.Definitions[1995];
                var retryMission = snapshot.Definitions[2005];

                var scoutTransition = finalMission.Transitions[(2U, 1U)];
                var scoutTrigger = scoutTransition.Triggers.Single();
                Assert.AreEqual(MissionTriggerKind.AreaEntered, scoutTrigger.Kind);
                Assert.AreEqual(435U, scoutTrigger.AreaId);
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        MissionActionKind.CompleteObjective,
                        MissionActionKind.StartScenario,
                        MissionActionKind.RevealObjective,
                        MissionActionKind.ActivateObjective
                    },
                    scoutTransition.Actions.Select(action => action.Kind).ToArray());
                CollectionAssert.AreEqual(
                    new[] { 2U, 10U, 10U },
                    scoutTransition.Actions
                        .Where(action => action.TargetObjectiveId.HasValue)
                        .Select(action => action.TargetObjectiveId!.Value)
                        .ToArray());

                var survivorScene = finalMission.Scenarios[1];
                Assert.IsTrue(survivorScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.SpawnGroup &&
                    step.SpawnGroupId == 1U));
                Assert.IsFalse(survivorScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.SpawnDynamicObject &&
                    step.DynamicObjectKey == "bootcamp-conrad-corpse"));
                Assert.IsFalse(survivorScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.SpawnDynamicObject &&
                    step.DynamicObjectKey == "bootcamp-dropship-debris"));

                var survivorConversationTransition = finalMission.Transitions[(10U, 1U)];
                var survivorConversationTrigger = survivorConversationTransition.Triggers.Single();
                Assert.AreEqual(MissionTriggerKind.Conversation, survivorConversationTrigger.Kind);
                Assert.AreEqual(2584U, survivorConversationTrigger.NpcPackageId);
                CollectionAssert.AreEqual(
                    new[] { 10U, 3U, 3U },
                    survivorConversationTransition.Actions
                        .Where(action => action.TargetObjectiveId.HasValue)
                        .Select(action => action.TargetObjectiveId!.Value)
                        .ToArray());

                var crashSiteScene = finalMission.Scenarios[7];
                Assert.IsTrue(crashSiteScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.DespawnGroup &&
                    step.SpawnGroupId == 1U));
                Assert.IsTrue(crashSiteScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.SpawnDynamicObject &&
                    step.DynamicObjectKey == "bootcamp-conrad-corpse"));
                Assert.IsTrue(crashSiteScene.Steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.SpawnDynamicObject &&
                    step.DynamicObjectKey == "bootcamp-dropship-debris"));

                Assert.AreEqual(
                    MissionScenarioStepKind.SatisfyDeadline,
                    finalMission.Scenarios[3].Steps.First().Kind);
                Assert.AreEqual(
                    MissionScenarioStepKind.SatisfyDeadline,
                    retryMission.Scenarios[1].Steps.First().Kind);

                Assert.IsFalse(context.SpawnPoolEntries.Any(entry =>
                    entry.Id == BootcampRuntimeTestHarness.WoundedSurvivorCreatureId));
            });
        }

        [TestMethod]
        public void BootcampMissionContentKeepsClient1992ObjectivesAndAddsReconstructedMcAllisterHandoff()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var orderedObjectiveIds = snapshot.Definitions[1992].Mission.Objectives.Values
                    .OrderBy(objective => objective.Ordinal)
                    .Select(objective => objective.ObjectiveId)
                    .ToArray();
                CollectionAssert.AreEqual(
                    new uint[] { 10, 4, 1, 2, 5, 6, 3, 9, 8, 7 },
                    orderedObjectiveIds);

                var reconstructedHandoff = context.MissionEvidenceEntries.Single(entry =>
                    entry.MissionId == 1992 &&
                    entry.ContentRevision == BootcampRevision &&
                    entry.OwnerKind == MissionEvidenceOwnerKind.Objective &&
                    entry.OwnerId == 10);
                Assert.AreEqual(
                    MissionEvidenceSourceKind.Reconstruction,
                    reconstructedHandoff.SourceKind);
                StringAssert.Contains(
                    reconstructedHandoff.ReconstructionNote,
                    "McAllister handoff");
            });
        }

        [TestMethod]
        public void BootcampMissionContentAddsAReconstructedSurvivorConversationObjectiveBetweenClientObjectivesTwoAndThree()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);
                var orderedObjectiveIds = snapshot.Definitions[1995].Mission.Objectives.Values
                    .OrderBy(objective => objective.Ordinal)
                    .Select(objective => objective.ObjectiveId)
                    .ToArray();
                CollectionAssert.AreEqual(
                    new uint[] { 2, 10, 3, 1, 4 },
                    orderedObjectiveIds);

                var reconstructedConversation = context.MissionObjectiveDefinitionEntries.Single(entry =>
                    entry.MissionId == 1995 &&
                    entry.ContentRevision == BootcampRevision &&
                    entry.ObjectiveId == 10);
                Assert.AreEqual(21556U, reconstructedConversation.ClientNameTextId);
                Assert.AreEqual(21557U, reconstructedConversation.ClientBodyTextId);

                var reconstructionEvidence = context.MissionEvidenceEntries.Single(entry =>
                    entry.MissionId == 1995 &&
                    entry.ContentRevision == BootcampRevision &&
                    entry.OwnerKind == MissionEvidenceOwnerKind.Objective &&
                    entry.OwnerId == 10);
                Assert.AreEqual(
                    MissionEvidenceSourceKind.Reconstruction,
                    reconstructionEvidence.SourceKind);
                StringAssert.Contains(
                    reconstructionEvidence.ReconstructionNote,
                    "server-authored reconstruction objective");
            });
        }

        [TestMethod]
        public void BootcampMissionContentAuthorsReachableScenariosAndRetryFailurePrerequisite()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                context.Database.Migrate();

                var snapshot = LoadSnapshot(context);

                foreach (var missionId in new uint[] { 1992, 1994, 1995, 2005 })
                {
                    var definition = snapshot.Definitions[missionId];
                    var rootScenarioIds = definition.Transitions.Values
                        .SelectMany(transition => transition.Actions)
                        .Where(action => action.Kind == MissionActionKind.StartScenario && action.ScenarioId.HasValue)
                        .Select(action => action.ScenarioId!.Value)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();
                    Assert.IsTrue(rootScenarioIds.Length > 0, $"Mission {missionId} is missing a StartScenario root.");

                    var reachableScenarioIds = new HashSet<uint>(rootScenarioIds);
                    var pending = new Queue<uint>(rootScenarioIds);
                    while (pending.Count > 0)
                    {
                        var scenarioId = pending.Dequeue();
                        foreach (var targetScenarioId in definition.Scenarios[scenarioId].Steps
                                     .Where(step => step.Kind == MissionScenarioStepKind.ScheduleScenario && step.TargetScenarioId.HasValue)
                                     .Select(step => step.TargetScenarioId!.Value))
                        {
                            if (reachableScenarioIds.Add(targetScenarioId))
                                pending.Enqueue(targetScenarioId);
                        }
                    }

                    CollectionAssert.AreEquivalent(
                        definition.Scenarios.Keys.OrderBy(id => id).ToArray(),
                        reachableScenarioIds.OrderBy(id => id).ToArray(),
                        $"Mission {missionId} contains unreachable scenarios.");
                }

                var retryPrerequisite = snapshot.Definitions[2005].Prerequisites.Single();
                Assert.AreEqual(MissionPrerequisiteKind.MissionAccepted, retryPrerequisite.Kind);
                Assert.AreEqual(1995U, retryPrerequisite.RequiredMissionId);
                Assert.AreEqual((byte)MissionState.Failed, retryPrerequisite.RequiredMissionStateValue);
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext), SqliteMigrationId)]
        [DataRow(typeof(MySqlWorldContext), MySqlMigrationId)]
        public void BootcampMissionContentMigrationIsRegisteredForBothProviders(
            Type contextType,
            string migrationId)
        {
            using var context = CreateContext(contextType, "unused");
            CollectionAssert.Contains(context.Database.GetMigrations().ToArray(), migrationId);

            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript());
            StringAssert.Contains(sql, "mission_content_definition");
            StringAssert.Contains(sql, "1990");
            StringAssert.Contains(sql, "1992");
            StringAssert.Contains(sql, "1994");
            StringAssert.Contains(sql, "1995");
            StringAssert.Contains(sql, "2005");
        }

        [TestMethod]
        public void BootcampCrateLootMigrationUpgradesAndDowngradesExistingWorldContent()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                const string previous = "20260919183000_BootcampFinalReviewFixes";
                var migrator = context.GetService<IMigrator>();
                migrator.Migrate(previous);
                Assert.AreEqual(MissionScenarioStepKind.GrantRewardPackage,
                    context.MissionScenarioStepEntries.AsNoTracking().Single(step =>
                        step.MissionId == 1992 && step.ScenarioId == 2 && step.StepId == 1).Kind);

                migrator.Migrate();

                var steps = context.MissionScenarioStepEntries.AsNoTracking().Where(step =>
                    step.MissionId == 1992 && step.ScenarioId == 2).ToArray();
                Assert.AreEqual(MissionScenarioStepKind.DisableInteraction,
                    steps.Single(step => step.StepId == 1).Kind);
                Assert.AreEqual(29877U, steps.Single(step => step.StepId == 1).EntityClassId);
                Assert.IsFalse(steps.Any(step =>
                    step.Kind == MissionScenarioStepKind.GrantRewardPackage ||
                    step.Kind == MissionScenarioStepKind.DespawnDynamicObject));
                Assert.AreEqual(2, steps.Count(step => step.Kind == MissionScenarioStepKind.GrantSkillAbility));
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);

                migrator.Migrate(previous);

                steps = context.MissionScenarioStepEntries.AsNoTracking().Where(step =>
                    step.MissionId == 1992 && step.ScenarioId == 2).ToArray();
                Assert.AreEqual(MissionScenarioStepKind.GrantRewardPackage,
                    steps.Single(step => step.StepId == 1).Kind);
                Assert.AreEqual(MissionScenarioStepKind.DespawnDynamicObject,
                    steps.Single(step => step.StepId == 2).Kind);
                migrator.Migrate();
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext), "20260919183000_BootcampFinalReviewFixes", "20260921183000_BootcampCrateLoot")]
        [DataRow(typeof(MySqlWorldContext), "20260919183010_BootcampFinalReviewFixes", "20260921183010_BootcampCrateLoot")]
        public void BootcampCrateLootMigrationGeneratesBoundedUpgradeSqlForBothProviders(
            Type contextType,
            string previous,
            string current)
        {
            using var context = CreateContext(contextType, "unused");
            CollectionAssert.Contains(context.Database.GetMigrations().ToArray(), current);
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript(previous, current));

            StringAssert.Contains(sql, "set kind = 4, reward_id = null, entity_class_id = 29877");
            StringAssert.Contains(sql, "where mission_id = 1992 and content_revision = 'deployment_11' and scenario_id = 2");
            StringAssert.Contains(sql, "and step_id = 2 and kind = 21 and dynamic_object_key = 'bootcamp-equipment-crate'");
            Assert.IsFalse(context.Database.HasPendingModelChanges());
        }

        [TestMethod]
        public void BootcampPracticeTargetsMigrationReplacesCreatureSpawnsAndKeepsTheTrainingStagesDistinct()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                var migrator = context.GetService<IMigrator>();
                migrator.Migrate("20260921183000_BootcampCrateLoot");
                migrator.Migrate();
                var triggers = context.MissionTriggerEntries.AsNoTracking()
                    .Where(trigger => trigger.MissionId == 1992 &&
                        (trigger.ObjectiveId == 3 || trigger.ObjectiveId == 8)).ToArray();
                Assert.IsTrue(triggers.All(trigger => trigger.EventKind == 13 && trigger.SubjectId == 29365));
                Assert.AreEqual(1U, triggers.Single(trigger => trigger.ObjectiveId == 3).CounterId);
                Assert.AreEqual(194U, triggers.Single(trigger => trigger.ObjectiveId == 8).CounterId);
                var steps = context.MissionScenarioStepEntries.AsNoTracking().Where(step =>
                    step.MissionId == 1992 &&
                    ((step.ScenarioId == 3 && step.StepId == 1) || (step.ScenarioId == 4 && step.StepId == 3)))
                    .ToArray();
                Assert.AreEqual(2, steps.Length);
                Assert.IsTrue(steps.All(step => step.Kind == MissionScenarioStepKind.EnableInteraction &&
                    step.EntityClassId == 29365 && step.SpawnGroupId == null));
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);

                migrator.Migrate("20260921183000_BootcampCrateLoot");
                triggers = context.MissionTriggerEntries.AsNoTracking()
                    .Where(trigger => trigger.MissionId == 1992 &&
                        (trigger.ObjectiveId == 3 || trigger.ObjectiveId == 8)).ToArray();
                Assert.AreEqual((byte)2, triggers.Single(trigger => trigger.ObjectiveId == 3).EventKind);
                Assert.AreEqual((byte)9, triggers.Single(trigger => trigger.ObjectiveId == 8).EventKind);
                migrator.Migrate();
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext), "20260921183000_BootcampCrateLoot", "20260921203000_BootcampPracticeTargets")]
        [DataRow(typeof(MySqlWorldContext), "20260921183010_BootcampCrateLoot", "20260921203010_BootcampPracticeTargets")]
        public void BootcampPracticeTargetsMigrationHasProviderParity(
            Type contextType, string previous, string current)
        {
            using var context = CreateContext(contextType, "unused");
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript(previous, current));
            StringAssert.Contains(sql, "event_kind = 13, subject_id = 29365");
            StringAssert.Contains(sql, "counter_id = case objective_id when 3 then 1 else 194 end");
            StringAssert.Contains(sql, "kind = 3, spawn_group_id = null");
            Assert.IsFalse(context.Database.HasPendingModelChanges());
        }

        [TestMethod]
        public void BootcampObjectiveIndicatorsMigrationOnlyDisablesWorldEffects()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                var migrator = context.GetService<IMigrator>();
                migrator.Migrate("20260921203000_BootcampPracticeTargets");
                var before = context.MissionIndicatorEntries.AsNoTracking()
                    .Where(entry => AllMissionIds.Contains(entry.MissionId)).ToArray();
                Assert.IsTrue(before.Length > 0 && before.All(entry => entry.Show3DEffect));

                migrator.Migrate();

                var after = context.MissionIndicatorEntries.AsNoTracking()
                    .Where(entry => AllMissionIds.Contains(entry.MissionId)).ToArray();
                Assert.IsFalse(after.Any(entry => entry.Show3DEffect));
                CollectionAssert.AreEquivalent(
                    before.Select(entry => (entry.MissionId, entry.ObjectiveId, entry.IndicatorId,
                        entry.PosX, entry.PosY, entry.PosZ, entry.Radius)).ToArray(),
                    after.Select(entry => (entry.MissionId, entry.ObjectiveId, entry.IndicatorId,
                        entry.PosX, entry.PosY, entry.PosZ, entry.Radius)).ToArray());
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);

                migrator.Migrate("20260921203000_BootcampPracticeTargets");
                Assert.IsTrue(context.MissionIndicatorEntries.AsNoTracking()
                    .Where(entry => AllMissionIds.Contains(entry.MissionId)).All(entry => entry.Show3DEffect));
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext), "20260921203000_BootcampPracticeTargets", "20260921204500_BootcampObjectiveIndicators")]
        [DataRow(typeof(MySqlWorldContext), "20260921203010_BootcampPracticeTargets", "20260921204510_BootcampObjectiveIndicators")]
        public void BootcampObjectiveIndicatorsMigrationHasProviderParity(
            Type contextType, string previous, string current)
        {
            using var context = CreateContext(contextType, "unused");
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript(previous, current));
            StringAssert.Contains(sql, "update mission_indicator set show_3d_effect = 0");
            StringAssert.Contains(sql, "mission_id in (1990, 1992, 1994, 1995, 2005)");
            Assert.IsFalse(context.Database.HasPendingModelChanges());
        }

        [TestMethod]
        public void BootcampWorldSetupMigrationGroundsDeSimoneAndEnablesAlistersRun()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                var migrator = context.GetService<IMigrator>();
                migrator.Migrate("20260921204500_BootcampObjectiveIndicators");
                var before = context.SpawnPoolEntries.AsNoTracking().Single(entry => entry.Id == 510206);
                Assert.AreEqual(114.0, before.PosY);
                Assert.AreEqual(0U, context.Set<CreatureEntry>().AsNoTracking()
                    .Single(entry => entry.Id == 510203).RunSpeed);

                migrator.Migrate();

                var after = context.SpawnPoolEntries.AsNoTracking().Single(entry => entry.Id == 510206);
                Assert.AreEqual(120.059, after.PosY, 0.001);
                Assert.AreEqual(before.PosX, after.PosX);
                Assert.AreEqual(before.PosZ, after.PosZ);
                Assert.AreEqual(before.Rotation, after.Rotation);
                Assert.AreEqual(before.Creature1Id, after.Creature1Id);
                Assert.AreEqual(7U, context.Set<CreatureEntry>().AsNoTracking()
                    .Single(entry => entry.Id == 510203).RunSpeed);
                Assert.IsFalse(Validate(LoadSnapshot(context), context).BlocksReadiness);

                migrator.Migrate("20260921204500_BootcampObjectiveIndicators");

                Assert.AreEqual(114.0, context.SpawnPoolEntries.AsNoTracking()
                    .Single(entry => entry.Id == 510206).PosY);
                Assert.AreEqual(0U, context.Set<CreatureEntry>().AsNoTracking()
                    .Single(entry => entry.Id == 510203).RunSpeed);
            });
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext), "20260921204500_BootcampObjectiveIndicators", "20260921231500_BootcampWorldSetup")]
        [DataRow(typeof(MySqlWorldContext), "20260921204510_BootcampObjectiveIndicators", "20260921231510_BootcampWorldSetup")]
        public void BootcampWorldSetupMigrationHasProviderParity(
            Type contextType, string previous, string current)
        {
            using var context = CreateContext(contextType, "unused");
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript(previous, current));
            StringAssert.Contains(sql, "update spawnpool set pos_y = 120.059 where id = 510206 and map_context_id = 1985");
            StringAssert.Contains(sql, "update creature set run_speed = 7 where id = 510203");
            Assert.IsFalse(context.Database.HasPendingModelChanges());
        }

        [TestMethod]
        public void BootcampMissionContentSeedInsertThrowsWhenGenericRowsDoNotMatchEntityColumns()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                BootcampWorldContentSeedData.Insert(
                    builder,
                    MissionSpawnGroupEntry.TableName,
                    typeof(MissionSpawnGroupEntry),
                    new[]
                    {
                        new object[] { 1994U, BootcampRevision }
                    }));

            StringAssert.Contains(ex.Message, "mission_spawn_group");
            StringAssert.Contains(ex.Message, "expected 10");
            StringAssert.Contains(ex.Message, "but received 2");
        }

        [TestMethod]
        [DataRow(typeof(SqliteWorldContext))]
        [DataRow(typeof(MySqlWorldContext))]
        public void BootcampMissionContentScriptsInsertSpawnGroupsWithPreSpawnPolicyColumnShape(
            Type contextType)
        {
            using var context = CreateContext(contextType, "unused");
            var sql = NormalizeSql(context.GetService<IMigrator>().GenerateScript());

            StringAssert.Contains(
                sql,
                "insert into mission_spawn_group (mission_id, content_revision, spawn_group_id, requirement, area_id, map_context_id, enabled, respawn_seconds, comment) values");
            StringAssert.Contains(
                sql,
                "update mission_spawn_group set spawn_policy = 1 where mission_id = 1994 and spawn_group_id in (1, 2, 3)");
        }

        [TestMethod]
        public void SqliteHistoricalBootcampMigrationReplaySeedsSpawnGroupsBeforeSpawnPolicyUpgrade()
        {
            WithDisposableSqliteWorld((context, database) =>
            {
                var migrations = context.Database.GetMigrations().ToArray();
                var bootcampIndex = Array.IndexOf(migrations, SqliteMigrationId);
                Assert.IsTrue(bootcampIndex > 0, SqliteMigrationId);

                context.GetService<IMigrator>().Migrate(migrations[bootcampIndex - 1]);
                context.GetService<IMigrator>().Migrate(SqliteMigrationId);

                using (var connection = new SqliteConnection($"Data Source={database}.db"))
                {
                    connection.Open();

                    using var tableInfo = connection.CreateCommand();
                    tableInfo.CommandText =
                        "SELECT COUNT(*) " +
                        "FROM pragma_table_info('mission_spawn_group') " +
                        "WHERE name = 'spawn_policy'";
                    Assert.AreEqual(0L, (long)tableInfo.ExecuteScalar()!);

                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "SELECT respawn_seconds " +
                        "FROM mission_spawn_group " +
                        "WHERE mission_id = 1994 AND content_revision = 'deployment_11' AND spawn_group_id IN (1, 2, 3) " +
                        "ORDER BY spawn_group_id";

                    using var reader = command.ExecuteReader();
                    var respawnSeconds = new List<uint>();
                    while (reader.Read())
                    {
                        respawnSeconds.Add(reader.GetFieldValue<uint>(0));
                    }

                    CollectionAssert.AreEqual(
                        new uint[] { 1, 1, 1 },
                        respawnSeconds.ToArray());
                }

                using var reopened = (SqliteWorldContext)CreateContext(typeof(SqliteWorldContext), database);
                reopened.Database.Migrate();

                var upgradedGroups = reopened.MissionSpawnGroupEntries
                    .Where(entry => entry.MissionId == 1994 &&
                                    entry.ContentRevision == BootcampRevision &&
                                    entry.SpawnGroupId <= 3)
                    .OrderBy(entry => entry.SpawnGroupId)
                    .ToArray();
                Assert.AreEqual(3, upgradedGroups.Length);
                Assert.IsTrue(upgradedGroups.All(entry => entry.RespawnSeconds == null));
                Assert.IsTrue(upgradedGroups.All(entry => entry.SpawnPolicy == MissionSpawnGroupPolicy.ScenarioControlled));
            });
        }

        [TestMethod]
        public void BootcampMissionContentMySqlScriptMatchesSqliteSeededRowCounts()
        {
            WithDisposableSqliteWorld((context, _) =>
            {
                // Compare seed inserts before later migrations update or remove authored rows.
                context.GetService<IMigrator>().Migrate("20260919183000_BootcampFinalReviewFixes");

                var sqliteCounts = new Dictionary<string, int>
                {
                    ["mission_content_definition"] = context.MissionContentDefinitionEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_prerequisite"] = context.MissionPrerequisiteEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_objective_definition"] = context.MissionObjectiveDefinitionEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_objective_transition"] = context.MissionObjectiveTransitionEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_trigger"] = context.MissionTriggerEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_action"] = context.MissionActionEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_reward_definition"] = context.MissionRewardDefinitionEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_reward_item"] = context.MissionRewardItemEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_indicator"] = context.MissionIndicatorEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_area"] = context.MissionAreaEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_spawn_group"] = context.MissionSpawnGroupEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_spawn"] = context.MissionSpawnEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_scenario"] = context.MissionScenarioEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_scenario_step"] = context.MissionScenarioStepEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision),
                    ["mission_evidence"] = context.MissionEvidenceEntries.Count(entry =>
                        AllMissionIds.Contains(entry.MissionId) && entry.ContentRevision == BootcampRevision)
                };

                using var mysqlContext = CreateContext(typeof(MySqlWorldContext), "unused");
                var sql = NormalizeSql(mysqlContext.GetService<IMigrator>().GenerateScript(
                    toMigration: "20260919183010_BootcampFinalReviewFixes"));

                foreach (var (table, expectedCount) in sqliteCounts)
                    Assert.AreEqual(
                        expectedCount,
                        CountBootcampRevisionInsertStatements(sql, table),
                        $"Generated MySQL script does not match SQLite seeded row count for {table}.");
            });
        }

        private static MissionContentSnapshot LoadSnapshot(SqliteWorldContext context)
        {
            var repository = new MissionContentRepository(context);
            return new Managers.MissionContentLoader().Load(repository);
        }

        private static MissionValidationReport Validate(
            MissionContentSnapshot snapshot,
            SqliteWorldContext context)
        {
            using var unit = new RepositoryBackedWorldUnitOfWork(context);
            return new Managers.MissionContentValidator().Validate(snapshot, unit);
        }

        private static void AssertOnMesh(
            NavMeshQuery nav,
            double x,
            double y,
            double z,
            string label)
        {
            Assert.IsTrue(
                nav.IsOnMesh(new Vector3((float)x, (float)y, (float)z)),
                $"{label} is off navmesh at ({x}, {y}, {z}).");
        }

        private static void AssertObjectiveStates(
            MissionContentSnapshot snapshot,
            uint missionId,
            params (uint ObjectiveId, MissionObjectiveState State)[] expected)
        {
            var actual = snapshot.Definitions[missionId].Mission.Objectives.Values
                .OrderBy(objective => objective.Ordinal)
                .Select(objective => (objective.ObjectiveId, objective.InitialState!.Value))
                .ToArray();
            CollectionAssert.AreEqual(expected, actual);
        }

        private static NavMeshQuery ResolveNav(
            uint mapContextId,
            NavMeshQuery bootcamp,
            NavMeshQuery wilderness) =>
            mapContextId == 1220 ? wilderness : bootcamp;

        private static string NormalizeSql(string sql)
        {
            sql ??= string.Empty;
            sql = sql.ToLowerInvariant()
                .Replace("`", string.Empty)
                .Replace("\"", string.Empty)
                .Replace("[", string.Empty)
                .Replace("]", string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ");
            return System.Text.RegularExpressions.Regex.Replace(sql, "\\s+", " ").Trim();
        }

        private static int CountBootcampRevisionInsertStatements(string sql, string tableName) =>
            System.Text.RegularExpressions.Regex.Matches(
                    sql.ToLowerInvariant(),
                    $@"insert into {System.Text.RegularExpressions.Regex.Escape(tableName.ToLowerInvariant())} \([^)]*\) values \((1990|1992|1994|1995|2005), 'deployment_11',")
                .Count;

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Rasa.NET.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static void WithDisposableSqliteWorld(Action<SqliteWorldContext, string> body)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "TestDatabases",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var database = Path.Combine(path, "database");
                using var context = (SqliteWorldContext)CreateContext(typeof(SqliteWorldContext), database);
                body(context, database);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
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
            return (RasaDbContextBase)Activator.CreateInstance(
                contextType,
                options,
                configuration,
                modifier)!;
        }

        private sealed class RepositoryBackedWorldUnitOfWork : Repositories.World.IWorldUnitOfWork
        {
            public RepositoryBackedWorldUnitOfWork(SqliteWorldContext context)
            {
                Actions = null;
                Equipment = new EquipmentRepository(context);
                Creatures = new CreatureRepository(context);
                EntityClasses = new EntityClassRepository(context);
                Footlockers = null;
                Logoses = null;
                MapInfos = new MapInfoRepository(context);
                MapLinks = null;
                Kraftwerks = null;
                MapRegions = null;
                MapMarkers = null;
                Recipes = null;
                NpcMissions = null;
                NpcMissionRewards = null;
                MissionContent = new MissionContentRepository(context);
                NpcPackages = new NpcPackageRepository(context);
                RandomNames = null;
                Spawnpools = null;
                Teleporters = new TeleporterRepository(context);
            }

            public IActionRepository Actions { get; }
            public IEquipmentRepository Equipment { get; }
            public ICreatureRepository Creatures { get; }
            public IEntityClassRepository EntityClasses { get; }
            public IFootlockerRepository Footlockers { get; }
            public ILogosRepository Logoses { get; }
            public IMapInfoRepository MapInfos { get; }
            public IMapLinkRepository MapLinks { get; }
            public IKraftwerksRepository Kraftwerks { get; }
            public IMapRegionRepository MapRegions { get; }
            public IMapMarkerRepository MapMarkers { get; }
            public IRecipeRepository Recipes { get; }
            public INpcMissionRepository NpcMissions { get; }
            public INpcMissionRewardRepository NpcMissionRewards { get; }
            public IMissionContentRepository MissionContent { get; }
            public INpcPackageRepository NpcPackages { get; }
            public IPlayerRandomNameRepository RandomNames { get; }
            public ISpawnpoolRepository Spawnpools { get; }
            public ITeleporterRepository Teleporters { get; }
            public void Complete() { }
            public void Reject() { }
            public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginTransaction() =>
                throw new NotSupportedException();
            public void Dispose() { }
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
