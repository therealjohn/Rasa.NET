using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rasa.Context.World;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.SqliteWorld
{
    [DbContext(typeof(SqliteWorldContext))]
    [Migration("20260922160000_BootcampCombat")]
    public class BootcampCombat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => BootcampCombatContent.Up(migrationBuilder);
        protected override void Down(MigrationBuilder migrationBuilder) => BootcampCombatContent.Down(migrationBuilder);
    }
}
