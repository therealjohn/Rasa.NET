using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionDurabilityState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_mission_deadline",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    due_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    state = table.Column<byte>(type: "tinyint(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_deadline", x => new { x.character_id, x.mission_id });
                    table.CheckConstraint("CK_character_mission_deadline_state", "state IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_character_mission_deadline_character_mission_character_id_mission_id",
                        columns: x => new { x.character_id, x.mission_id },
                        principalTable: "character_mission",
                        principalColumns: new[] { "character_id", "mission_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_mission_scenario_step",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    step_key = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_scenario_step", x => new { x.character_id, x.mission_id, x.step_key });
                    table.ForeignKey(
                        name: "FK_character_mission_scenario_step_character_mission_character_id_mission_id",
                        columns: x => new { x.character_id, x.mission_id },
                        principalTable: "character_mission",
                        principalColumns: new[] { "character_id", "mission_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_qualification",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    qualification_key = table.Column<byte>(type: "tinyint(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_qualification", x => new { x.character_id, x.qualification_key });
                    table.CheckConstraint("CK_character_qualification_key", "qualification_key IN (1)");
                    table.ForeignKey(
                        name: "FK_character_qualification_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_starting_experience",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    state = table.Column<byte>(type: "tinyint(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_starting_experience", x => x.character_id);
                    table.CheckConstraint("CK_character_starting_experience_state", "state IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_character_starting_experience_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_mission_deadline");

            migrationBuilder.DropTable(
                name: "character_mission_scenario_step");

            migrationBuilder.DropTable(
                name: "character_qualification");

            migrationBuilder.DropTable(
                name: "character_starting_experience");
        }
    }
}
