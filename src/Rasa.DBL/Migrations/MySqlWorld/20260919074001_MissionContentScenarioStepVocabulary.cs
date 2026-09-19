using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class MissionContentScenarioStepVocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "ability_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ability_slot",
                table: "mission_scenario_step",
                type: "tinyint(3) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "account_skip_entitlement",
                table: "mission_scenario_step",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attempt_key",
                table: "mission_scenario_step",
                type: "varchar(64)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "audio_set_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "delay_milliseconds",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "entity_class_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "map_context_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "orientation",
                table: "mission_scenario_step",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pos_x",
                table: "mission_scenario_step",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pos_y",
                table: "mission_scenario_step",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pos_z",
                table: "mission_scenario_step",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "qualification_key",
                table: "mission_scenario_step",
                type: "tinyint(3) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "qualification_value",
                table: "mission_scenario_step",
                type: "tinyint(3) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "reward_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "scenario_event_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "skill_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "skill_level",
                table: "mission_scenario_step",
                type: "tinyint(3) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "spawn_group_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "spawn_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "target_objective_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "target_scenario_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "tutorial_id",
                table: "mission_scenario_step",
                type: "int(11) unsigned",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_reward_id",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "reward_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_spawn_grou~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "spawn_group_id", "spawn_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_target_obj~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "target_objective_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_target_sce~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "target_scenario_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_scenario_step_kind_parameter_set",
                table: "mission_scenario_step",
                sql: "(kind IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19)) AND (kind <> 1 OR (spawn_group_id IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 2 OR (spawn_group_id IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 3 OR ((((entity_class_id IS NOT NULL AND spawn_group_id IS NULL AND spawn_id IS NULL) OR (entity_class_id IS NULL AND spawn_group_id IS NOT NULL AND spawn_id IS NOT NULL)) AND target_objective_id IS NULL AND reward_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL))) AND (kind <> 4 OR ((((entity_class_id IS NOT NULL AND spawn_group_id IS NULL AND spawn_id IS NULL) OR (entity_class_id IS NULL AND spawn_group_id IS NOT NULL AND spawn_id IS NOT NULL)) AND target_objective_id IS NULL AND reward_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL))) AND (kind <> 5 OR (target_objective_id IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 6 OR (target_objective_id IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 7 OR (target_objective_id IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 8 OR (target_objective_id IS NOT NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 9 OR (delay_milliseconds IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 10 OR (target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 11 OR (reward_id IS NOT NULL AND target_objective_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 12 OR (skill_id IS NOT NULL AND ability_id IS NOT NULL AND skill_level IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 13 OR (tutorial_id IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 14 OR (target_scenario_id IS NOT NULL AND delay_milliseconds IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 15 OR ((((target_scenario_id IS NOT NULL AND attempt_key IS NULL) OR (target_scenario_id IS NULL AND attempt_key IS NOT NULL AND attempt_key <> '')) AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL))) AND (kind <> 16 OR (scenario_event_id IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 17 OR (map_context_id IS NOT NULL AND pos_x IS NOT NULL AND pos_y IS NOT NULL AND pos_z IS NOT NULL AND orientation IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND qualification_key IS NULL AND qualification_value IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 18 OR (qualification_key IS NOT NULL AND qualification_value IS NOT NULL AND qualification_key IN (1) AND qualification_value IN (1) AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND account_skip_entitlement IS NULL)) AND (kind <> 19 OR (account_skip_entitlement IS NOT NULL AND target_objective_id IS NULL AND reward_id IS NULL AND spawn_group_id IS NULL AND spawn_id IS NULL AND entity_class_id IS NULL AND target_scenario_id IS NULL AND delay_milliseconds IS NULL AND skill_id IS NULL AND ability_id IS NULL AND skill_level IS NULL AND ability_slot IS NULL AND tutorial_id IS NULL AND audio_set_id IS NULL AND attempt_key IS NULL AND scenario_event_id IS NULL AND map_context_id IS NULL AND pos_x IS NULL AND pos_y IS NULL AND pos_z IS NULL AND orientation IS NULL AND qualification_key IS NULL AND qualification_value IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_scenario_step_numeric_bounds",
                table: "mission_scenario_step",
                sql: "(target_objective_id IS NULL OR target_objective_id > 0) AND (reward_id IS NULL OR reward_id > 0) AND (spawn_group_id IS NULL OR spawn_group_id > 0) AND (spawn_id IS NULL OR spawn_id > 0) AND (entity_class_id IS NULL OR entity_class_id > 0) AND (target_scenario_id IS NULL OR target_scenario_id > 0) AND (delay_milliseconds IS NULL OR (delay_milliseconds >= 1 AND delay_milliseconds <= 86400000)) AND (skill_id IS NULL OR skill_id > 0) AND (ability_id IS NULL OR (ability_id >= 1 AND ability_id <= 2147483647)) AND (skill_level IS NULL OR (skill_level >= 1 AND skill_level <= 5)) AND (ability_slot IS NULL OR ability_slot <= 24) AND (tutorial_id IS NULL OR tutorial_id > 0) AND (audio_set_id IS NULL OR audio_set_id > 0) AND (attempt_key IS NULL OR attempt_key <> '') AND (scenario_event_id IS NULL OR scenario_event_id > 0) AND (map_context_id IS NULL OR map_context_id > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_mission_scenario_step_mission_objective_definition_mission_i~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "target_objective_id" },
                principalTable: "mission_objective_definition",
                principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_scenario_step_mission_reward_definition_mission_id_c~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "reward_id" },
                principalTable: "mission_reward_definition",
                principalColumns: new[] { "mission_id", "content_revision", "reward_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_scenario_step_mission_scenario_mission_id_content_r~1",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "target_scenario_id" },
                principalTable: "mission_scenario",
                principalColumns: new[] { "mission_id", "content_revision", "scenario_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_scenario_step_mission_spawn_group_mission_id_content~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                principalTable: "mission_spawn_group",
                principalColumns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mission_scenario_step_mission_spawn_mission_id_content_revis~",
                table: "mission_scenario_step",
                columns: new[] { "mission_id", "content_revision", "spawn_group_id", "spawn_id" },
                principalTable: "mission_spawn",
                principalColumns: new[] { "mission_id", "content_revision", "spawn_group_id", "spawn_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mission_scenario_step_mission_objective_definition_mission_i~",
                table: "mission_scenario_step");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_scenario_step_mission_reward_definition_mission_id_c~",
                table: "mission_scenario_step");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_scenario_step_mission_scenario_mission_id_content_r~1",
                table: "mission_scenario_step");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_scenario_step_mission_spawn_group_mission_id_content~",
                table: "mission_scenario_step");

            migrationBuilder.DropForeignKey(
                name: "FK_mission_scenario_step_mission_spawn_mission_id_content_revis~",
                table: "mission_scenario_step");

            migrationBuilder.DropIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_reward_id",
                table: "mission_scenario_step");

            migrationBuilder.DropIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_spawn_grou~",
                table: "mission_scenario_step");

            migrationBuilder.DropIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_target_obj~",
                table: "mission_scenario_step");

            migrationBuilder.DropIndex(
                name: "IX_mission_scenario_step_mission_id_content_revision_target_sce~",
                table: "mission_scenario_step");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_scenario_step_kind_parameter_set",
                table: "mission_scenario_step");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_scenario_step_numeric_bounds",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "ability_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "ability_slot",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "account_skip_entitlement",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "attempt_key",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "audio_set_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "delay_milliseconds",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "entity_class_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "map_context_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "orientation",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "pos_x",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "pos_y",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "pos_z",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "qualification_key",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "qualification_value",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "reward_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "scenario_event_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "skill_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "skill_level",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "spawn_group_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "spawn_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "target_objective_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "target_scenario_id",
                table: "mission_scenario_step");

            migrationBuilder.DropColumn(
                name: "tutorial_id",
                table: "mission_scenario_step");
        }
    }
}
