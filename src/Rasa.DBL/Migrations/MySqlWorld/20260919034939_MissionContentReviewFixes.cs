using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class MissionContentReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_mission_trigger_mission_id_content_revision_area_id",
                table: "mission_trigger",
                columns: new[] { "mission_id", "content_revision", "area_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_trigger_mission_id_content_revision_related_objectiv~",
                table: "mission_trigger",
                columns: new[] { "mission_id", "content_revision", "related_objective_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger",
                sql: "(kind IN (1, 2, 3, 4, 5)) AND (kind <> 1 OR (npc_package_id IS NOT NULL AND player_flag_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 2 OR (event_kind IS NOT NULL AND subject_id IS NOT NULL AND counter_id IS NOT NULL AND initial_value IS NOT NULL AND target_value IS NOT NULL AND source_spawn_resolved IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL)) AND (kind <> 3 OR (related_objective_id IS NOT NULL AND related_state IS NOT NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 4 OR (area_id IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND duration_seconds IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL)) AND (kind <> 5 OR (duration_seconds IS NOT NULL AND related_objective_id IS NULL AND related_state IS NULL AND event_kind IS NULL AND subject_id IS NULL AND counter_id IS NULL AND initial_value IS NULL AND target_value IS NULL AND area_id IS NULL AND npc_package_id IS NULL AND player_flag_id IS NULL AND source_spawn_resolved IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_mission_spawn_group_mission_id_content_revision_area_id",
                table: "mission_spawn_group",
                columns: new[] { "mission_id", "content_revision", "area_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_evidence_source_location",
                table: "mission_evidence",
                sql: "source_uri IS NOT NULL OR local_client_path IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_mission_action_mission_id_content_revision_objective_id_indi~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "objective_id", "indicator_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_action_mission_id_content_revision_reward_id",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "reward_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_action_mission_id_content_revision_scenario_id",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "scenario_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_action_mission_id_content_revision_spawn_group_id",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "spawn_group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_action_mission_id_content_revision_target_objective_~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "target_objective_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_action_kind_parameter_set",
                table: "mission_action",
                sql: "(kind IN (1, 2, 3, 4, 5, 6, 7, 8)) AND (kind <> 1 OR (target_objective_id IS NOT NULL AND objective_state IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 2 OR (target_objective_id IS NOT NULL AND objective_state IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 3 OR (target_objective_id IS NOT NULL AND objective_state IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 4 OR (reward_id IS NOT NULL AND target_objective_id IS NULL AND objective_state IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 5 OR (scenario_id IS NOT NULL AND target_objective_id IS NULL AND objective_state IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 6 OR (spawn_group_id IS NOT NULL AND target_objective_id IS NULL AND objective_state IS NULL AND reward_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 7 OR (indicator_id IS NOT NULL AND target_objective_id IS NULL AND objective_state IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND player_flag_id IS NULL AND player_flag_value IS NULL)) AND (kind <> 8 OR (player_flag_id IS NOT NULL AND player_flag_value IS NOT NULL AND target_objective_id IS NULL AND objective_state IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND scenario_id IS NULL AND indicator_id IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_mission_action_mission_indicator_mission_id_content_revision~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "objective_id", "indicator_id" },
                principalTable: "mission_indicator",
                principalColumns: new[] { "mission_id", "content_revision", "objective_id", "indicator_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_action_mission_objective_definition_mission_id_conte~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "target_objective_id" },
                principalTable: "mission_objective_definition",
                principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_action_mission_reward_definition_mission_id_content_~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "reward_id" },
                principalTable: "mission_reward_definition",
                principalColumns: new[] { "mission_id", "content_revision", "reward_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_action_mission_scenario_mission_id_content_revision_~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "scenario_id" },
                principalTable: "mission_scenario",
                principalColumns: new[] { "mission_id", "content_revision", "scenario_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_action_mission_spawn_group_mission_id_content_revisi~",
                table: "mission_action",
                columns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                principalTable: "mission_spawn_group",
                principalColumns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_spawn_group_mission_area_mission_id_content_revision~",
                table: "mission_spawn_group",
                columns: new[] { "mission_id", "content_revision", "area_id" },
                principalTable: "mission_area",
                principalColumns: new[] { "mission_id", "content_revision", "area_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_trigger_mission_area_mission_id_content_revision_are~",
                table: "mission_trigger",
                columns: new[] { "mission_id", "content_revision", "area_id" },
                principalTable: "mission_area",
                principalColumns: new[] { "mission_id", "content_revision", "area_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_trigger_mission_objective_definition_mission_id_cont~",
                table: "mission_trigger",
                columns: new[] { "mission_id", "content_revision", "related_objective_id" },
                principalTable: "mission_objective_definition",
                principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mission_action_mission_indicator_mission_id_content_revision~",
                table: "mission_action");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_action_mission_objective_definition_mission_id_conte~",
                table: "mission_action");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_action_mission_reward_definition_mission_id_content_~",
                table: "mission_action");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_action_mission_scenario_mission_id_content_revision_~",
                table: "mission_action");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_action_mission_spawn_group_mission_id_content_revisi~",
                table: "mission_action");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_spawn_group_mission_area_mission_id_content_revision~",
                table: "mission_spawn_group");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_trigger_mission_area_mission_id_content_revision_are~",
                table: "mission_trigger");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_trigger_mission_objective_definition_mission_id_cont~",
                table: "mission_trigger");

            migrationBuilder.DropIndex(
                name: "IX_mission_trigger_mission_id_content_revision_area_id",
                table: "mission_trigger");

            migrationBuilder.DropIndex(
                name: "IX_mission_trigger_mission_id_content_revision_related_objectiv~",
                table: "mission_trigger");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_trigger_kind_parameter_set",
                table: "mission_trigger");

            migrationBuilder.DropIndex(
                name: "IX_mission_spawn_group_mission_id_content_revision_area_id",
                table: "mission_spawn_group");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_evidence_source_location",
                table: "mission_evidence");

            migrationBuilder.DropIndex(
                name: "IX_mission_action_mission_id_content_revision_objective_id_indi~",
                table: "mission_action");

            migrationBuilder.DropIndex(
                name: "IX_mission_action_mission_id_content_revision_reward_id",
                table: "mission_action");

            migrationBuilder.DropIndex(
                name: "IX_mission_action_mission_id_content_revision_scenario_id",
                table: "mission_action");

            migrationBuilder.DropIndex(
                name: "IX_mission_action_mission_id_content_revision_spawn_group_id",
                table: "mission_action");

            migrationBuilder.DropIndex(
                name: "IX_mission_action_mission_id_content_revision_target_objective_~",
                table: "mission_action");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_action_kind_parameter_set",
                table: "mission_action");
        }
    }
}
