using System;
using System.Collections.Generic;

namespace Rasa.Structures
{
    internal static class BootcampThraxLoot
    {
        internal const uint SkullTemplateId = 41666;

        private static readonly (uint TemplateId, int Chance, int Minimum, int Maximum)[] Drops =
        {
            (SkullTemplateId, 100, 1, 1),
            (28, 55, 12, 24),
            (56, 30, 8, 16),
            (44917, 15, 1, 1),
            (41665, 25, 1, 1)
        };

        internal static IEnumerable<(uint TemplateId, uint Quantity)> Roll(Func<int, int, int> next)
        {
            foreach (var drop in Drops)
                if (drop.Chance == 100 || next(0, 100) < drop.Chance)
                    yield return (drop.TemplateId, (uint)(drop.Minimum == drop.Maximum
                        ? drop.Minimum
                        : next(drop.Minimum, drop.Maximum + 1)));
        }
    }
}
