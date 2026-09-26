using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionItemLegacyUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_mission_item_quarantine",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    assignment_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_item_quarantine", x => new { x.character_id, x.assignment_id });
                    table.ForeignKey(
                        name: "FK_character_mission_item_quarantine_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            Services.Preloader.Missions.BootcampMissionItemUpgradeV1.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_mission_item_quarantine");
        }
    }
}
