using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionCharacterState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM \"character_mission\" " +
                "WHERE NOT EXISTS (" +
                "SELECT 1 FROM \"character\" " +
                "WHERE \"character\".\"id\" = \"character_mission\".\"character_id\")");

            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission");

            migrationBuilder.AlterColumn<uint>(
                name: "character_id",
                table: "character_mission",
                type: "integer",
                nullable: false,
                oldClrType: typeof(uint),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<bool>(
                name: "completeable",
                table: "character_mission",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission",
                columns: new[] { "character_id", "mission_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_character_mission_character_character_id",
                table: "character_mission",
                column: "character_id",
                principalTable: "character",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_character_mission_character_character_id",
                table: "character_mission");

            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission");

            migrationBuilder.DropColumn(
                name: "completeable",
                table: "character_mission");

            migrationBuilder.AlterColumn<uint>(
                name: "character_id",
                table: "character_mission",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(uint),
                oldType: "integer")
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission",
                column: "character_id");
        }
    }
}
