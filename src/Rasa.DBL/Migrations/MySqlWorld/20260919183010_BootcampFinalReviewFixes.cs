using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260919183010_BootcampFinalReviewFixes")]
    public class BootcampFinalReviewFixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_content_definition " +
                "set abandonment_policy = 2 " +
                "where mission_id = 1990 and content_revision = 'deployment_11';");
            migrationBuilder.Sql(
                "update mission_content_definition " +
                "set requirement = 1 " +
                "where mission_id = 2005 and content_revision = 'deployment_11';");
            migrationBuilder.Sql(
                "update mission_prerequisite " +
                "set requirement = 1 " +
                "where mission_id = 2005 and content_revision = 'deployment_11';");
            migrationBuilder.Sql(
                "update mission_scenario " +
                "set start_policy = 2 " +
                "where content_revision = 'deployment_11' and " +
                "((mission_id = 1995 and scenario_id = 6) or (mission_id = 2005 and scenario_id = 5));");
            migrationBuilder.Sql(
                "insert into mission_reward_item " +
                "(mission_id, content_revision, reward_id, item_id, kind, item_template_id, quantity) " +
                "values (1992, 'deployment_11', 58, 6, 1, 28, 20) " +
                "on duplicate key update quantity = quantity;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "delete from mission_reward_item " +
                "where mission_id = 1992 and content_revision = 'deployment_11' and reward_id = 58 and item_id = 6;");
            migrationBuilder.Sql(
                "update mission_scenario " +
                "set start_policy = 1 " +
                "where content_revision = 'deployment_11' and " +
                "((mission_id = 1995 and scenario_id = 6) or (mission_id = 2005 and scenario_id = 5));");
            migrationBuilder.Sql(
                "update mission_prerequisite " +
                "set requirement = 2 " +
                "where mission_id = 2005 and content_revision = 'deployment_11';");
            migrationBuilder.Sql(
                "update mission_content_definition " +
                "set requirement = 2 " +
                "where mission_id = 2005 and content_revision = 'deployment_11';");
            migrationBuilder.Sql(
                "update mission_content_definition " +
                "set abandonment_policy = 1 " +
                "where mission_id = 1990 and content_revision = 'deployment_11';");
        }
    }
}
