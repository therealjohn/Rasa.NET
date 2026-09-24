using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using Rasa.Data;
using Rasa.Missions.Content;
using Rasa.Missions.Scenes;

namespace Rasa.Services.Preloader.Missions
{
    public static class BootcampCorpseDialogueV6
    {
        private const string Revision = BootcampMissionDataV1.Revision;
        private const string Corpse = "bootcamp-conrad-corpse";
        private const string Mission = "mission_id = 1995 and content_revision = 'deployment_11'";

        public static MissionSceneDefinition Scene()
        {
            var scene = BootcampExtractionDataV5.Scene(1995);
            scene.Actors[Corpse] = CorpseActor(scene.Actors[Corpse]);
            scene.Sequences[1] = new()
            {
                World = new() { new RemoveActorIntent("retire-scout-survivor", "group-1-spawn-1-0") }
            };
            return scene;
        }

        public static MissionExperienceDefinition Experience()
        {
            var experience = BootcampExtractionDataV5.Experience();
            experience.Scene.Actors[Corpse] = CorpseActor(experience.Scene.Actors[Corpse]);
            return experience;
        }

        private static SceneActorDefinition CorpseActor(SceneActorDefinition actor) =>
            actor with
            {
                TemplateId = 21081,
                Position = new ScenePosition(-99, 86.33577f, 74),
                InitialObjectState = 0,
                Conversation = new SceneObjectConversation(1995, 3, 2584, 2)
            };

        public static void Up(MigrationBuilder migration)
        {
            migration.Sql($"delete from mission_action where {Mission} and objective_id = 2 and transition_id = 1;");
            migration.Sql($"update mission_action set transition_id = 1 where {Mission} and objective_id = 2 and transition_id = 2;");
            migration.Sql($"delete from mission_trigger where {Mission} and objective_id = 2 and transition_id = 2;");
            migration.Sql($"delete from mission_objective_transition where {Mission} and objective_id = 2 and transition_id = 2;");
            migration.Sql($"update mission_objective_transition set to_state = 2 where {Mission} and objective_id = 2 and transition_id = 1;");
            migration.Sql($"update mission_trigger set subject_id = 21081 where {Mission} and objective_id = 3 and subject_id = 24990;");
            migration.Sql($"update mission_scenario_step set entity_class_id = 21081, pos_y = 86.33577 where {Mission} and dynamic_object_key = '{Corpse}';");
            migration.Sql($"update mission_evidence set reconstruction_note = 'Proximity completes the search. Native NPC corpse dialogue 1995/2/2584/1 advances bomb objective 3 only on Continue.' where {Mission} and evidence_id in (2, 3);");
            MissionDataMigration.UpdateScene(migration, 1995, Revision, Scene());
            MissionDataMigration.UpdateExperience(migration, Experience());
        }

        public static void Down(MigrationBuilder migration)
        {
            migration.Sql(
                "insert into mission_objective_transition (mission_id, content_revision, objective_id, transition_id, requirement, sequence, from_state, to_state, comment) " +
                $"select mission_id, content_revision, 2, 2, requirement, 2, from_state, to_state, comment from mission_objective_transition where {Mission} and objective_id = 2 and transition_id = 1;");
            migration.Sql($"update mission_action set transition_id = 2 where {Mission} and objective_id = 2 and transition_id = 1;");
            migration.Sql($"update mission_objective_transition set to_state = 1 where {Mission} and objective_id = 2 and transition_id = 1;");
            BootcampWorldContentSeedData.Insert(migration, Structures.World.MissionTriggerEntry.TableName,
                typeof(Structures.World.MissionTriggerEntry),
                BootcampWorldContentSeedData.MissionTriggers().Where(row => (uint)row[0] == 1995 && (uint)row[2] == 10)
                    .Select(row => { var copy = (object[])row.Clone(); copy[2] = 2U; copy[3] = 2U; return copy; }));
            BootcampWorldContentSeedData.Insert(migration, Structures.World.MissionActionEntry.TableName,
                typeof(Structures.World.MissionActionEntry),
                BootcampWorldContentSeedData.MissionActions().Where(row => (uint)row[0] == 1995 &&
                    (uint)row[2] == 2 && (uint)row[4] == 2));
            migration.Sql($"update mission_trigger set subject_id = 24990 where {Mission} and objective_id = 3 and subject_id = 21081;");
            migration.Sql($"update mission_scenario_step set entity_class_id = 24990, pos_y = 86.41823 where {Mission} and dynamic_object_key = '{Corpse}';");
            MissionDataMigration.UpdateScene(migration, 1995, Revision, BootcampExtractionDataV5.Scene(1995));
            MissionDataMigration.UpdateExperience(migration, BootcampExtractionDataV5.Experience());
        }

        public static void UpgradeCharacters(MigrationBuilder migration)
        {
            // The queued area scene is durable proof that this assignment already found the bodies.
            migration.Sql(
                "create temporary table bootcamp_corpse_dialogue_upgrade as " +
                "select s.run_id, s.generation, s.owner_character_id as character_id " +
                "from mission_scene s join character_mission m on m.assignment_id = s.assignment_id " +
                "join character_mission_objective search on search.character_id = m.character_id and search.mission_id = m.mission_id and search.objective_id = 2 " +
                "join character_mission_objective bomb on bomb.character_id = m.character_id and bomb.mission_id = m.mission_id and bomb.objective_id = 3 " +
                "where s.mission_id = 1995 and s.script_key = 'bootcamp.reinforcements' and m.mission_state = 0 " +
                "and s.status in ('Running', 'Waiting') and search.objective_state = 1 and bomb.objective_state = 4 " +
                "and exists (select 1 from mission_scene_message input where input.run_id = s.run_id and input.generation = s.generation and input.sequence_id = 1 and input.status in ('Pending', 'Handled'));");
            migration.Sql(
                "update character_mission_objective set objective_state = case when objective_id = 2 then 2 else 1 end " +
                "where mission_id = 1995 and objective_id in (2, 3) and character_id in (select character_id from bootcamp_corpse_dialogue_upgrade);");
            migration.Sql(
                "insert into mission_scene_message (run_id, generation, operation_key, sequence_id, status, version) " +
                "select run_id, generation, 'corpse-dialogue-upgrade', 7, 'Pending', 0 from bootcamp_corpse_dialogue_upgrade;");
            migration.Sql(
                "update mission_world_effect set payload = '{\"$kind\":\"remove\",\"OperationKey\":\"sequence-1-step-1-group-1-spawn-1-0\",\"Role\":\"group-1-spawn-1-0\"}', status = 'Pending', failure = null " +
                "where operation_key = 'sequence-1-step-1-group-1-spawn-1-0' and run_id in " +
                "(select run_id from mission_scene where mission_id = 1995 and script_key = 'bootcamp.reinforcements');");
            migration.Sql(migration.ActiveProvider == "Pomelo.EntityFrameworkCore.MySql"
                ? "drop temporary table bootcamp_corpse_dialogue_upgrade;"
                : "drop table bootcamp_corpse_dialogue_upgrade;");
        }
    }
}
