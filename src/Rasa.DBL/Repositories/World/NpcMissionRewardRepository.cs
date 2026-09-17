using System.Collections.Generic;
using System.Linq;

namespace Rasa.Repositories.World
{
    using Context.World;
    using Structures.World;

    public interface INpcMissionRewardRepository
    {
        IReadOnlyList<NpcMissionRewardEntry> Get(uint missionId);
    }

    public class NpcMissionRewardRepository : INpcMissionRewardRepository
    {
        private readonly WorldContext _worldContext;

        public NpcMissionRewardRepository(WorldContext worldContext)
        {
            _worldContext = worldContext;
        }
        public IReadOnlyList<NpcMissionRewardEntry> Get(uint missionId)
        {
            var query = _worldContext.CreateNoTrackingQuery(_worldContext.NpcMissionRewardEntries);
            return query.Where(entry => entry.Id == missionId)
                .OrderBy(entry => entry.Type)
                .ThenBy(entry => entry.ItemTemplateId)
                .ThenBy(entry => entry.Quantity)
                .ToList();
        }
    }
}
