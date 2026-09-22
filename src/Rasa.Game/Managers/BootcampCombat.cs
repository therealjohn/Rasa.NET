namespace Rasa.Managers
{
    using Structures;

    internal static class BootcampCombat
    {
        internal static bool IsThrax(Creature creature) =>
            creature?.SpawnPool?.MapContextId == 1985 &&
            creature.DbId is 510210 or 510216 or >= 510221 and <= 510226;

        internal static bool IsBaseDefender(Creature creature) =>
            creature?.SpawnPool?.MapContextId == 1985 && creature.DbId == 510207;

        internal const float BaseDefenseRadius = 18;
    }
}
