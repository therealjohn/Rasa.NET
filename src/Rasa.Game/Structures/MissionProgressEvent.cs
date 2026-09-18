namespace Rasa.Structures
{
    using Data;

    public readonly struct MissionProgressEvent
    {
        public MissionProgressEventKind Kind { get; }
        public uint SubjectId { get; }

        private MissionProgressEvent(MissionProgressEventKind kind, uint subjectId)
        {
            Kind = kind;
            SubjectId = subjectId;
        }

        public static MissionProgressEvent Waypoint(uint waypointId) =>
            new(MissionProgressEventKind.WaypointAcquired, waypointId);

        public static MissionProgressEvent Logos(uint logosId) =>
            new(MissionProgressEventKind.LogosAcquired, logosId);

        public static MissionProgressEvent Creature(uint creatureDbId) =>
            new(MissionProgressEventKind.CreatureKilled, creatureDbId);

        public static MissionProgressEvent Mission(uint completedMissionId) =>
            new(MissionProgressEventKind.MissionCompleted, completedMissionId);
    }
}
