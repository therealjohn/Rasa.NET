using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionRepeatAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE character_mission_history SET assignment_id = lower(hex(randomblob(16))) " +
                "WHERE assignment_id IS NULL OR assignment_id = '';");
            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history");

            migrationBuilder.AlterColumn<string>(
                name: "assignment_id",
                table: "character_mission_history",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldNullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "assignment_generation",
                table: "character_mission_history",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1u);

            migrationBuilder.AddColumn<DateTime>(
                name: "reward_window_start_utc",
                table: "character_mission_history",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rewarded_at_utc",
                table: "character_mission_history",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_history_character_id_mission_id_assignment_generation",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id", "assignment_generation" });

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_history_character_id_mission_id_reward_window_start_utc",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id", "reward_window_start_utc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history");

            migrationBuilder.DropIndex(
                name: "IX_character_mission_history_character_id_mission_id_assignment_generation",
                table: "character_mission_history");

            migrationBuilder.DropIndex(
                name: "IX_character_mission_history_character_id_mission_id_reward_window_start_utc",
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
                oldType: "varchar(32)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_character_mission_history",
                table: "character_mission_history",
                columns: new[] { "character_id", "mission_id" });
        }
    }
}
