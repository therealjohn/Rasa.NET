namespace Rasa.Data
{
    // Community revision baseline, not exact client-build balance. See docs/abilities.md.
    internal static class HistoricalAbilities
    {
        internal const int LightningRange = 60;
        internal const int LightningActivationMilliseconds = 500;
        internal const int LightningCooldownMilliseconds = 1200;
        internal const int SprintEffectType = 247;

        internal sealed record SprintRank(double SpeedBonus, decimal MaximumAdrenalinePerSecond);

        internal static SprintRank Sprint(int rank) => rank switch
        {
            1 => new(0.20d, 0.015m),
            2 => new(0.30d, 0.0135m),
            3 => new(0.40d, 0.0125m),
            4 => new(0.50d, 0.010m),
            5 => new(0.60d, 0.009m),
            _ => throw new System.ArgumentOutOfRangeException(nameof(rank))
        };

        internal sealed record LightningRank(int Power, int MinimumDamage, int MaximumDamage,
            int ArcRadius, int ArcDamage, decimal SonicFraction = 0,
            decimal StunChance = 0, int StunMilliseconds = 0,
            int AreaRadius = 0, int AreaMinimumDamage = 0, int AreaMaximumDamage = 0,
            int AreaTickMilliseconds = 0, int AreaDurationMilliseconds = 0);

        internal static LightningRank Lightning(int rank) => rank switch
        {
            1 => new(25, 180, 240, 0, 0),
            2 => new(50, 240, 300, 12, 210),
            3 => new(75, 240, 300, 18, 90, 0.5m),
            4 => new(100, 240, 300, 24, 210, 0.5m, 0.5m, 3000),
            5 => new(150, 240, 300, 30, 210, 0.5m, 0.5m, 3000, 30, 60, 90, 2000, 6000),
            _ => throw new System.ArgumentOutOfRangeException(nameof(rank))
        };
    }
}
