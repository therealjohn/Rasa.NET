using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260921231510_BootcampWorldSetup")]
    public class BootcampWorldSetup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update spawnpool set pos_y = 120.059 where id = 510206 and map_context_id = 1985;");
            migrationBuilder.Sql(
                "update creature set run_speed = 7 where id = 510203;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update spawnpool set pos_y = 114.0 where id = 510206 and map_context_id = 1985;");
            migrationBuilder.Sql(
                "update creature set run_speed = 0 where id = 510203;");
        }
    }
}
