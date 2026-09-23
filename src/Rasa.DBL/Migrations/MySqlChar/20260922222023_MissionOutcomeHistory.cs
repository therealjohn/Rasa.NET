using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
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
                type: "int unsigned",
                nullable: false,
                defaultValue: 4u);
            migrationBuilder.Sql(
                "UPDATE character_mission_history h JOIN character_mission m " +
                "ON m.character_id = h.character_id AND m.mission_id = h.mission_id SET h.outcome = m.mission_state;");
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
