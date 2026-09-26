using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionRadioChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<uint>(
                name: "receiver_id",
                table: "mission_content_definition",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "int(11)");

            migrationBuilder.AlterColumn<uint>(
                name: "giver_id",
                table: "mission_content_definition",
                type: "int(11)",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "int(11)");

            migrationBuilder.CreateTable(
                name: "mission_channel_policy",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    acceptance_channel = table.Column<int>(type: "INTEGER", nullable: false),
                    completion_channel = table.Column<int>(type: "INTEGER", nullable: false),
                    radio_sources = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_channel_policy", x => new { x.mission_id, x.content_revision });
                    table.CheckConstraint("CK_mission_channel_policy_channels", "acceptance_channel IN (1, 2, 3) AND completion_channel IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_mission_channel_policy_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new System.NotSupportedException("Mission channels are forward-only; restore a backup rather than inventing NPC identities.");
    }
}
