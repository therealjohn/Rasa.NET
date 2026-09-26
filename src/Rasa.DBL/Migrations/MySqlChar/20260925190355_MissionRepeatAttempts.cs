using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class MissionRepeatAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE character_mission_history SET assignment_id = REPLACE(UUID(), '-', '') " +
                "WHERE assignment_id IS NULL OR assignment_id = '';");
            migrationBuilder.CreateIndex(
                name: "IX_p6_history_character_fk",
                table: "character_mission_history",
                column: "character_id");
            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history");

            migrationBuilder.AlterColumn<string>(
                name: "assignment_id",
                table: "character_mission_history",
                type: "varchar(32)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "assignment_generation",
                table: "character_mission_history",
                type: "int unsigned",
                nullable: false,
                defaultValue: 1u);

            migrationBuilder.AddColumn<DateTime>(
                name: "reward_window_start_utc",
                table: "character_mission_history",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rewarded_at_utc",
                table: "character_mission_history",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_history_character_id_mission_id_assignment~",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id", "assignment_generation" });

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_history_character_id_mission_id_reward_win~",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id", "reward_window_start_utc" },
                unique: true);

            migrationBuilder.DropIndex(name: "IX_p6_history_character_fk", table: "character_mission_history");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history");

            migrationBuilder.DropIndex(
                name: "IX_character_mission_history_character_id_mission_id_assignment~",
                table: "character_mission_history");

            migrationBuilder.DropIndex(
                name: "IX_character_mission_history_character_id_mission_id_reward_win~",
                table: "character_mission_history");

            migrationBuilder.DropColumn(
                name: "assignment_generation",
                table: "character_mission_history");

            migrationBuilder.DropColumn(
                name: "reward_window_start_utc",
                table: "character_mission_history");

            migrationBuilder.DropColumn(
                name: "rewarded_at_utc",
                table: "character_mission_history");

            migrationBuilder.AlterColumn<string>(
                name: "assignment_id",
                table: "character_mission_history",
                type: "varchar(32)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(32)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id" });
        }
    }
}
