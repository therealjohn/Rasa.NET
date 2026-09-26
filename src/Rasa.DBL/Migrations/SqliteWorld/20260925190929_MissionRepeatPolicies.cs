using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionRepeatPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_repeat_policy",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    repeat_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    cooldown_seconds = table.Column<uint>(type: "INTEGER", nullable: true),
                    reset_second_utc = table.Column<uint>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_repeat_policy", x => new { x.mission_id, x.content_revision });
                    table.CheckConstraint("CK_mission_repeat_policy_parameters", "(repeat_kind IN (0, 1) AND cooldown_seconds IS NULL AND reset_second_utc IS NULL) OR (repeat_kind = 2 AND cooldown_seconds IS NOT NULL AND cooldown_seconds > 0 AND reset_second_utc IS NULL) OR (repeat_kind = 3 AND cooldown_seconds IS NULL AND reset_second_utc IS NOT NULL AND reset_second_utc BETWEEN 0 AND 86399)");
                    table.ForeignKey(
                        name: "FK_mission_repeat_policy_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_repeat_policy");
        }
    }
}
