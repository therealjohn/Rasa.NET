using System.Linq;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Rasa.Context.World;
using Rasa.Services.Preloader;
using Rasa.Structures.World;

namespace Rasa.Migrations.MySqlWorld
{
    [DbContext(typeof(MySqlWorldContext))]
    [Migration("20260921183010_BootcampCrateLoot")]
    public class BootcampCrateLoot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_scenario_step " +
                "set kind = 4, reward_id = null, entity_class_id = 29877, comment = 'Disable empty equipment crate' " +
                "where mission_id = 1992 and content_revision = 'deployment_11' and scenario_id = 2 " +
                "and step_id = 1 and kind = 11 and reward_id = 58;");
            migrationBuilder.Sql(
                "delete from mission_scenario_step " +
                "where mission_id = 1992 and content_revision = 'deployment_11' and scenario_id = 2 " +
                "and step_id = 2 and kind = 21 and dynamic_object_key = 'bootcamp-equipment-crate';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "update mission_scenario_step " +
                "set kind = 11, reward_id = 58, entity_class_id = null, comment = 'Grant crate loadout' " +
                "where mission_id = 1992 and content_revision = 'deployment_11' and scenario_id = 2 " +
                "and step_id = 1 and kind = 4 and entity_class_id = 29877;");
            BootcampWorldContentSeedData.Insert(
                migrationBuilder,
                MissionScenarioStepEntry.TableName,
                typeof(MissionScenarioStepEntry),
                BootcampWorldContentSeedData.MissionScenarioSteps().Where(row =>
                    (uint)row[0] == 1992 && (uint)row[2] == 2 && (uint)row[3] == 2));
        }
    }
}
