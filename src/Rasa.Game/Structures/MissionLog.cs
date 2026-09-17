namespace Rasa.Structures
{
    using Data;

    public class MissionLog
    {
        public uint MissionId { get; }
        public MissionState State { get; set; }
        public bool Completeable { get; set; }

        public MissionLog(uint missionId, MissionState state, bool completeable)
        {
            MissionId = missionId;
            State = state;
            Completeable = state == MissionState.Active && completeable;
        }
    }
}
