using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Rasa.Migrations.SqliteChar
{
    /// <inheritdoc />
    public partial class StartingExperienceLegacyBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT OR IGNORE INTO \"character_starting_experience\" (\"character_id\", \"content_revision\", \"state\") " +
                "SELECT \"character\".\"id\", 'legacy', 5 FROM \"character\"");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
