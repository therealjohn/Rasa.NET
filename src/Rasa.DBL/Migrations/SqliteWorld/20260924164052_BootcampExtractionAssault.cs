using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class BootcampExtractionAssault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampExtractionDataV5.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampExtractionDataV5.Down(migrationBuilder);
        }
    }
}
