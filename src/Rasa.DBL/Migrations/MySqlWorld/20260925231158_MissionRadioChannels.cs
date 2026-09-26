using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
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
                type: "int(11) unsigned",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "int(11) unsigned");

            migrationBuilder.AlterColumn<uint>(
                name: "giver_id",
                table: "mission_content_definition",
                type: "int(11) unsigned",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "int(11) unsigned");

            migrationBuilder.CreateTable(
                name: "mission_channel_policy",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acceptance_channel = table.Column<int>(type: "int", nullable: false),
                    completion_channel = table.Column<int>(type: "int", nullable: false),
                    radio_sources = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_channel_policy", x => new { x.mission_id, x.content_revision });
                    table.CheckConstraint("CK_mission_channel_policy_channels", "acceptance_channel IN (1, 2, 3) AND completion_channel IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_mission_channel_policy_mission_content_definition_mission_id~",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new System.NotSupportedException("Mission channels are forward-only; restore a backup rather than inventing NPC identities.");
    }
}
