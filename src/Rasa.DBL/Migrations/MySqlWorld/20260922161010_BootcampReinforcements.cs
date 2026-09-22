using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260922161010_BootcampReinforcements")]
    public class BootcampReinforcements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            BootcampReinforcementsContent.Up(migrationBuilder);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            BootcampReinforcementsContent.Down(migrationBuilder);
    }
}
