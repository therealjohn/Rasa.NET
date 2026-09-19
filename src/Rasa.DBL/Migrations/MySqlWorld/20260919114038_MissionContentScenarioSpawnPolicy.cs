using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class MissionContentScenarioSpawnPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "spawn_policy",
                table: "mission_spawn_group",
                type: "tinyint(3) unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.Sql(
                "UPDATE mission_spawn_group SET spawn_policy = 1 WHERE mission_id = 1994 AND spawn_group_id IN (1, 2, 3)");
            migrationBuilder.Sql(
                "UPDATE mission_spawn_group SET respawn_seconds = NULL WHERE mission_id = 1994 AND spawn_group_id IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE mission_spawn_group SET respawn_seconds = 1 WHERE mission_id = 1994 AND spawn_group_id IN (1, 2, 3)");
            migrationBuilder.DropColumn(
                name: "spawn_policy",
                table: "mission_spawn_group");
        }
    }
}
