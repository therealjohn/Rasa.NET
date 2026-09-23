using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class SceneAssignmentIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assignment_id",
                table: "mission_scene",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mission_scene_owner_character_id_mission_id_script_key_assig~",
                table: "mission_scene",
                columns: new[] { "owner_character_id", "mission_id", "script_key", "assignment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mission_scene_owner_character_id_mission_id_script_key_assig~",
                table: "mission_scene");

            migrationBuilder.DropColumn(
                name: "assignment_id",
                table: "mission_scene");
        }
    }
}
