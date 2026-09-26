using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
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
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    assignment_id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    reason = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");
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
