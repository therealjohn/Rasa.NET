using System;
using System.Linq;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Services.Preloader
{
    using Structures.World;

    internal static class BootcampReinforcementsContent
    {
        private const string Mission = "mission_id = 1995 and content_revision = 'deployment_11'";
        private const string Finales = "mission_id in (1995, 2005) and content_revision = 'deployment_11'";

        internal static void Up(MigrationBuilder migration)
        {
            ReplaceWreckClass(migration, 24911, 24586);
            SetFinalePlacements(migration, corrected: true);
            // Area discovery reveals the survivor; only the conversation completes client objective 2.
            migration.Sql($"update mission_objective_transition set to_state = 1 where {Mission} and objective_id = 2 and transition_id = 1;");
            migration.Sql($"delete from mission_action where {Mission} and objective_id = 2 and kind <> 5;");
            migration.Sql(
                "insert into mission_objective_transition " +
                "(mission_id, content_revision, objective_id, transition_id, requirement, sequence, from_state, to_state, comment) " +
                $"select mission_id, content_revision, 2, 2, requirement, 2, from_state, to_state, comment from mission_objective_transition where {Mission} and objective_id = 10;");
            migration.Sql($"update mission_trigger set objective_id = 2, transition_id = 2 where {Mission} and objective_id = 10;");
            migration.Sql($"update mission_action set objective_id = 2, transition_id = 2, target_objective_id = case when target_objective_id = 10 then 2 else target_objective_id end where {Mission} and objective_id = 10;");
            migration.Sql($"delete from mission_objective_transition where {Mission} and objective_id = 10;");
            migration.Sql($"delete from mission_objective_definition where {Mission} and objective_id = 10;");
            migration.Sql($"update mission_objective_definition set ordinal = ordinal - 1 where {Mission} and ordinal > 2;");
            migration.Sql($"update mission_evidence set reconstruction_note = 'Entering the missing-team search area reveals survivor package 2584 while objective 2 remains incomplete until dialogue.' where {Mission} and evidence_id = 2;");
            migration.Sql($"update mission_evidence set owner_id = 2, reconstruction_note = 'Client objective 2 includes locating the missing team and speaking to wounded survivor package 2584; no client objective 10 exists.' where {Mission} and evidence_id = 3;");
        }

        internal static void Down(MigrationBuilder migration)
        {
            ReplaceWreckClass(migration, 24586, 24911);
            SetFinalePlacements(migration, corrected: false);
            migration.Sql($"delete from mission_action where {Mission} and objective_id = 2;");
            migration.Sql($"delete from mission_trigger where {Mission} and objective_id = 2;");
            migration.Sql($"delete from mission_objective_transition where {Mission} and objective_id = 2;");
            migration.Sql($"update mission_objective_definition set ordinal = ordinal + 1 where {Mission} and objective_id in (3, 1, 4);");
            BootcampWorldContentSeedData.Insert(migration, MissionObjectiveDefinitionEntry.TableName,
                typeof(MissionObjectiveDefinitionEntry),
                BootcampWorldContentSeedData.MissionObjectives().Where(row => (uint)row[0] == 1995 && (uint)row[2] == 10));
            BootcampWorldContentSeedData.Insert(migration, MissionObjectiveTransitionEntry.TableName,
                typeof(MissionObjectiveTransitionEntry),
                BootcampWorldContentSeedData.MissionTransitions().Where(IsSearchOrSurvivor));
            BootcampWorldContentSeedData.Insert(migration, MissionTriggerEntry.TableName,
                typeof(MissionTriggerEntry),
                BootcampWorldContentSeedData.MissionTriggers().Where(IsSearchOrSurvivor));
            BootcampWorldContentSeedData.Insert(migration, MissionActionEntry.TableName,
                typeof(MissionActionEntry),
                BootcampWorldContentSeedData.MissionActions().Where(IsSearchOrSurvivor));
            migration.Sql($"delete from mission_evidence where {Mission} and evidence_id in (2, 3);");
            BootcampWorldContentSeedData.Insert(migration, MissionEvidenceEntry.TableName, typeof(MissionEvidenceEntry),
                BootcampWorldContentSeedData.MissionEvidence().Where(row =>
                    (uint)row[0] == 1995 && ((uint)row[2] == 2 || (uint)row[2] == 3)));
        }

        private static bool IsSearchOrSurvivor(object[] row) =>
            (uint)row[0] == 1995 && ((uint)row[2] == 2 || (uint)row[2] == 10);

        private static void ReplaceWreckClass(MigrationBuilder migration, uint previous, uint current)
        {
            migration.Sql($"update mission_scenario_step set entity_class_id = {current} where {Finales} and entity_class_id = {previous};");
            migration.Sql($"update mission_trigger set subject_id = {current} where {Finales} and objective_id = 1 and subject_id = {previous};");
        }

        private static void SetFinalePlacements(MigrationBuilder migration, bool corrected)
        {
            var survivor = corrected ? (-101.2, 86.40231, 70.8) : (-104.6, 86.1, 70.5);
            var conradGround = corrected ? (-102.4, 86.10937, 66.8) : (-102.4, 85.69, 66.8);
            var conradVisual = corrected ? (-102.4, 86.20677, 66.8) : conradGround;
            // Keep the confirmed origin: lifting by render minY (-4.390) puts Damage1 beyond native use range.
            var wreck = corrected ? (-225.0, 101.12099, -71.0) : (-225.35, 99.60, -70.52);
            var van = (-223.0, corrected ? 101.269646 : 99.60, -76.0);
            var handoff = corrected ? van : (-225.35, 99.60, -70.52);

            Place(migration, "mission_spawn", $"{Mission} and spawn_group_id = 1 and spawn_id = 1", survivor);
            Place(migration, "mission_spawn", $"{Finales} and creature_id = 39", (-218.0, corrected ? 101.08475 : 99.60, -78.0));
            Place(migration, "mission_spawn", $"{Finales} and creature_id = 50", (-221.0, corrected ? 101.2538 : 99.60, -74.0));
            Place(migration, "mission_spawn", $"{Finales} and creature_id = 510209", van);
            Place(migration, "mission_scenario_step", $"{Mission} and scenario_id = 7 and step_id = 2", conradVisual);
            Place(migration, "mission_scenario_step", $"{Mission} and scenario_id = 7 and step_id = 3", wreck);
            Place(migration, "mission_indicator", $"{Mission} and objective_id = 2 and indicator_id = 435", survivor);
            Place(migration, "mission_indicator", $"{Mission} and objective_id = 3 and indicator_id = 436", conradGround);
            Place(migration, "mission_indicator", $"{Finales} and objective_id = 1 and indicator_id = 432", wreck);
            Place(migration, "mission_indicator", $"{Finales} and objective_id = 4 and indicator_id = 438", handoff);
            migration.Sql($"update mission_area set pos_y = {(corrected ? "101.1059" : "99.60")} where {Finales} and area_id = 438;");
        }

        private static void Place(
            MigrationBuilder migration, string table, string key, (double X, double Y, double Z) position) =>
            migration.Sql(FormattableString.Invariant(
                $"update {table} set pos_x = {position.X:R}, pos_y = {position.Y:R}, pos_z = {position.Z:R} where {key};"));

        internal static void UpCharacters(MigrationBuilder migration)
        {
            // Unknown layouts stay intact for the runtime's explicit compatibility error.
            migration.Sql(
                "create temporary table bootcamp_reinforcements_legacy as " +
                "select legacy.character_id, case when legacy.objective_state = 4 then " +
                "case when search.objective_state = 2 then 1 else search.objective_state end " +
                "else legacy.objective_state end as mapped_state " +
                "from character_mission_objective legacy join character_mission_objective search " +
                "on search.character_id = legacy.character_id and search.mission_id = 1995 and search.objective_id = 2 " +
                "join character_mission mission on mission.character_id = legacy.character_id and mission.mission_id = 1995 " +
                "where legacy.mission_id = 1995 and legacy.objective_id = 10 " +
                "and mission.mission_state in (0, 1, 2, 4) " +
                "and (mission.mission_state <> 0 or mission.completeable = case when not exists " +
                "(select 1 from character_mission_objective incomplete where incomplete.character_id = legacy.character_id " +
                "and incomplete.mission_id = 1995 and incomplete.objective_state <> 2) then 1 else 0 end) " +
                "and (legacy.objective_state = 4 or search.objective_state = 2) " +
                "and (select count(*) from character_mission_objective all_objectives " +
                "where all_objectives.character_id = legacy.character_id and all_objectives.mission_id = 1995) = 5 " +
                "and not exists (select 1 from character_mission_objective invalid " +
                "where invalid.character_id = legacy.character_id and invalid.mission_id = 1995 " +
                "and (invalid.objective_id not in (2, 10, 3, 1, 4) or invalid.objective_state not between 1 and 4)) " +
                "and not exists (select 1 from character_mission_objective_counter counter_row " +
                "where counter_row.character_id = legacy.character_id and counter_row.mission_id = 1995) " +
                "and not exists (select 1 from character_mission_objective_item_counter counter_row " +
                "where counter_row.character_id = legacy.character_id and counter_row.mission_id = 1995);");
            migration.Sql(
                "update character_mission_objective set objective_state = coalesce(" +
                "(select mapped_state from bootcamp_reinforcements_legacy where character_id = character_mission_objective.character_id), objective_state) " +
                "where mission_id = 1995 and objective_id = 2;");
            migration.Sql(
                "delete from character_mission_objective where mission_id = 1995 and objective_id = 10 " +
                "and character_id in (select character_id from bootcamp_reinforcements_legacy);");
            migration.Sql(migration.ActiveProvider == "Pomelo.EntityFrameworkCore.MySql"
                ? "drop temporary table bootcamp_reinforcements_legacy;"
                : "drop table bootcamp_reinforcements_legacy;");
        }
    }
}
