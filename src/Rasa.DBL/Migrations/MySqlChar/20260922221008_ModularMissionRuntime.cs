using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class ModularMissionRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assignment_id",
                table: "character_mission",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "content_revision",
                table: "character_mission",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "generation",
                table: "character_mission",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "character_mission",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "character_mission_history",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    assignment_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    rewarded = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_history", x => new { x.character_id, x.mission_id });
                    table.ForeignKey(
                        name: "FK_character_mission_history_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_outcome",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    run_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_outcome", x => x.event_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_receipt",
                columns: table => new
                {
                    owner_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    operation_key = table.Column<string>(type: "varchar(96)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_receipt", x => new { x.owner_id, x.generation, x.operation_key });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_scene",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    release = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    script_key = table.Column<string>(type: "varchar(64)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    state_version = table.Column<int>(type: "int", nullable: false),
                    owner_character_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    map_key = table.Column<string>(type: "varchar(64)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    checkpoint = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    fault = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scene", x => x.run_id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_credit_delivery",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignment_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignment_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    character_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    objective_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_credit_delivery", x => new { x.event_id, x.assignment_id });
                    table.ForeignKey(
                        name: "FK_mission_credit_delivery_mission_outcome_event_id",
                        column: x => x.event_id,
                        principalTable: "mission_outcome",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_actor_lease",
                columns: table => new
                {
                    map_key = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    spawn_key = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    run_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actor_role = table.Column<string>(type: "varchar(64)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    state = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_actor_lease", x => new { x.map_key, x.spawn_key });
                    table.ForeignKey(
                        name: "FK_mission_actor_lease_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_scene_participant",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    character_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    assignment_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    assignment_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    active = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scene_participant", x => new { x.run_id, x.character_id });
                    table.ForeignKey(
                        name: "FK_mission_scene_participant_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_timer",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    clock_policy = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    due_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    remaining_ticks = table.Column<long>(type: "bigint", nullable: true),
                    disposition = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_timer", x => new { x.run_id, x.name });
                    table.ForeignKey(
                        name: "FK_mission_timer_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mission_world_effect",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    operation_key = table.Column<string>(type: "varchar(96)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payload = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(16)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_world_effect", x => new { x.run_id, x.generation, x.operation_key });
                    table.ForeignKey(
                        name: "FK_mission_world_effect_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE character_mission SET assignment_id = REPLACE(UUID(), '-', ''), " +
                "content_revision = 'legacy', generation = 1;");
            migrationBuilder.Sql(
                "INSERT INTO character_mission_history " +
                "(character_id, mission_id, assignment_id, content_revision, completed_at_utc, rewarded) " +
                "SELECT character_id, mission_id, assignment_id, content_revision, UTC_TIMESTAMP(), mission_state = 4 " +
                "FROM character_mission WHERE mission_state IN (1, 4);");
            migrationBuilder.Sql(
                "INSERT INTO mission_receipt (owner_id, generation, operation_key, kind, created_at_utc) " +
                "SELECT assignment_id, 0, 'mission-reward', 'Grant', UTC_TIMESTAMP() " +
                "FROM character_mission WHERE mission_state = 4;");

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_assignment_id",
                table: "character_mission",
                column: "assignment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mission_actor_lease_run_id",
                table: "mission_actor_lease",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_mission_credit_delivery_character_id_status",
                table: "mission_credit_delivery",
                columns: new[] { "character_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scene_map_key_status",
                table: "mission_scene",
                columns: new[] { "map_key", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scene_owner_character_id_mission_id",
                table: "mission_scene",
                columns: new[] { "owner_character_id", "mission_id" });

            migrationBuilder.CreateIndex(
                name: "IX_mission_timer_disposition_due_at_utc",
                table: "mission_timer",
                columns: new[] { "disposition", "due_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_mission_history");

            migrationBuilder.DropTable(
                name: "mission_actor_lease");

            migrationBuilder.DropTable(
                name: "mission_credit_delivery");

            migrationBuilder.DropTable(
                name: "mission_receipt");

            migrationBuilder.DropTable(
                name: "mission_scene_participant");

            migrationBuilder.DropTable(
                name: "mission_timer");

            migrationBuilder.DropTable(
                name: "mission_world_effect");

            migrationBuilder.DropTable(
                name: "mission_outcome");

            migrationBuilder.DropTable(
                name: "mission_scene");

            migrationBuilder.DropIndex(
                name: "IX_character_mission_assignment_id",
                table: "character_mission");

            migrationBuilder.DropColumn(
                name: "assignment_id",
                table: "character_mission");

            migrationBuilder.DropColumn(
                name: "content_revision",
                table: "character_mission");

            migrationBuilder.DropColumn(
                name: "generation",
                table: "character_mission");

            migrationBuilder.DropColumn(
                name: "version",
                table: "character_mission");
        }
    }
}
