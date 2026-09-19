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

            migrationBuilder.Sql(
                "UPDATE mission_reward_definition " +
                "SET selection_count = CASE kind WHEN 2 THEN 1 ELSE 0 END;");

            migrationBuilder.Sql(
                "UPDATE mission_reward_item " +
                "SET kind = CASE " +
                "WHEN EXISTS (SELECT 1 FROM mission_reward_definition reward " +
                "WHERE reward.mission_id = mission_reward_item.mission_id " +
                "AND reward.content_revision = mission_reward_item.content_revision " +
                "AND reward.reward_id = mission_reward_item.reward_id " +
                "AND reward.kind = 2) THEN 2 ELSE 1 END;");

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

            // The old schema only stores reward shape on mission_reward_definition.kind.
            // When newer rows mix fixed and selectable items, downgrade chooses Selectable
            // if any selectable signal exists because the legacy schema cannot represent both.
            migrationBuilder.Sql(
                "UPDATE mission_reward_definition " +
                "SET kind = CASE " +
                "WHEN selection_count > 0 OR EXISTS (SELECT 1 FROM mission_reward_item item " +
                "WHERE item.mission_id = mission_reward_definition.mission_id " +
                "AND item.content_revision = mission_reward_definition.content_revision " +
                "AND item.reward_id = mission_reward_definition.reward_id " +
                "AND item.kind = 2) THEN 2 ELSE 1 END;");

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
