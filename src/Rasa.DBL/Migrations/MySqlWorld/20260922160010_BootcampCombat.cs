using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rasa.Context.World;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260922160010_BootcampCombat")]
    public class BootcampCombat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => BootcampCombatContent.Up(migrationBuilder);
        protected override void Down(MigrationBuilder migrationBuilder) => BootcampCombatContent.Down(migrationBuilder);
    }
}
