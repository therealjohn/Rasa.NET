using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Migrations.MySqlAuth
{
    public partial class Net10IdentityMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical migrations already create these columns as AUTO_INCREMENT.
            // Only the old snapshot omitted the identity annotations required by EF 9.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back metadata must not remove the existing AUTO_INCREMENT behavior.
        }
    }
}
