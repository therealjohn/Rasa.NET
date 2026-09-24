using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MigrationOwnedMissionContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_active_release");

            migrationBuilder.DropTable(
                name: "mission_release_member");

            migrationBuilder.DropPrimaryKey(
                name: "PK_mission_experience_binding",
                table: "mission_experience_binding");

            migrationBuilder.DropColumn(
                name: "release_name",
                table: "mission_experience_binding");

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "mission_experience_binding",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "mission_content_definition",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_mission_experience_binding",
                table: "mission_experience_binding",
                column: "experience_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_mission_experience_binding",
                table: "mission_experience_binding");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "mission_experience_binding");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "mission_content_definition");

            migrationBuilder.AddColumn<string>(
                name: "release_name",
                table: "mission_experience_binding",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_mission_experience_binding",
                table: "mission_experience_binding",
                columns: new[] { "release_name", "experience_key" });

            migrationBuilder.CreateTable(
                name: "mission_active_release",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    manifest_hash = table.Column<string>(type: "varchar(64)", nullable: false),
                    release_name = table.Column<string>(type: "varchar(32)", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_active_release", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mission_release_member",
                columns: table => new
                {
                    release_name = table.Column<string>(type: "varchar(32)", nullable: false),
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_release_member", x => new { x.release_name, x.mission_id });
                    table.ForeignKey(
                        name: "FK_mission_release_member_mission_content_definition_mission_id_content_revision",
                        columns: x => new { x.mission_id, x.content_revision },
                        principalTable: "mission_content_definition",
                        principalColumns: new[] { "mission_id", "content_revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mission_release_member_mission_id_content_revision",
                table: "mission_release_member",
                columns: new[] { "mission_id", "content_revision" });
        }
    }
}
