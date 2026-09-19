using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionContentPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "start_policy",
                table: "mission_scenario",
                type: "tinyint(3)",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "abandonment_policy",
                table: "mission_content_definition",
                type: "tinyint(3)",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "start_policy",
                table: "mission_scenario");

            migrationBuilder.DropColumn(
                name: "abandonment_policy",
                table: "mission_content_definition");
        }
    }
}
