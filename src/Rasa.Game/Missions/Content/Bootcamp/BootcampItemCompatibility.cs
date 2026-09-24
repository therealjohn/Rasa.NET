using System.Collections.Generic;

namespace Rasa.Game.Missions.Content.Bootcamp
{
    using Data;
    using Structures;

    internal static class BootcampItemCompatibility
    {
        internal static void Apply(IDictionary<uint, ItemTemplate> templates)
        {
            if (!templates.TryGetValue(17131, out var pistol) ||
                pistol.Class != (EntityClasses)27120 || pistol.WeaponInfo != null)
                return;
            if (!templates.TryGetValue(11557, out var reference) ||
                reference.Class != pistol.Class || reference.WeaponInfo == null)
                throw new System.InvalidOperationException("The starting pistol requires its authored class-27120 weapon profile (template 11557).");

            pistol.WeaponInfo = reference.WeaponInfo;
            Logger.WriteLog(LogType.Initialize,
                "Applied starting pistol weapon profile compatibility: template 17131 uses the authored class-27120 profile from 11557.");
        }
    }
}
