using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rasa.Migrations.MySqlWorld
{
    /// <inheritdoc />
    public partial class MissionContentLegacyNpcMissionBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO mission_content_definition " +
                "(mission_id, content_revision, requirement, client_name_text_id, giver_id, receiver_id, level, group_type, category_id, shareable, radio_completeable, comment) " +
                "SELECT npc_mission.id, 'legacy', 2, 0, npc_mission.giver_id, npc_mission.reciver_id, npc_mission.level, npc_mission.group_type, npc_mission.category_id, npc_mission.shareable, npc_mission.radio_completeable, npc_mission.comment " +
                "FROM npc_mission " +
                "WHERE NOT EXISTS (" +
                "SELECT 1 FROM mission_content_definition existing " +
                "WHERE existing.mission_id = npc_mission.id AND existing.content_revision = 'legacy')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
