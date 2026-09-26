using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class BootcampAssignmentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampMissionItemsV7.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.BootcampMissionItemsV7.Down(migrationBuilder);
        }
    }
}
