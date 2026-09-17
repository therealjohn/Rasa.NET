using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class MissionCharacterState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<uint>(
                name: "character_id",
                table: "character_mission",
                type: "int(11) unsigned",
                nullable: false,
                oldClrType: typeof(uint),
                oldType: "int unsigned")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission");

            migrationBuilder.AddColumn<bool>(
                name: "completeable",
                table: "character_mission",
                type: "tinyint(1)",
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
                onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission",
                table: "character_mission",
                column: "character_id");

            migrationBuilder.AlterColumn<uint>(
                name: "character_id",
                table: "character_mission",
                type: "int unsigned",
                nullable: false,
                oldClrType: typeof(uint),
                oldType: "int(11) unsigned")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.DropColumn(
                name: "completeable",
                table: "character_mission");
        }
    }
}
