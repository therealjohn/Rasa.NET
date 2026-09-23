namespace Rasa.Game.Missions.World
{
    using global::Rasa.Missions.Definitions;
    using Managers;
    using Structures;

    internal static class CreatureGameplayRules
    {
        internal static ActorGameplayPolicy Policy(Creature creature) =>
            creature == null ? ActorGameplayPolicy.Ordinary :
                MissionApplication.Instance.ActorPolicies.Get(creature.SpawnPool?.MapContextId ?? creature.MapContextId, creature.DbId);
        internal static bool IsDefender(Creature creature) => Policy(creature).Defends;
        internal static bool IsInvulnerable(Creature creature) => Policy(creature).Invulnerable;
        internal static bool TracksParticipation(Creature creature) => Policy(creature).TrackParticipation;
    }
}
