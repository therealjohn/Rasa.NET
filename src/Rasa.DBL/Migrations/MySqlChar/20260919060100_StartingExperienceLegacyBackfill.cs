using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Rasa.Migrations.MySqlChar
{
    /// <inheritdoc />
    public partial class StartingExperienceLegacyBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO `character_starting_experience` (`character_id`, `content_revision`, `state`) " +
                "SELECT `character`.`id`, 'legacy', 5 FROM `character` " +
                "WHERE NOT EXISTS (" +
                "SELECT 1 FROM `character_starting_experience` existing " +
                "WHERE existing.`character_id` = `character`.`id`)");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
