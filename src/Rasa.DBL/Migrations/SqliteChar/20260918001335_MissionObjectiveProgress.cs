using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionObjectiveProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_mission_objective",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    objective_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    objective_state = table.Column<byte>(type: "tinyint(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_objective", x => new { x.character_id, x.mission_id, x.objective_id });
                    table.ForeignKey(
                        name: "FK_character_mission_objective_character_mission_character_id_mission_id",
                        columns: x => new { x.character_id, x.mission_id },
                        principalTable: "character_mission",
                        principalColumns: new[] { "character_id", "mission_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_mission_objective_counter",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    objective_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    counter_id = table.Column<uint>(type: "int(11)", nullable: false),
                    counter_value = table.Column<uint>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_objective_counter", x => new { x.character_id, x.mission_id, x.objective_id, x.counter_id });
                    table.ForeignKey(
                        name: "FK_character_mission_objective_counter_character_mission_objective_character_id_mission_id_objective_id",
                        columns: x => new { x.character_id, x.mission_id, x.objective_id },
                        principalTable: "character_mission_objective",
                        principalColumns: new[] { "character_id", "mission_id", "objective_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_mission_objective_item_counter",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    objective_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    item_class_id = table.Column<uint>(type: "int(11)", nullable: false),
                    counter_value = table.Column<uint>(type: "int(11)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_objective_item_counter", x => new { x.character_id, x.mission_id, x.objective_id, x.item_class_id });
                    table.ForeignKey(
                        name: "FK_character_mission_objective_item_counter_character_mission_objective_character_id_mission_id_objective_id",
                        columns: x => new { x.character_id, x.mission_id, x.objective_id },
                        principalTable: "character_mission_objective",
                        principalColumns: new[] { "character_id", "mission_id", "objective_id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_mission_objective_counter");

            migrationBuilder.DropTable(
                name: "character_mission_objective_item_counter");

            migrationBuilder.DropTable(
                name: "character_mission_objective");
        }
    }
}
