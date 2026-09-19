using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class MissionContentRewardShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "selection_count",
                table: "mission_reward_definition",
                type: "tinyint(3)",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "kind",
                table: "mission_reward_item",
                type: "tinyint(3)",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_reward_item_kind",
                table: "mission_reward_item",
                sql: "kind IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_mission_reward_definition_selection_count",
                table: "mission_reward_definition",
                sql: "selection_count IN (0, 1)");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "mission_reward_definition");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "kind",
                table: "mission_reward_definition",
                type: "tinyint(3)",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_reward_item_kind",
                table: "mission_reward_item");

            migrationBuilder.DropCheckConstraint(
                name: "CK_mission_reward_definition_selection_count",
                table: "mission_reward_definition");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "mission_reward_item");

            migrationBuilder.DropColumn(
                name: "selection_count",
                table: "mission_reward_definition");
        }
    }
}
