namespace Rasa.Structures
{
    using Data;
    using Structures.World;

    public sealed class Mission
    {
        public uint MissionId { get; }
        public uint MissionGiver { get; }
        public uint MissionReciver { get; }
        public uint Level { get; }
        public byte GroupType { get; }
        public byte CategoryId { get; }
        public bool Shareable { get; }
        public bool RadioCompletable { get; }

        public Mission(NpcMissionEntry mission)
        {
            MissionId = mission.Id;
            MissionGiver = mission.GiverId;
            MissionReciver = mission.ReciverId;
            Level = mission.Level;
            GroupType = mission.GroupType;
            CategoryId = mission.CategoryId;
            Shareable = mission.Shareable;
            RadioCompletable = mission.RadioCompleteable;
        }

        internal MissionInfo CreateInfo(MissionState state, bool completeable)
        {
            return new MissionInfo
            {
                MissionState = state,
                Completeable = state == MissionState.Active && completeable,
                MissionConstantData = new MissionConstantData
                {
                    Level = Level,
                    GroupType = GroupType,
                    CategoryId = CategoryId,
                    Shareable = Shareable,
                    RadioCompletable = RadioCompletable
                }
            };
        }
    }
}
