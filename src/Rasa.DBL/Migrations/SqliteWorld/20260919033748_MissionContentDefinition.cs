using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionContentDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_content_definition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    client_name_text_id = table.Column<uint>(type: "int(11)", nullable: false),
                    giver_id = table.Column<uint>(type: "int(11)", nullable: false),
                    receiver_id = table.Column<uint>(type: "int(11)", nullable: false),
                    level = table.Column<uint>(type: "int(11)", nullable: false),
                    group_type = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    category_id = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    shareable = table.Column<bool>(type: "INTEGER", nullable: false),
                    radio_completeable = table.Column<bool>(type: "INTEGER", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_content_definition", x => new { x.mission_id, x.content_revision });
                });

            migrationBuilder.CreateTable(
                name: "mission_area",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    area_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    map_context_id = table.Column<uint>(type: "int(11)", nullable: false),
                    shape = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    pos_x = table.Column<double>(type: "REAL", nullable: false),
                    pos_y = table.Column<double>(type: "REAL", nullable: false),
                    pos_z = table.Column<double>(type: "REAL", nullable: false),
                    radius = table.Column<double>(type: "REAL", nullable: true),
                    extent_x = table.Column<double>(type: "REAL", nullable: true),
                    extent_y = table.Column<double>(type: "REAL", nullable: true),
                    extent_z = table.Column<double>(type: "REAL", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_area", x => new { x.mission_id, x.content_revision, x.area_id });
                    table.ForeignKey(
                        name: "FK_mission_area_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_evidence",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    evidence_id = table.Column<uint>(type: "int(11)", nullable: false),
                    owner_kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    owner_id = table.Column<uint>(type: "int(11)", nullable: false),
                    source_kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    source_uri = table.Column<string>(type: "varchar(256)", nullable: true),
                    local_client_path = table.Column<string>(type: "varchar(256)", nullable: true),
                    confidence = table.Column<double>(type: "double", nullable: false),
                    reconstruction_note = table.Column<string>(type: "varchar(256)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_evidence", x => new { x.mission_id, x.content_revision, x.evidence_id });
                    table.ForeignKey(
                        name: "FK_mission_evidence_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_objective_definition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    objective_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    client_name_text_id = table.Column<uint>(type: "int(11)", nullable: false),
                    client_body_text_id = table.Column<uint>(type: "int(11)", nullable: false),
                    ordinal = table.Column<uint>(type: "int(11)", nullable: false),
                    initial_state = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    is_required = table.Column<bool>(type: "INTEGER", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_objective_definition", x => new { x.mission_id, x.content_revision, x.objective_id });
                    table.ForeignKey(
                        name: "FK_mission_objective_definition_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_prerequisite",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    prerequisite_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    required_mission_id = table.Column<uint>(type: "int(11)", nullable: true),
                    required_mission_state = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    required_level = table.Column<uint>(type: "int(11)", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11)", nullable: true),
                    player_flag_value = table.Column<uint>(type: "int(11)", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_prerequisite", x => new { x.mission_id, x.content_revision, x.prerequisite_id });
                    table.ForeignKey(
                        name: "FK_mission_prerequisite_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_reward_definition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    reward_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    experience = table.Column<uint>(type: "int(11)", nullable: false),
                    credits = table.Column<uint>(type: "int(11)", nullable: false),
                    prestige = table.Column<uint>(type: "int(11)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_reward_definition", x => new { x.mission_id, x.content_revision, x.reward_id });
                    table.ForeignKey(
                        name: "FK_mission_reward_definition_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_scenario",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    scenario_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scenario", x => new { x.mission_id, x.content_revision, x.scenario_id });
                    table.ForeignKey(
                        name: "FK_mission_scenario_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_spawn_group",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    spawn_group_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    area_id = table.Column<uint>(type: "int(11)", nullable: true),
                    map_context_id = table.Column<uint>(type: "int(11)", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    respawn_seconds = table.Column<uint>(type: "int(11)", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_spawn_group", x => new { x.mission_id, x.content_revision, x.spawn_group_id });
                    table.ForeignKey(
                        name: "FK_mission_spawn_group_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_indicator",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    objective_id = table.Column<uint>(type: "int(11)", nullable: false),
                    indicator_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    pos_x = table.Column<double>(type: "REAL", nullable: false),
                    pos_y = table.Column<double>(type: "REAL", nullable: false),
                    pos_z = table.Column<double>(type: "REAL", nullable: false),
                    radius = table.Column<double>(type: "REAL", nullable: false),
                    show_3d_effect = table.Column<bool>(type: "INTEGER", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_indicator", x => new { x.mission_id, x.content_revision, x.objective_id, x.indicator_id });
                    table.ForeignKey(
                        name: "FK_mission_indicator_mission_objective_definition_mission_id_content_revision_objective_id",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id },
                        principalTable: "mission_objective_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_objective_transition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    objective_id = table.Column<uint>(type: "int(11)", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    sequence = table.Column<uint>(type: "int(11)", nullable: false),
                    from_state = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    to_state = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_objective_transition", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id });
                    table.ForeignKey(
                        name: "FK_mission_objective_transition_mission_objective_definition_mission_id_content_revision_objective_id",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id },
                        principalTable: "mission_objective_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_reward_item",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    reward_id = table.Column<uint>(type: "int(11)", nullable: false),
                    item_id = table.Column<uint>(type: "int(11)", nullable: false),
                    item_template_id = table.Column<uint>(type: "int(11)", nullable: false),
                    quantity = table.Column<uint>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_reward_item", x => new { x.mission_id, x.content_revision, x.reward_id, x.item_id });
                    table.ForeignKey(
                        name: "FK_mission_reward_item_mission_reward_definition_mission_id_content_revision_reward_id",
                        columns: x => new { x.mission_id, x.content_revision, x.reward_id },
                        principalTable: "mission_reward_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "reward_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_scenario_step",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    scenario_id = table.Column<uint>(type: "int(11)", nullable: false),
                    step_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    sequence = table.Column<uint>(type: "int(11)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scenario_step", x => new { x.mission_id, x.content_revision, x.scenario_id, x.step_id });
                    table.ForeignKey(
                        name: "FK_mission_scenario_step_mission_scenario_mission_id_content_revision_scenario_id",
                        columns: x => new { x.mission_id, x.content_revision, x.scenario_id },
                        principalTable: "mission_scenario",
                        principalColumns: new[] { "mission_id", "content_revision", "scenario_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_spawn",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    spawn_group_id = table.Column<uint>(type: "int(11)", nullable: false),
                    spawn_id = table.Column<uint>(type: "int(11)", nullable: false),
                    creature_id = table.Column<uint>(type: "int(11)", nullable: false),
                    pos_x = table.Column<double>(type: "REAL", nullable: false),
                    pos_y = table.Column<double>(type: "REAL", nullable: false),
                    pos_z = table.Column<double>(type: "REAL", nullable: false),
                    rotation = table.Column<double>(type: "REAL", nullable: false),
                    quantity = table.Column<uint>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_spawn", x => new { x.mission_id, x.content_revision, x.spawn_group_id, x.spawn_id });
                    table.ForeignKey(
                        name: "FK_mission_spawn_mission_spawn_group_mission_id_content_revision_spawn_group_id",
                        columns: x => new { x.mission_id, x.content_revision, x.spawn_group_id },
                        principalTable: "mission_spawn_group",
                        principalColumns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mission_action",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    objective_id = table.Column<uint>(type: "int(11)", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11)", nullable: false),
                    action_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    sequence = table.Column<uint>(type: "int(11)", nullable: false),
                    target_objective_id = table.Column<uint>(type: "int(11)", nullable: true),
                    objective_state = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    reward_id = table.Column<uint>(type: "int(11)", nullable: true),
                    spawn_group_id = table.Column<uint>(type: "int(11)", nullable: true),
                    scenario_id = table.Column<uint>(type: "int(11)", nullable: true),
                    indicator_id = table.Column<uint>(type: "int(11)", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11)", nullable: true),
                    player_flag_value = table.Column<uint>(type: "int(11)", nullable: true),
                    npc_package_id = table.Column<uint>(type: "int(11)", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_action", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id, x.action_id });
                    table.ForeignKey(
                        name: "FK_mission_action_mission_objective_transition_mission_id_content_revision_objective_id_transition_id",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id },
                        principalTable: "mission_objective_transition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id", "transition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mission_trigger",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    objective_id = table.Column<uint>(type: "int(11)", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11)", nullable: false),
                    trigger_id = table.Column<uint>(type: "int(11)", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3)", nullable: false),
                    sequence = table.Column<uint>(type: "int(11)", nullable: false),
                    related_objective_id = table.Column<uint>(type: "int(11)", nullable: true),
                    related_state = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    event_kind = table.Column<byte>(type: "tinyint(3)", nullable: true),
                    subject_id = table.Column<uint>(type: "int(11)", nullable: true),
                    counter_id = table.Column<uint>(type: "int(11)", nullable: true),
                    initial_value = table.Column<uint>(type: "int(11)", nullable: true),
                    target_value = table.Column<uint>(type: "int(11)", nullable: true),
                    area_id = table.Column<uint>(type: "int(11)", nullable: true),
                    duration_seconds = table.Column<uint>(type: "int(11)", nullable: true),
                    npc_package_id = table.Column<uint>(type: "int(11)", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11)", nullable: true),
                    source_spawn_resolved = table.Column<bool>(type: "INTEGER", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_trigger", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id, x.trigger_id });
                    table.ForeignKey(
                        name: "FK_mission_trigger_mission_objective_transition_mission_id_content_revision_objective_id_transition_id",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id },
                        principalTable: "mission_objective_transition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id", "transition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "mission_content_definition_index_content_revision",
                table: "mission_content_definition",
                column: "content_revision");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_action");

            migrationBuilder.DropTable(
                name: "mission_area");

            migrationBuilder.DropTable(
                name: "mission_evidence");

            migrationBuilder.DropTable(
                name: "mission_indicator");

            migrationBuilder.DropTable(
                name: "mission_prerequisite");

            migrationBuilder.DropTable(
                name: "mission_reward_item");

            migrationBuilder.DropTable(
                name: "mission_scenario_step");

            migrationBuilder.DropTable(
                name: "mission_spawn");

            migrationBuilder.DropTable(
                name: "mission_trigger");

            migrationBuilder.DropTable(
                name: "mission_reward_definition");

            migrationBuilder.DropTable(
                name: "mission_scenario");

            migrationBuilder.DropTable(
                name: "mission_spawn_group");

            migrationBuilder.DropTable(
                name: "mission_objective_transition");

            migrationBuilder.DropTable(
                name: "mission_objective_definition");

            migrationBuilder.DropTable(
                name: "mission_content_definition");
        }
    }
}
