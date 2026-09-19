using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionContentCounterTextAndProgressShapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger");

            migrationBuilder.AddColumn<uint>(
                name: "client_counter_0_text_id",
                table: "mission_objective_definition",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "client_counter_1_text_id",
                table: "mission_objective_definition",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "client_counter_2_text_id",
                table: "mission_objective_definition",
                type: "int(11)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger",
                sql: "(kind IN (1, 2, 3, 4, 5)) AND (kind <> 1 OR (npc_package_id IS NOT NULL AND player_flag_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 2 OR (event_kind IS NOT NULL AND subject_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL)) AND (kind <> 3 OR (related_objective_id IS NOT NULL AND related_state IS NOT NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 4 OR (area_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 5 OR (duration_seconds IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger");

            migrationBuilder.DropColumn(
                name: "client_counter_0_text_id",
                table: "mission_objective_definition");

            migrationBuilder.DropColumn(
                name: "client_counter_1_text_id",
                table: "mission_objective_definition");

            migrationBuilder.DropColumn(
                name: "client_counter_2_text_id",
                table: "mission_objective_definition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger",
                sql: "(kind IN (1, 2, 3, 4, 5)) AND (kind <> 1 OR (npc_package_id IS NOT NULL AND player_flag_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 2 OR (event_kind IS NOT NULL AND subject_id IS NOT NULL AND counter_id IS NOT NULL AND initial_value IS NOT NULL AND target_value IS NOT NULL AND source_spawn_resolved IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL)) AND (kind <> 3 OR (related_objective_id IS NOT NULL AND related_state IS NOT NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 4 OR (area_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 5 OR (duration_seconds IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL))");
        }
    }
}
