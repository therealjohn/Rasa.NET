using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class AssignmentMissionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_mission_item",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    assignment_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    item_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    item_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    quantity = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_item", x => new { x.character_id, x.mission_id, x.assignment_id, x.item_key, x.item_id });
                    table.CheckConstraint("CK_mission_item_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "FK_character_mission_item_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_mission_item_receipt",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    assignment_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    operation_key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_item_receipt", x => new { x.character_id, x.assignment_id, x.operation_key });
                    table.ForeignKey(
                        name: "FK_character_mission_item_receipt_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_item_item_id",
                table: "character_mission_item",
                column: "item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_mission_item");

            migrationBuilder.DropTable(
                name: "character_mission_item_receipt");
        }
    }
}
