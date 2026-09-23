using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionOutcomeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "outcome",
                table: "character_mission_history",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4u);
            migrationBuilder.Sql(
                "UPDATE character_mission_history SET outcome = COALESCE(" +
                "(SELECT mission_state FROM character_mission WHERE character_mission.character_id = " +
                "character_mission_history.character_id AND character_mission.mission_id = character_mission_history.mission_id), 4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "outcome",
                table: "character_mission_history");
        }
    }
}
