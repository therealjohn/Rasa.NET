using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class ForwardedSceneEffectProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "source_assignment_generation",
                table: "mission_world_effect",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_assignment_id",
                table: "mission_world_effect",
                type: "varchar(32)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<uint>(
                name: "source_generation",
                table: "mission_world_effect",
                type: "int unsigned",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_run_id",
                table: "mission_world_effect",
                type: "varchar(32)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mission_world_effect_source_run_id",
                table: "mission_world_effect",
                column: "source_run_id");

            migrationBuilder.Sql(@"
UPDATE mission_world_effect
SET status = 'Cancelled', failure = 'Legacy forwarded work has no source-attempt provenance.', version = version + 1
WHERE source_run_id IS NULL AND LENGTH(operation_key) = 71 AND operation_key LIKE 'shared-%'
  AND status IN ('Pending', 'Running')
  AND run_id IN (SELECT run_id FROM mission_scene WHERE mission_id = 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Forwarded operation provenance is durable. Restore a backup instead of removing attempt isolation.");
        }
    }
}
