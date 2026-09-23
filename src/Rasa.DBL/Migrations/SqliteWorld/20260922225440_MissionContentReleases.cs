using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionContentReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_active_release",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    release_name = table.Column<string>(type: "varchar(32)", nullable: false),
                    manifest_hash = table.Column<string>(type: "varchar(64)", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "mission_scene_binding",
                columns: table => new
                {
                    mission_id = table.Column<uint>(type: "integer", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    script_key = table.Column<string>(type: "varchar(64)", nullable: true),
                    state_version = table.Column<int>(type: "INTEGER", nullable: false),
                    bindings = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_scene_binding", x => new { x.mission_id, x.content_revision });
                    table.ForeignKey(
                        name: "FK_mission_scene_binding_mission_content_definition_mission_id_content_revision",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_active_release");

            migrationBuilder.DropTable(
                name: "mission_release_member");

            migrationBuilder.DropTable(
                name: "mission_scene_binding");
        }
    }
}
