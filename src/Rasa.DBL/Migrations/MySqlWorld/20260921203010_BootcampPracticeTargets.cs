using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260921203010_BootcampPracticeTargets")]
    public class BootcampPracticeTargets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_trigger set event_kind = 13, subject_id = 29365, " +
                "counter_id = case objective_id when 3 then 1 else 194 end, " +
                "initial_value = null, target_value = null, source_spawn_resolved = null " +
                "where mission_id = 1992 and content_revision = 'deployment_11' " +
                "and objective_id in (3, 8) and transition_id = 1 and trigger_id = 1;");
            migrationBuilder.Sql(
                "update mission_scenario_step set kind = 3, spawn_group_id = null, " +
                "entity_class_id = 29365, comment = 'Keep the existing practice targets enabled' " +
                "where mission_id = 1992 and content_revision = 'deployment_11' " +
                "and ((scenario_id = 3 and step_id = 1) or (scenario_id = 4 and step_id = 3));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_trigger set event_kind = 2, subject_id = 510211, counter_id = null " +
                "where mission_id = 1992 and content_revision = 'deployment_11' " +
                "and objective_id = 3 and transition_id = 1 and trigger_id = 1;");
            migrationBuilder.Sql(
                "update mission_trigger set event_kind = 9, subject_id = 194, counter_id = 510212 " +
                "where mission_id = 1992 and content_revision = 'deployment_11' " +
                "and objective_id = 8 and transition_id = 1 and trigger_id = 1;");
            migrationBuilder.Sql(
                "update mission_scenario_step set kind = 1, entity_class_id = null, " +
                "spawn_group_id = case scenario_id when 3 then 1 else 2 end, " +
                "comment = case scenario_id when 3 then 'Spawn practice dummy' else 'Spawn Lightning dummy' end " +
                "where mission_id = 1992 and content_revision = 'deployment_11' " +
                "and ((scenario_id = 3 and step_id = 1) or (scenario_id = 4 and step_id = 3));");
        }
    }
}
