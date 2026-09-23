namespace Rasa.Game.Missions.Persistence
{
    using Managers;
    using Repositories.Char;
    using Repositories.Char.CharacterMissionProgress;
    using Repositories.UnitOfWork;
    using Structures;
    using Migration = Content.Bootcamp.BootcampReinforcementsCompatibility;

    internal static class MissionSaveCompatibility
    {
        internal static CharacterMissionProgressSnapshot NormalizeSnapshot(uint characterId,
            CharacterMissionProgressSnapshot progress, IGameUnitOfWorkFactory factory, MissionApplication manager) =>
            Migration.NormalizeSnapshot(characterId, progress, factory, manager);
        internal static void NormalizeDurable(uint characterId, ICharUnitOfWork unit, MissionApplication manager) =>
            Migration.NormalizeDurable(characterId, unit, manager);
        internal static void NormalizeRuntime(Manifestation player, IGameUnitOfWorkFactory factory,
            MissionApplication manager, uint? missionId = null) =>
            Migration.NormalizeRuntime(player, factory, manager, missionId);
        internal static void NormalizeProgress(Manifestation player, IGameUnitOfWorkFactory factory,
            MissionApplication manager, MissionProgressEvent progress) =>
            Migration.NormalizeProgress(player, factory, manager, progress);
    }
}
