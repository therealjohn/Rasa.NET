using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
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
                    character_id = table.Column<uint>(type: "integer", nullable: false),
                    mission_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    offer_id = table.Column<string>(type: "varchar(32)", nullable: false),
                    content_revision = table.Column<string>(type: "varchar(32)", nullable: false),
                    account_id = table.Column<uint>(type: "INTEGER", nullable: false),
                    player_entity_id = table.Column<ulong>(type: "INTEGER", nullable: false),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    player_epoch = table.Column<Guid>(type: "char(36)", nullable: false),
                    map_epoch = table.Column<Guid>(type: "char(36)", nullable: false),
                    source_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    source_key = table.Column<string>(type: "varchar(96)", nullable: false),
                    source_instance_id = table.Column<string>(type: "varchar(96)", nullable: false),
                    source_generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    source_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    source_assignment_generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    prior_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    prior_assignment_generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    prior_assignment_revision = table.Column<string>(type: "varchar(32)", nullable: true),
                    prior_history_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    consumed_assignment_id = table.Column<string>(type: "varchar(32)", nullable: true),
                    consumed_assignment_generation = table.Column<uint>(type: "INTEGER", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false)
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
                });

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
