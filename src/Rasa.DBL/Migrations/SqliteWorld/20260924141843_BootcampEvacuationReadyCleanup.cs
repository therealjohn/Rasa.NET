using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteWorld
{
    /// <inheritdoc />
    public partial class BootcampEvacuationReadyCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampEvacuationReadyV4.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampEvacuationReadyV4.Down(migrationBuilder);
        }
    }
}
