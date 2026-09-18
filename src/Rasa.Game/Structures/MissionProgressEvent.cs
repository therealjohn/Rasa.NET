namespace Rasa.Structures
{
    using Data;

    public readonly struct MissionProgressEvent
    {
        public MissionProgressEventKind Kind { get; }
        public uint SubjectId { get; }
        public uint Quantity { get; }

        private MissionProgressEvent(
            MissionProgressEventKind kind,
            uint subjectId,
            uint quantity = 1)
        {
            Kind = kind;
            SubjectId = subjectId;
            Quantity = quantity;
        }

        public static MissionProgressEvent Waypoint(uint waypointId) =>
            new(MissionProgressEventKind.WaypointAcquired, waypointId);

        public static MissionProgressEvent Logos(uint logosId) =>
            new(MissionProgressEventKind.LogosAcquired, logosId);

        public static MissionProgressEvent Creature(uint creatureDbId) =>
            new(MissionProgressEventKind.CreatureKilled, creatureDbId);

        public static MissionProgressEvent Mission(uint completedMissionId) =>
            new(MissionProgressEventKind.MissionCompleted, completedMissionId);

        public static MissionProgressEvent ItemAcquired(
            uint itemClassId,
            uint quantity) =>
            new(MissionProgressEventKind.ItemAcquired, itemClassId, quantity);

        public static MissionProgressEvent ItemConsumed(
            uint itemClassId,
            uint quantity) =>
            new(MissionProgressEventKind.ItemConsumed, itemClassId, quantity);

        public static MissionProgressEvent Interaction(uint entityClassId) =>
            new(MissionProgressEventKind.InteractionUsed, entityClassId);
    }
}
