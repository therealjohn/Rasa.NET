using System.Collections.Generic;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Migrations.MySqlWorld
{
    using Services.Preloader;

    public partial class BootcampMissionContent : Migration
    {
        private const uint CreatureIdMin = 510203;
        private const uint CreatureIdMax = 510212;
        private const uint StaticSpawnIdMin = 510203;
        private const uint StaticSpawnIdMax = 510206;
        private const string Revision = BootcampWorldContentSeedData.Revision;
        private const string MissionIds = "1990,1992,1994,1995,2005";

        private readonly ICollection<IPreloader> _preloaders = new List<IPreloader>
        {
            new BootcampMissionNpcCreaturePreloader(),
            new BootcampMissionNpcAppearancePreloader(),
            new BootcampMissionNpcPackagePreloader(),
            new BootcampMissionNpcSpawnpoolPreloader(),
            new BootcampMissionContentDefinitionPreloader(),
            new BootcampMissionPrerequisitePreloader(),
            new BootcampMissionObjectiveDefinitionPreloader(),
            new BootcampMissionObjectiveTransitionPreloader(),
            new BootcampMissionRewardDefinitionPreloader(),
            new BootcampMissionRewardItemPreloader(),
            new BootcampMissionIndicatorPreloader(),
            new BootcampMissionAreaPreloader(),
            new BootcampMissionSpawnGroupPreloader(),
            new BootcampMissionSpawnPreloader(),
            new BootcampMissionScenarioPreloader(),
            new BootcampMissionTriggerPreloader(),
            new BootcampMissionActionPreloader(),
            new BootcampMissionScenarioStepPreloader(),
            new BootcampMissionEvidencePreloader()
        };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var preloader in _preloaders)
                preloader.Preload(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"delete from mission_evidence where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_scenario_step where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_action where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_trigger where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_scenario where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_spawn where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_spawn_group where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_indicator where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_area where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_reward_item where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_reward_definition where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_objective_transition where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_objective_definition where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_prerequisite where content_revision = '{Revision}' and mission_id in ({MissionIds});");
            migrationBuilder.Sql($"delete from mission_content_definition where content_revision = '{Revision}' and mission_id in ({MissionIds});");

            migrationBuilder.Sql($"delete from npc_package where id between {CreatureIdMin} and 510209;");
            migrationBuilder.Sql($"delete from spawnpool where id between {StaticSpawnIdMin} and {StaticSpawnIdMax};");
            migrationBuilder.Sql($"delete from creature_appearance where id between {CreatureIdMin} and 510209;");
            migrationBuilder.Sql($"delete from creature where id between {CreatureIdMin} and {CreatureIdMax};");
        }
    }
}
