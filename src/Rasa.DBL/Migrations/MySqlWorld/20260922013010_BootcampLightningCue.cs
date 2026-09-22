using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260922013010_BootcampLightningCue")]
    public class BootcampLightningCue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_action set npc_package_id = 1634 " +
                "where mission_id = 1990 and content_revision = 'deployment_11' and objective_id = 1 " +
                "and transition_id = 1 and action_id = 4 and kind = 9;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_action set npc_package_id = 1635 " +
                "where mission_id = 1990 and content_revision = 'deployment_11' and objective_id = 1 " +
                "and transition_id = 1 and action_id = 4 and kind = 9;");
        }
    }
}
