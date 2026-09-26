using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
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
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    assignment_id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    item_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    item_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    quantity = table.Column<uint>(type: "int unsigned", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "character_mission_item_receipt",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    assignment_id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operation_key = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    payload = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
