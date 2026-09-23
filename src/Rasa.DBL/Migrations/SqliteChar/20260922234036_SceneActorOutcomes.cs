using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class SceneActorOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_actor_state",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false),
                    actor_role = table.Column<string>(type: "varchar(64)", nullable: false),
                    generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    owner_character_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    map_context_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    shared_key = table.Column<string>(type: "varchar(64)", nullable: true),
                    outcome = table.Column<string>(type: "varchar(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_actor_state", x => new { x.run_id, x.actor_role, x.generation });
                    table.ForeignKey(
                        name: "FK_mission_actor_state_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_actor_state");
        }
    }
}
