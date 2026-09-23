using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class NamedSceneTimerBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "mission_id",
                table: "mission_timer",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "objective_id",
                table: "mission_timer",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "sequence_id",
                table: "mission_timer",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mission_id",
                table: "mission_timer");

            migrationBuilder.DropColumn(
                name: "objective_id",
                table: "mission_timer");

            migrationBuilder.DropColumn(
                name: "sequence_id",
                table: "mission_timer");
        }
    }
}
