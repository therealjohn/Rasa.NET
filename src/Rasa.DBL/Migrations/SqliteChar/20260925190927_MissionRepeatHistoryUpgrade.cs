using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class MissionRepeatHistoryUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Services.Preloader.Missions.MissionRepeatHistoryUpgradeV1.Apply(migrationBuilder, sqlite: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Attempt history is durable. Restore a backup instead of collapsing terminal outcomes.");
        }
    }
}
