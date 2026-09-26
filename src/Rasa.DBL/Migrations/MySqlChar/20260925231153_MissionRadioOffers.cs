using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class MissionRadioOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_mission_offer",
                columns: table => new
                {
                    character_id = table.Column<uint>(type: "int(11) unsigned", nullable: false),
                    mission_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    offer_id = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    account_id = table.Column<uint>(type: "int unsigned", nullable: false),
                    player_entity_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    player_epoch = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    map_epoch = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    source_kind = table.Column<int>(type: "int", nullable: false),
                    source_key = table.Column<string>(type: "varchar(96)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_instance_id = table.Column<string>(type: "varchar(96)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    source_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_assignment_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    prior_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prior_assignment_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    prior_assignment_revision = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prior_history_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    state = table.Column<int>(type: "int", nullable: false),
                    consumed_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    consumed_assignment_generation = table.Column<uint>(type: "int unsigned", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_mission_offer", x => new { x.character_id, x.mission_id });
                    table.ForeignKey(
                        name: "FK_character_mission_offer_character_character_id",
                        column: x => x.character_id,
                        principalTable: "character",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_character_mission_offer_offer_id",
                table: "character_mission_offer",
                column: "offer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Mission offer authority is forward-only; restore a backup rather than discarding admission state.");
    }
}
