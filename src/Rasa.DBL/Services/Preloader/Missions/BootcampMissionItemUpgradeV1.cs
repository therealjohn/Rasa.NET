using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Rasa.Missions.Content;
using Rasa.Missions.Scenes;

namespace Rasa.Services.Preloader.Missions
{
    public static class BootcampMissionItemUpgradeV1
    {
        public static void Up(MigrationBuilder migration)
        {
            var mysql = migration.ActiveProvider == "Pomelo.EntityFrameworkCore.MySql";
            var issuedKey = mysql ? "concat('bootcamp-bomb-issued:', m.assignment_id)"
                : "'bootcamp-bomb-issued:' || m.assignment_id";
            migration.Sql(
                "create temporary table mission_item_legacy_candidates as " +
                "select m.character_id, m.mission_id, m.assignment_id, m.generation, m.mission_state, d.state as deadline_state, " +
                $"exists (select 1 from character_mission_scenario_step r where r.character_id = m.character_id and r.mission_id = m.mission_id and r.step_key = {issuedKey}) as issued, " +
                "exists (select 1 from mission_scene s join mission_scene_participant p on p.run_id = s.run_id " +
                "where s.assignment_id = m.assignment_id and s.owner_character_id = m.character_id and s.mission_id = m.mission_id " +
                "and s.release = 'deployment_11' and s.map_key like '1985:%' and s.map_key <> '1985:0' " +
                "and p.character_id = m.character_id and p.assignment_id = m.assignment_id and p.assignment_generation = m.generation " +
                "and ((m.mission_id = 1995 and s.script_key = 'bootcamp.reinforcements') or (m.mission_id = 2005 and s.script_key = 'bootcamp.bomb-retry'))) as scene_ok, " +
                "(select count(*) from character_mission a join character_mission_deadline t on t.character_id = a.character_id and t.mission_id = a.mission_id " +
                "where a.character_id = m.character_id and a.mission_id in (1995, 2005) and a.mission_state = 0 and t.state = 1) as active_attempts, " +
                "(select count(*) from character_inventory v join items i on i.item_id = v.item_id where v.character_id = m.character_id and i.item_template_id = 11519) as bomb_count, " +
                "(select min(v.item_id) from character_inventory v join items i on i.item_id = v.item_id join `character` c on c.id = v.character_id " +
                "where v.character_id = m.character_id and v.account_id = c.account_id and v.invenotry_type = 1 and v.slot_id between 150 and 199 " +
                "and i.item_template_id = 11519 and i.stack_size = 1 " +
                "and (select count(*) from character_inventory other_item where other_item.item_id = v.item_id) = 1 " +
                "and (select count(*) from character_inventory other_slot where other_slot.character_id = v.character_id and other_slot.invenotry_type = 1 and other_slot.slot_id = v.slot_id) = 1 " +
                "and not exists (select 1 from clan_inventory clan where clan.item_id = v.item_id) " +
                "and not exists (select 1 from auction a where a.item_id = v.item_id)) as item_id " +
                "from character_mission m left join character_mission_deadline d on d.character_id = m.character_id and d.mission_id = m.mission_id " +
                "where m.mission_id in (1995, 2005) and m.content_revision in ('deployment_11', 'legacy', 'unversioned') " +
                "and (d.state is not null or exists (select 1 from character_mission_scenario_step r " +
                $"where r.character_id = m.character_id and r.mission_id = m.mission_id and r.step_key = {issuedKey}));");
            migration.Sql("create temporary table mission_item_legacy_owners as select * from mission_item_legacy_candidates " +
                "where issued = 1 and scene_ok = 1 and mission_state = 0 and deadline_state = 1 " +
                "and active_attempts = 1 and bomb_count = 1 and item_id is not null;");
            migration.Sql("create temporary table mission_item_legacy_valid as select c.* from mission_item_legacy_candidates c " +
                "left join mission_item_legacy_owners o on o.character_id = c.character_id " +
                "where o.assignment_id = c.assignment_id " +
                "or (c.issued = 1 and c.scene_ok = 1 and c.deadline_state in (2, 3, 4) " +
                "and (c.bomb_count = 0 or o.assignment_id is not null));");
            migration.Sql("insert into character_mission_item (character_id, mission_id, assignment_id, generation, item_key, item_id, quantity) " +
                "select o.character_id, o.mission_id, o.assignment_id, o.generation, 'bomb', o.item_id, 1 from mission_item_legacy_owners o " +
                "where not exists (select 1 from character_mission_item existing where existing.item_id = o.item_id);");
            foreach (var missionId in new[] { 1995U, 2005U })
            {
                var issue = new IssueMissionItemIntent(missionId == 1995 ? "issue-bomb" : "accept-bomb",
                    missionId, "bomb", 11519, 1);
                Receipt(migration, missionId, issue, "");
                Receipt(migration, missionId,
                    new ConsumeMissionItemIntent("plant-bomb", missionId, "bomb", 1, MissionItemScope.AssignmentIssued),
                    "and v.deadline_state = 2 ");
            }
            migration.Sql("insert into character_mission_item_quarantine (character_id, mission_id, assignment_id, reason) " +
                "select c.character_id, c.mission_id, c.assignment_id, 'Legacy bomb ownership is ambiguous or missing; reconcile the exact assignment, receipt and inventory before retrying.' " +
                "from mission_item_legacy_candidates c where not exists (select 1 from mission_item_legacy_valid v where v.assignment_id = c.assignment_id) " +
                "and not exists (select 1 from character_mission_item_quarantine q where q.character_id = c.character_id and q.assignment_id = c.assignment_id);");
            foreach (var table in new[] { "mission_item_legacy_valid", "mission_item_legacy_owners", "mission_item_legacy_candidates" })
                migration.Sql($"drop {(mysql ? "temporary " : "")}table {table};");
        }

        private static void Receipt(MigrationBuilder migration, uint missionId, CharacterIntent intent, string condition)
        {
            var payload = JsonSerializer.Serialize(intent, MissionContentCodec.Options).Replace("'", "''");
            migration.Sql("insert into character_mission_item_receipt (character_id, mission_id, assignment_id, generation, operation_key, payload) " +
                $"select v.character_id, v.mission_id, v.assignment_id, v.generation, '{intent.OperationKey}', '{payload}' " +
                $"from mission_item_legacy_valid v where v.mission_id = {missionId} {condition}" +
                "and not exists (select 1 from character_mission_item_receipt r where r.character_id = v.character_id " +
                $"and r.assignment_id = v.assignment_id and r.operation_key = '{intent.OperationKey}');");
        }
    }
}
