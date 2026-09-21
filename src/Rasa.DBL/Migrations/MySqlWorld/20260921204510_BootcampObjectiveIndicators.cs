using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260921204510_BootcampObjectiveIndicators")]
    public class BootcampObjectiveIndicators : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_indicator set show_3d_effect = 0 " +
                "where content_revision = 'deployment_11' and mission_id in (1990, 1992, 1994, 1995, 2005);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_indicator set show_3d_effect = 1 " +
                "where content_revision = 'deployment_11' and mission_id in (1990, 1992, 1994, 1995, 2005);");
        }
    }
}
