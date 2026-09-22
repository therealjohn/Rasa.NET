using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;
using Rasa.Services.Preloader;

namespace Rasa.Migrations.SqliteWorld
{
    [DbContext(typeof(SqliteWorldContext))]
    [Migration("20260922161000_BootcampReinforcements")]
    public class BootcampReinforcements : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            BootcampReinforcementsContent.Up(migrationBuilder);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            BootcampReinforcementsContent.Down(migrationBuilder);
    }
}
