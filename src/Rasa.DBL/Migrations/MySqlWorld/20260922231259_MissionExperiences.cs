using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class MissionExperiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mission_experience_binding",
                columns: table => new
                {
                    release_name = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    experience_key = table.Column<string>(type: "varchar(64)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    map_context_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    bindings = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mission_experience_binding", x => new { x.release_name, x.experience_key });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mission_experience_binding");
        }
    }
}
