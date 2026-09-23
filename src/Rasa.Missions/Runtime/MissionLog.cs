using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Rasa.Structures
{
    using Data;

    public class MissionLog
    {
        public uint MissionId { get; }
        public MissionState State { get; set; }
        public bool Completeable { get; set; }
        public IReadOnlyDictionary<uint, MissionObjectiveLog> Objectives { get; }

        public MissionLog(
            uint missionId,
            MissionState state,
            bool completeable,
            IReadOnlyDictionary<uint, MissionObjectiveLog> objectives = null)
        {
            MissionId = missionId;
            State = state;
            Completeable = state == MissionState.Active && completeable;
            Objectives = new ReadOnlyDictionary<uint, MissionObjectiveLog>(
                new Dictionary<uint, MissionObjectiveLog>(
                    objectives ?? new Dictionary<uint, MissionObjectiveLog>()));
        }
    }
}
