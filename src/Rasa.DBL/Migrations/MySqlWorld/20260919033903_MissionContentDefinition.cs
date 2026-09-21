using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
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
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    client_name_text_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    giver_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    receiver_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    level = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    group_type = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    category_id = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    shareable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    radio_completeable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_content_definition", x => new { x.mission_id, x.content_revision });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_area",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    area_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    map_context_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    shape = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    pos_x = table.Column<double>(type: "double", nullable: false),
                    pos_y = table.Column<double>(type: "double", nullable: false),
                    pos_z = table.Column<double>(type: "double", nullable: false),
                    radius = table.Column<double>(type: "double", nullable: true),
                    extent_x = table.Column<double>(type: "double", nullable: true),
                    extent_y = table.Column<double>(type: "double", nullable: true),
                    extent_z = table.Column<double>(type: "double", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_area", x => new { x.mission_id, x.content_revision, x.area_id });
                    table.ForeignKey(
                        name: "FK_mission_area_mission_content_definition_mission_id_content_r~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_evidence",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidence_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    owner_kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    owner_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    source_kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    source_uri = table.Column<string>(type: "varchar(256)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    local_client_path = table.Column<string>(type: "varchar(256)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confidence = table.Column<double>(type: "double unsigned", nullable: false),
                    reconstruction_note = table.Column<string>(type: "varchar(256)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_evidence", x => new { x.mission_id, x.content_revision, x.evidence_id });
                    table.ForeignKey(
                        name: "FK_mission_evidence_mission_content_definition_mission_id_conte~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_objective_definition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    client_name_text_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    client_body_text_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    ordinal = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    initial_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    is_required = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_objective_definition", x => new { x.mission_id, x.content_revision, x.objective_id });
                    table.ForeignKey(
                        name: "FK_mission_objective_definition_mission_content_definition_miss~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_prerequisite",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prerequisite_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    required_mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    required_mission_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    required_level = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    player_flag_value = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_prerequisite", x => new { x.mission_id, x.content_revision, x.prerequisite_id });
                    table.ForeignKey(
                        name: "FK_mission_prerequisite_mission_content_definition_mission_id_c~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_reward_definition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reward_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    experience = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    credits = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    prestige = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_reward_definition", x => new { x.mission_id, x.content_revision, x.reward_id });
                    table.ForeignKey(
                        name: "FK_mission_reward_definition_mission_content_definition_mission~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_scenario",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scenario_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scenario", x => new { x.mission_id, x.content_revision, x.scenario_id });
                    table.ForeignKey(
                        name: "FK_mission_scenario_mission_content_definition_mission_id_conte~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_spawn_group",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    spawn_group_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    area_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    map_context_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    respawn_seconds = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_spawn_group", x => new { x.mission_id, x.content_revision, x.spawn_group_id });
                    table.ForeignKey(
                        name: "FK_mission_spawn_group_mission_content_definition_mission_id_co~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_indicator",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    indicator_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    pos_x = table.Column<double>(type: "double", nullable: false),
                    pos_y = table.Column<double>(type: "double", nullable: false),
                    pos_z = table.Column<double>(type: "double", nullable: false),
                    radius = table.Column<double>(type: "double", nullable: false),
                    show_3d_effect = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_indicator", x => new { x.mission_id, x.content_revision, x.objective_id, x.indicator_id });
                    table.ForeignKey(
                        name: "FK_mission_indicator_mission_objective_definition_mission_id_co~",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id },
                        principalTable: "mission_objective_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_objective_transition",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    sequence = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    from_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    to_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_objective_transition", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id });
                    table.ForeignKey(
                        name: "FK_mission_objective_transition_mission_objective_definition_mi~",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id },
                        principalTable: "mission_objective_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_reward_item",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reward_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    item_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    item_template_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    quantity = table.Column<uint>(type: "int(11) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_reward_item", x => new { x.mission_id, x.content_revision, x.reward_id, x.item_id });
                    table.ForeignKey(
                        name: "FK_mission_reward_item_mission_reward_definition_mission_id_con~",
                        columns: x => new { x.mission_id, x.content_revision, x.reward_id },
                        principalTable: "mission_reward_definition",
                        principalColumns: new[] { "mission_id", "content_revision", "reward_id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_scenario_step",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scenario_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    step_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    sequence = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scenario_step", x => new { x.mission_id, x.content_revision, x.scenario_id, x.step_id });
                    table.ForeignKey(
                        name: "FK_mission_scenario_step_mission_scenario_mission_id_content_re~",
                        columns: x => new { x.mission_id, x.content_revision, x.scenario_id },
                        principalTable: "mission_scenario",
                        principalColumns: new[] { "mission_id", "content_revision", "scenario_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_spawn",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    spawn_group_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    spawn_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    creature_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    pos_x = table.Column<double>(type: "double", nullable: false),
                    pos_y = table.Column<double>(type: "double", nullable: false),
                    pos_z = table.Column<double>(type: "double", nullable: false),
                    rotation = table.Column<double>(type: "double", nullable: false),
                    quantity = table.Column<uint>(type: "int(11) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_spawn", x => new { x.mission_id, x.content_revision, x.spawn_group_id, x.spawn_id });
                    table.ForeignKey(
                        name: "FK_mission_spawn_mission_spawn_group_mission_id_content_revisio~",
                        columns: x => new { x.mission_id, x.content_revision, x.spawn_group_id },
                        principalTable: "mission_spawn_group",
                        principalColumns: new[] { "mission_id", "content_revision", "spawn_group_id" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_action",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    action_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    sequence = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    target_objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    objective_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    reward_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    spawn_group_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    scenario_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    indicator_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    player_flag_value = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    npc_package_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_action", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id, x.action_id });
                    table.ForeignKey(
                        name: "FK_mission_action_mission_objective_transition_mission_id_conte~",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id },
                        principalTable: "mission_objective_transition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id", "transition_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_trigger",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    transition_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    trigger_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    requirement = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    sequence = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    related_objective_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    related_state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    event_kind = table.Column<byte>(type: "tinyint(3) unsigned", nullable: true),
                    subject_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    counter_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    initial_value = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    target_value = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    area_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    duration_seconds = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    npc_package_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    player_flag_id = table.Column<uint>(type: "int(11) unsigned", nullable: true),
                    source_spawn_resolved = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    comment = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_trigger", x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id, x.trigger_id });
                    table.ForeignKey(
                        name: "FK_mission_trigger_mission_objective_transition_mission_id_cont~",
                        columns: x => new { x.mission_id, x.content_revision, x.objective_id, x.transition_id },
                        principalTable: "mission_objective_transition",
                        principalColumns: new[] { "mission_id", "content_revision", "objective_id", "transition_id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
