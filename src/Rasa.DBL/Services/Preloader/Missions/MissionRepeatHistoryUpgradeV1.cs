using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Services.Preloader.Missions
{
    public static class MissionRepeatHistoryUpgradeV1
    {
        public static void Apply(MigrationBuilder migration, bool sqlite)
        {
            var now = sqlite ? "CURRENT_TIMESTAMP" : "UTC_TIMESTAMP(6)";
            migration.Sql(@"
UPDATE character_mission_history SET assignment_generation = COALESCE(
    (SELECT generation FROM character_mission m WHERE m.assignment_id = character_mission_history.assignment_id), 1);
UPDATE character_mission_history SET rewarded_at_utc = COALESCE(
    (SELECT MAX(created_at_utc) FROM mission_receipt r
     WHERE r.owner_id = character_mission_history.assignment_id AND r.operation_key = 'mission-reward'),
    completed_at_utc)
WHERE rewarded = 1;");
            // Old journals do not record terminal time. Use the receipt when present,
            // otherwise the migration's UTC time, without changing the original history.
            migration.Sql($@"
INSERT INTO character_mission_history
    (character_id, mission_id, assignment_id, content_revision, assignment_generation,
     completed_at_utc, rewarded, outcome, rewarded_at_utc, reward_window_start_utc)
SELECT m.character_id, m.mission_id, m.assignment_id, m.content_revision, m.generation,
    COALESCE((SELECT MAX(created_at_utc) FROM mission_receipt r
        WHERE r.owner_id = m.assignment_id AND r.operation_key = 'mission-reward'), {now}),
    CASE WHEN m.mission_state = 4 THEN 1 ELSE 0 END, m.mission_state,
    CASE WHEN m.mission_state = 4 THEN COALESCE(
        (SELECT MAX(created_at_utc) FROM mission_receipt r
         WHERE r.owner_id = m.assignment_id AND r.operation_key = 'mission-reward'), {now}) ELSE NULL END, NULL
FROM character_mission m
WHERE m.mission_state IN (1, 2, 4)
    AND NOT EXISTS (SELECT 1 FROM character_mission_history h WHERE h.assignment_id = m.assignment_id);");
            migration.Sql(@"
INSERT INTO mission_receipt (owner_id, generation, operation_key, kind, created_at_utc)
SELECT h.assignment_id, h.assignment_generation, 'mission-reward', 'Grant', h.rewarded_at_utc
FROM character_mission_history h
WHERE h.rewarded = 1 AND NOT EXISTS (SELECT 1 FROM mission_receipt r
    WHERE r.owner_id = h.assignment_id AND r.operation_key = 'mission-reward');");
        }
    }
}
