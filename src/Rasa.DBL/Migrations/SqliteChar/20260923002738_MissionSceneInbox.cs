using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionSceneInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_scene_message",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    run_id = table.Column<string>(type: "varchar(32)", nullable: false),
                    generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    operation_key = table.Column<string>(type: "varchar(96)", nullable: false),
                    sequence_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scene_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_mission_scene_message_mission_scene_run_id",
                        column: x => x.run_id,
                        principalTable: "mission_scene",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mission_scene_message_run_id_generation_operation_key",
                table: "mission_scene_message",
                columns: new[] { "run_id", "generation", "operation_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_scene_message");
        }
    }
}
