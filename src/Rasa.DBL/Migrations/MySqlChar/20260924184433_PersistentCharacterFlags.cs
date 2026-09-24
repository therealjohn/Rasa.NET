using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class PersistentCharacterFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_qualification");

            migrationBuilder.CreateTable(
                name: "character_flag",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    flag_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    value = table.Column<uint>(type: "int(11) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_flag", x => new { x.character_id, x.flag_id });
                    table.CheckConstraint("CK_character_flag_id", "flag_id BETWEEN 1 AND 4294967295");
                    table.CheckConstraint("CK_character_flag_value", "value BETWEEN 0 AND 4294967295");
                    table.ForeignKey(
                        name: "FK_character_flag_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_flag");

            migrationBuilder.CreateTable(
                name: "character_qualification",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    qualification_key = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_qualification", x => new { x.character_id, x.qualification_key });
                    table.CheckConstraint("CK_character_qualification_key", "qualification_key IN (1)");
                    table.ForeignKey(
                        name: "FK_character_qualification_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
