using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Services.Preloader
{
    using Rasa.Structures.Char;
    using Structures.World;

    internal static class BootcampWorldContentSeedData
    {
        internal const string Revision = "deployment_11";
        internal const uint BootcampMapContextId = 1985;
        internal const uint WildernessMapContextId = 1220;

        internal const uint MajorMcAllisterCreatureId = 510203;
        internal const uint CaptainDelessioCreatureId = 510204;
        internal const uint CorporalHartmannCreatureId = 510205;
        internal const uint CorporalDeSimoneCreatureId = 510206;
        internal const uint CaptainYoungbloodCreatureId = 510207;
        internal const uint WoundedSurvivorCreatureId = 510208;
        internal const uint CorporalVanValkenbergCreatureId = 510209;
        internal const uint TizzikGiCreatureId = 510210;
        internal const uint PracticeDummyCreatureId = 510211;
        internal const uint LightningDummyCreatureId = 510212;
        internal const uint CommanderRogersCreatureId = 100;
        private const byte MissionCompletedState = 4;
        private const byte MissionFailedState = 2;
        private const byte ObjectiveInactiveState = 4;
        private const byte ObjectiveIncompleteState = 1;
        private const byte ObjectiveCompletedState = 2;
        private const byte ObjectiveFailedState = 3;
        private const byte ProgressInteractionUsed = 6;
        private const byte ProgressAreaEntered = 7;
        private const byte ProgressItemEquipped = 8;
        private const byte ProgressAbilityHit = 9;
        private const byte ProgressCreatureKilled = 2;
        private const uint CaptureTheFlagPromotionRewardId = 59;
        private const uint CaptureTheFlagPromotionExperience = 43000;
        private const uint CaptureTheFlagYoungbloodDelayMilliseconds = 7000;

        internal static void Insert(
            MigrationBuilder migrationBuilder,
            string tableName,
            System.Type entityType,
            IEnumerable<object[]> rows)
        {
            var columns = entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(property => property.GetCustomAttributes<ColumnAttribute>())
                .Select(attribute => attribute.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            foreach (var row in rows)
            {
                var rowColumns = columns.Take(row.Length).ToArray();
                var values = string.Join(", ", row.Select(ToSqlLiteral));
                migrationBuilder.Sql(
                    $"insert into {tableName} ({string.Join(", ", rowColumns)}) values ({values});");
            }
        }

        private static string ToSqlLiteral(object value)
        {
            if (value == null)
                return "null";
            if (value is bool boolean)
                return boolean ? "1" : "0";
            if (value is string text)
                return $"'{text.Replace("'", "''")}'";

            var type = value.GetType();
            if (type.IsEnum)
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            return value switch
            {
                byte or sbyte or short or ushort or int or uint or long or ulong =>
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                float single => single.ToString("R", CultureInfo.InvariantCulture),
                double @double => @double.ToString("R", CultureInfo.InvariantCulture),
                decimal @decimal => @decimal.ToString(CultureInfo.InvariantCulture),
                _ => $"'{value.ToString()?.Replace("'", "''")}'"
            };
        }

        internal static IEnumerable<object[]> NpcCreatures()
        {
            yield return Creature(MajorMcAllisterCreatureId, "Major McAllister", 3846, 1, 10, 1000, 10566);
            yield return Creature(CaptainDelessioCreatureId, "Captain Delessio", 3846, 1, 10, 1000, 10576);
            yield return Creature(CorporalHartmannCreatureId, "Corporal Hartmann", 3846, 1, 10, 1000, 10578);
            yield return Creature(CorporalDeSimoneCreatureId, "Corporal DeSimone", 3846, 1, 10, 1000, 10579);
            yield return Creature(CaptainYoungbloodCreatureId, "Captain Youngblood", 3846, 1, 10, 1000, 10575);
            yield return Creature(WoundedSurvivorCreatureId, "Wounded Survivor", 3846, 1, 8, 1000, 0);
            yield return Creature(CorporalVanValkenbergCreatureId, "Corporal Van Valkenberg", 3846, 1, 10, 1000, 10574);
            yield return Creature(TizzikGiCreatureId, "Tizzik Gi", 10504, 0, 6, 1000, 6730);
            yield return Creature(PracticeDummyCreatureId, "Practice Dummy", 26548, 0, 1, 1, 0);
            yield return Creature(LightningDummyCreatureId, "Target Dummy", 26548, 0, 1, 1, 0);
        }

        internal static IEnumerable<object[]> NpcAppearances()
        {
            foreach (var creatureId in new[]
                     {
                         MajorMcAllisterCreatureId,
                         CaptainDelessioCreatureId,
                         CorporalHartmannCreatureId,
                         CorporalDeSimoneCreatureId,
                         CaptainYoungbloodCreatureId,
                         WoundedSurvivorCreatureId,
                         CorporalVanValkenbergCreatureId
                     })
            {
                yield return new object[] { creatureId, 17U, 24008U, 4286690539U };
                yield return new object[] { creatureId, 13U, 6271U, 1U };
                yield return new object[] { creatureId, 14U, 9781U, 4278655809U };
                yield return new object[] { creatureId, 15U, 4023U, 4294934528U };
                yield return new object[] { creatureId, 16U, 4022U, 4294934528U };
                yield return new object[] { creatureId, 2U, 4021U, 4294934528U };
            }
        }

        internal static IEnumerable<object[]> NpcPackages()
        {
            yield return new object[] { CaptainDelessioCreatureId, 2560U, "Bootcamp Captain Delessio" };
            yield return new object[] { CaptainYoungbloodCreatureId, 2561U, "Bootcamp Captain Youngblood" };
            yield return new object[] { CorporalDeSimoneCreatureId, 2562U, "Bootcamp Corporal DeSimone" };
            yield return new object[] { CorporalHartmannCreatureId, 2563U, "Bootcamp Corporal Hartmann" };
            yield return new object[] { CorporalVanValkenbergCreatureId, 2564U, "Bootcamp Corporal Van Valkenberg" };
            yield return new object[] { WoundedSurvivorCreatureId, 2584U, "Bootcamp wounded survivor" };
        }

        internal static IEnumerable<object[]> NpcSpawnpools()
        {
            yield return SpawnPool(MajorMcAllisterCreatureId, 387.2, 125.57, 53.3, 0.0, BootcampMapContextId, MajorMcAllisterCreatureId, "McAllister");
            yield return SpawnPool(CaptainDelessioCreatureId, 398.9, 114.0, 173.3, 0.0, BootcampMapContextId, CaptainDelessioCreatureId, "Delessio");
            yield return SpawnPool(CorporalHartmannCreatureId, 385.7, 119.4, 166.7, 0.0, BootcampMapContextId, CorporalHartmannCreatureId, "Hartmann");
            yield return SpawnPool(CorporalDeSimoneCreatureId, 391.5, 114.0, 164.8, 0.0, BootcampMapContextId, CorporalDeSimoneCreatureId, "DeSimone");
            yield return SpawnPool(WoundedSurvivorCreatureId, -104.6, 86.1, 70.5, 0.0, BootcampMapContextId, WoundedSurvivorCreatureId, "Wounded survivor");
        }

        internal static IEnumerable<object[]> MissionDefinitions()
        {
            yield return new object[] { 1990U, Revision, MissionContentRequirement.Required, 21132U, MajorMcAllisterCreatureId, MajorMcAllisterCreatureId, 1U, (byte)1, (byte)1, false, false, "Initiation" };
            yield return new object[] { 1992U, Revision, MissionContentRequirement.Required, 21164U, MajorMcAllisterCreatureId, CorporalDeSimoneCreatureId, 1U, (byte)1, (byte)1, false, false, "Gearing Up for Battle" };
            yield return new object[] { 1994U, Revision, MissionContentRequirement.Required, 21235U, CorporalDeSimoneCreatureId, CaptainYoungbloodCreatureId, 1U, (byte)1, (byte)1, false, false, "Capture the Flag" };
            yield return new object[] { 1995U, Revision, MissionContentRequirement.Required, 21240U, CaptainYoungbloodCreatureId, CommanderRogersCreatureId, 2U, (byte)1, (byte)1, false, false, "Calling for Reinforcements" };
            yield return new object[] { 2005U, Revision, MissionContentRequirement.Optional, 21564U, CaptainYoungbloodCreatureId, CommanderRogersCreatureId, 2U, (byte)1, (byte)1, false, false, "Calling for Reinforcements Retry" };
        }

        internal static IEnumerable<object[]> MissionPrerequisites()
        {
            yield return new object[] { 1992U, Revision, 1U, MissionContentRequirement.Required, MissionPrerequisiteKind.MissionCompleted, 1990U, null, null, null, null, "Requires Initiation" };
            yield return new object[] { 1994U, Revision, 1U, MissionContentRequirement.Required, MissionPrerequisiteKind.MissionCompleted, 1992U, MissionCompletedState, null, null, null, "Requires Gearing Up for Battle" };
            yield return new object[] { 1995U, Revision, 1U, MissionContentRequirement.Required, MissionPrerequisiteKind.MissionCompleted, 1994U, MissionCompletedState, null, null, null, "Requires Capture the Flag" };
            yield return new object[] { 2005U, Revision, 1U, MissionContentRequirement.Optional, MissionPrerequisiteKind.MissionAccepted, 1995U, MissionFailedState, null, null, null, "Retry after final mission fails" };
        }

        internal static IEnumerable<object[]> MissionObjectives()
        {
            yield return Objective(1990, 1, 21148, 21149, 1, "Approach the Eloh Hologram");
            yield return Objective(1990, 2, 21150, 21151, 2, "Approach the Eloh Hologram", initialState: ObjectiveInactiveState);

            yield return Objective(1992, 10, 21165, 21166, 1, "McAllister handoff acceptance", initialState: ObjectiveCompletedState);
            yield return Objective(1992, 4, 21482, 21483, 2, "Speak to Captain Delessio");
            yield return Objective(1992, 1, 21174, 21175, 3, "Get your gear from the nearby crate", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 2, 21176, 21177, 4, "Equip the gear", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 5, 21485, 21486, 5, "Speak to Captain Delessio", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 6, 21489, 21490, 6, "Speak to Corporal Hartmann by the Firing Range", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 3, 21178, 21179, 7, "Shoot the Practice Dummy", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 9, 21663, 21664, 8, "Speak to Corporal Hartmann", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 8, 21666, 21667, 9, "Use Lightning on the Target Dummy", initialState: ObjectiveInactiveState);
            yield return Objective(1992, 7, 21492, 21493, 10, "Speak to Corporal Hartmann", initialState: ObjectiveInactiveState);

            yield return Objective(1994, 4, 21670, 21671, 1, "Speak to Corporal DeSimone");
            yield return Objective(1994, 2, 21336, 21337, 2, "Find a way out of the cave", initialState: ObjectiveInactiveState);
            yield return Objective(1994, 1, 21313, 21314, 3, "Eliminate the Tizzik G", 21315, ObjectiveInactiveState);
            yield return Objective(1994, 3, 21615, 21616, 4, "Report to Captain Youngblood", initialState: ObjectiveInactiveState);

            yield return Objective(1995, 2, 21556, 21557, 1, "Locate the missing AFS team");
            yield return Objective(1995, 3, 21559, 21560, 2, "Remove the bomb from Conrad's corpse", initialState: ObjectiveInactiveState);
            yield return Objective(1995, 1, 21327, 21328, 3, "Destroy the crashed dropship", initialState: ObjectiveInactiveState);
            yield return Objective(1995, 4, 21561, 21562, 4, "Check in with Corporal Van Valkenberg", initialState: ObjectiveInactiveState);

            yield return Objective(2005, 1, 21569, 21570, 1, "Destroy the crashed dropship");
            yield return Objective(2005, 4, 21571, 21572, 2, "Check in with Corporal Van Valkenberg", initialState: ObjectiveInactiveState);
        }

        internal static IEnumerable<object[]> MissionTransitions()
        {
            yield return Transition(1990, 1, 1, "Bridge approach");
            yield return Transition(1990, 2, 1, "Terrace approach");

            yield return Transition(1992, 4, 1, "Delessio greeting");
            yield return Transition(1992, 1, 1, "Equipment crate");
            yield return Transition(1992, 2, 1, "Equip first crate item");
            yield return Transition(1992, 2, 2, "Equip crate gloves");
            yield return Transition(1992, 2, 3, "Equip crate legs");
            yield return Transition(1992, 2, 4, "Equip crate vest");
            yield return Transition(1992, 2, 5, "Equip crate weapon");
            yield return Transition(1992, 5, 1, "Delessio follow-up");
            yield return Transition(1992, 6, 1, "Hartmann greeting");
            yield return Transition(1992, 3, 1, "Firearm dummy destroyed");
            yield return Transition(1992, 9, 1, "Hartmann Lightning setup");
            yield return Transition(1992, 8, 1, "Lightning dummy hit");
            yield return Transition(1992, 7, 1, "Hartmann completion");

            yield return Transition(1994, 4, 1, "DeSimone promotion");
            yield return Transition(1994, 2, 1, "Cave-in exit");
            yield return Transition(1994, 1, 1, "Tizzik defeated");
            yield return Transition(1994, 3, 1, "Youngblood debrief");

            yield return Transition(1995, 2, 1, "Wounded survivor");
            yield return Transition(1995, 3, 1, "Conrad corpse");
            yield return Transition(1995, 1, 1, "Dropship charge");
            yield return Transition(1995, 1, 2, ObjectiveFailedState, "Dropship timeout");
            yield return Transition(1995, 4, 1, "Van Valkenberg");

            yield return Transition(2005, 1, 1, "Retry dropship charge");
            yield return Transition(2005, 1, 2, ObjectiveFailedState, "Retry timeout");
            yield return Transition(2005, 4, 1, "Retry Van Valkenberg");
        }

        internal static IEnumerable<object[]> MissionTriggers()
        {
            yield return AreaTrigger(1990, 1, 1, 430, "Approach marker 430");
            yield return AreaTrigger(1990, 2, 1, 431, "Approach marker 431");

            yield return ConversationTrigger(1992, 4, 1, 1, 2560, 1, "Delessio instructions");
            yield return ProgressTrigger(1992, 1, 1, 1, ProgressInteractionUsed, 7862, "Use equipment crate");
            yield return ProgressTrigger(1992, 2, 1, 1, ProgressItemEquipped, 13066, "Equip Astra boots", sourceSpawnResolved: true);
            yield return ProgressTrigger(1992, 2, 2, 1, ProgressItemEquipped, 13096, "Equip Recruit gloves", sourceSpawnResolved: true);
            yield return ProgressTrigger(1992, 2, 3, 1, ProgressItemEquipped, 13156, "Equip Recruit legs", sourceSpawnResolved: true);
            yield return ProgressTrigger(1992, 2, 4, 1, ProgressItemEquipped, 13186, "Equip Recruit vest", sourceSpawnResolved: true);
            yield return ProgressTrigger(1992, 2, 5, 1, ProgressItemEquipped, 13713, "Equip Shinobi rifle", sourceSpawnResolved: true);
            yield return ConversationTrigger(1992, 5, 1, 1, 2560, 1, "Return to Delessio");
            yield return ConversationTrigger(1992, 6, 1, 1, 2563, 1, "Report to Hartmann");
            yield return ProgressTrigger(1992, 3, 1, 1, ProgressCreatureKilled, PracticeDummyCreatureId, "Destroy the practice dummy");
            yield return ConversationTrigger(1992, 9, 1, 1, 2563, 1, "Return to Hartmann for Lightning");
            yield return ProgressTrigger(1992, 8, 1, 1, ProgressAbilityHit, 194, "Hit the Lightning dummy", counterId: LightningDummyCreatureId);
            yield return ConversationTrigger(1992, 7, 1, 1, 2563, 1, "Hartmann completion");

            yield return ConversationTrigger(1994, 4, 1, 1, 2562, 1, "Promotion with DeSimone");
            yield return AreaTrigger(1994, 2, 1, 439, "Exit through the cave-in");
            yield return ProgressTrigger(1994, 1, 1, 1, ProgressCreatureKilled, TizzikGiCreatureId, "Defeat Tizzik");
            yield return ConversationTrigger(1994, 3, 1, 1, 2561, 1, "Youngblood debrief");

            yield return ConversationTrigger(1995, 2, 1, 1, 2584, 1, "Wounded survivor briefing");
            yield return ProgressTrigger(1995, 3, 1, 1, ProgressInteractionUsed, 24990, "Recover Conrad's bomb");
            yield return ProgressTrigger(1995, 1, 1, 1, ProgressInteractionUsed, 24911, "Plant the dropship bomb");
            yield return TimerTrigger(1995, 1, 2, 1, 600U, "Dropship timer expired");
            yield return ConversationTrigger(1995, 4, 1, 1, 2564, 1, "Van Valkenberg handoff");

            yield return ProgressTrigger(2005, 1, 1, 1, ProgressInteractionUsed, 24911, "Retry the dropship bomb");
            yield return TimerTrigger(2005, 1, 2, 1, 600U, "Retry timer expired");
            yield return ConversationTrigger(2005, 4, 1, 1, 2564, 1, "Retry Van Valkenberg handoff");
        }

        internal static IEnumerable<object[]> MissionActions()
        {
            yield return CompleteAction(1990, 1, 1, 1, 1, "Complete bridge approach");
            yield return RevealAction(1990, 1, 1, 2, 2, "Reveal terrace approach");
            yield return ActivateAction(1990, 1, 1, 3, 2, "Activate terrace approach");

            yield return CompleteAction(1992, 4, 1, 1, 4, "Complete Delessio greeting");
            yield return StartScenarioAction(1992, 4, 1, 2, 1, "Start equipment crate scene");
            yield return RevealAction(1992, 4, 1, 3, 1, "Reveal equipment crate");
            yield return ActivateAction(1992, 4, 1, 4, 1, "Activate equipment crate");
            yield return CompleteAction(1992, 1, 1, 1, 1, "Complete equipment crate");
            yield return StartScenarioAction(1992, 1, 1, 2, 2, "Grant the crate loadout");
            yield return RevealAction(1992, 1, 1, 3, 2, "Reveal equip gear");
            yield return ActivateAction(1992, 1, 1, 4, 2, "Activate equip gear");
            yield return CompleteAction(1992, 2, 1, 1, 2, "Complete equip gear");
            yield return RevealAction(1992, 2, 1, 2, 5, "Reveal Delessio follow-up");
            yield return ActivateAction(1992, 2, 1, 3, 5, "Activate Delessio follow-up");
            yield return CompleteAction(1992, 2, 2, 1, 2, "Complete equip gloves");
            yield return RevealAction(1992, 2, 2, 2, 5, "Reveal Delessio follow-up from gloves");
            yield return ActivateAction(1992, 2, 2, 3, 5, "Activate Delessio follow-up from gloves");
            yield return CompleteAction(1992, 2, 3, 1, 2, "Complete equip legs");
            yield return RevealAction(1992, 2, 3, 2, 5, "Reveal Delessio follow-up from legs");
            yield return ActivateAction(1992, 2, 3, 3, 5, "Activate Delessio follow-up from legs");
            yield return CompleteAction(1992, 2, 4, 1, 2, "Complete equip vest");
            yield return RevealAction(1992, 2, 4, 2, 5, "Reveal Delessio follow-up from vest");
            yield return ActivateAction(1992, 2, 4, 3, 5, "Activate Delessio follow-up from vest");
            yield return CompleteAction(1992, 2, 5, 1, 2, "Complete equip weapon");
            yield return RevealAction(1992, 2, 5, 2, 5, "Reveal Delessio follow-up from weapon");
            yield return ActivateAction(1992, 2, 5, 3, 5, "Activate Delessio follow-up from weapon");
            yield return CompleteAction(1992, 5, 1, 1, 5, "Complete Delessio follow-up");
            yield return RevealAction(1992, 5, 1, 2, 6, "Reveal Hartmann greeting");
            yield return ActivateAction(1992, 5, 1, 3, 6, "Activate Hartmann greeting");
            yield return CompleteAction(1992, 6, 1, 1, 6, "Complete Hartmann greeting");
            yield return StartScenarioAction(1992, 6, 1, 2, 3, "Start practice dummy scene");
            yield return RevealAction(1992, 6, 1, 3, 3, "Reveal practice dummy");
            yield return ActivateAction(1992, 6, 1, 4, 3, "Activate practice dummy");
            yield return CompleteAction(1992, 3, 1, 1, 3, "Complete practice dummy");
            yield return RevealAction(1992, 3, 1, 2, 9, "Reveal Hartmann Lightning setup");
            yield return ActivateAction(1992, 3, 1, 3, 9, "Activate Hartmann Lightning setup");
            yield return CompleteAction(1992, 9, 1, 1, 9, "Complete Hartmann Lightning setup");
            yield return StartScenarioAction(1992, 9, 1, 2, 4, "Start Lightning training scene");
            yield return RevealAction(1992, 9, 1, 3, 8, "Reveal Lightning dummy");
            yield return ActivateAction(1992, 9, 1, 4, 8, "Activate Lightning dummy");
            yield return CompleteAction(1992, 8, 1, 1, 8, "Complete Lightning dummy");
            yield return RevealAction(1992, 8, 1, 2, 7, "Reveal Hartmann completion");
            yield return ActivateAction(1992, 8, 1, 3, 7, "Activate Hartmann completion");
            yield return CompleteAction(1992, 7, 1, 1, 7, "Complete Hartmann training");
            yield return RewardAction(1992, 7, 1, 2, 1, "Reference mission reward");

            yield return CompleteAction(1994, 4, 1, 1, 4, "Complete DeSimone promotion");
            yield return StartScenarioAction(1994, 4, 1, 2, 1, "Start promotion scene");
            yield return RevealAction(1994, 4, 1, 3, 2, "Reveal cave exit");
            yield return ActivateAction(1994, 4, 1, 4, 2, "Activate cave exit");
            yield return CompleteAction(1994, 2, 1, 1, 2, "Complete cave exit");
            yield return StartScenarioAction(1994, 2, 1, 2, 2, "Start the assault scene");
            yield return RevealAction(1994, 2, 1, 3, 1, "Reveal Tizzik encounter");
            yield return ActivateAction(1994, 2, 1, 4, 1, "Activate Tizzik encounter");
            yield return CompleteAction(1994, 1, 1, 1, 1, "Complete Tizzik encounter");
            yield return StartScenarioAction(1994, 1, 1, 2, 3, "Schedule Youngblood arrival");
            yield return CompleteAction(1994, 3, 1, 1, 3, "Complete Youngblood debrief");
            yield return RewardAction(1994, 3, 1, 2, 1, "Reference mission reward");

            yield return CompleteAction(1995, 2, 1, 1, 2, "Complete wounded survivor");
            yield return StartScenarioAction(1995, 2, 1, 2, 1, "Start the crash site scene");
            yield return RevealAction(1995, 2, 1, 3, 3, "Reveal Conrad corpse");
            yield return ActivateAction(1995, 2, 1, 4, 3, "Activate Conrad corpse");
            yield return CompleteAction(1995, 3, 1, 1, 3, "Complete Conrad corpse");
            yield return RevealAction(1995, 3, 1, 2, 1, "Reveal dropship charge");
            yield return ActivateAction(1995, 3, 1, 3, 1, "Activate dropship charge");
            yield return CompleteAction(1995, 1, 1, 1, 1, "Complete dropship charge");
            yield return StartScenarioAction(1995, 1, 1, 2, 3, "Begin the extraction fuse");
            yield return CompleteAction(1995, 4, 1, 1, 4, "Complete Van Valkenberg handoff");
            yield return StartScenarioAction(1995, 4, 1, 2, 5, "Transfer to Alia Das");

            yield return CompleteAction(2005, 1, 1, 1, 1, "Complete retry dropship charge");
            yield return StartScenarioAction(2005, 1, 1, 2, 1, "Begin the retry extraction fuse");
            yield return CompleteAction(2005, 4, 1, 1, 4, "Complete retry handoff");
            yield return StartScenarioAction(2005, 4, 1, 2, 3, "Transfer retry to Alia Das");
        }

        internal static IEnumerable<object[]> MissionRewards()
        {
            yield return new object[] { 1992U, Revision, 1U, MissionContentRequirement.Required, 1250U, 200U, 0U, (byte)0, "1992 completion reward" };
            yield return new object[] { 1992U, Revision, 58U, MissionContentRequirement.Required, 0U, 0U, 0U, (byte)0, "1992 equipment crate loadout" };
            yield return new object[] { 1994U, Revision, 1U, MissionContentRequirement.Required, 5000U, 0U, 0U, (byte)0, "1994 completion reward" };
            yield return new object[] { 1994U, Revision, CaptureTheFlagPromotionRewardId, MissionContentRequirement.Required, CaptureTheFlagPromotionExperience, 0U, 0U, (byte)0, "1994 promotion reward" };
        }

        internal static IEnumerable<object[]> MissionRewardItems()
        {
            yield return RewardItem(1992, 58, 1, MissionRewardItemKind.Fixed, 13066, 1);
            yield return RewardItem(1992, 58, 2, MissionRewardItemKind.Fixed, 13096, 1);
            yield return RewardItem(1992, 58, 3, MissionRewardItemKind.Fixed, 13156, 1);
            yield return RewardItem(1992, 58, 4, MissionRewardItemKind.Fixed, 13186, 1);
            yield return RewardItem(1992, 58, 5, MissionRewardItemKind.Fixed, 13713, 1);
        }

        internal static IEnumerable<object[]> MissionIndicators()
        {
            yield return Indicator(1990, 1, 430, 387.22, 118.75, -28.3, 10.0, "Eloh approach 1");
            yield return Indicator(1990, 2, 431, 387.83, 112.75, 6.71, 10.0, "Eloh approach 2");

            yield return Indicator(1992, 1, 433, 397.3, 114.0, 173.7, 6.0, "Equipment crate");
            yield return Indicator(1992, 6, 434, 384.7, 119.4, 186.8, 10.0, "Firing range");

            yield return Indicator(1994, 2, 439, 279.05, 120.5, 66.07, 10.0, "Cave-in location");
            yield return Indicator(1994, 1, 437, 95.1, 109.25, 150.8, 20.0, "Base center");

            yield return Indicator(1995, 2, 435, -104.6, 86.1, 70.5, 12.0, "Missing scout area");
            yield return Indicator(1995, 3, 436, -102.4, 85.69, 66.8, 8.0, "Conrad corpse");
            yield return Indicator(1995, 1, 432, -225.35, 99.60, -70.52, 12.0, "Dropship debris");
            yield return Indicator(1995, 4, 438, -225.35, 99.60, -70.52, 12.0, "Exit pad");

            yield return Indicator(2005, 1, 432, -225.35, 99.60, -70.52, 12.0, "Retry dropship debris");
            yield return Indicator(2005, 4, 438, -225.35, 99.60, -70.52, 12.0, "Retry exit pad");
        }

        internal static IEnumerable<object[]> MissionAreas()
        {
            yield return Area(1990, 430, 387.22, 118.75, -28.3, 10.0, "1990 obj1 area");
            yield return Area(1990, 431, 387.83, 112.75, 6.71, 10.0, "1990 obj2 area");

            yield return Area(1994, 439, 279.05, 120.5, 66.07, 10.0, "1994 cave-in");
            yield return Area(1994, 437, 95.1, 109.25, 150.8, 20.0, "1994 base center");

            yield return Area(1995, 435, -104.6, 86.1, 70.5, 12.0, "1995 scout party");
            yield return Area(1995, 438, -225.35, 99.60, -70.52, 12.0, "1995 exit pad");

            yield return Area(2005, 438, -225.35, 99.60, -70.52, 12.0, "2005 exit pad");
        }

        internal static IEnumerable<object[]> MissionSpawnGroups()
        {
            yield return SpawnGroup(1992, 1, BootcampMapContextId, false, null, "Practice dummy");
            yield return SpawnGroup(1992, 2, BootcampMapContextId, false, null, "Lightning dummy");

            yield return SpawnGroup(1994, 1, BootcampMapContextId, false, null, "AFS escort");
            yield return SpawnGroup(1994, 2, BootcampMapContextId, false, 437U, "Tizzik encounter");
            yield return SpawnGroup(1994, 3, BootcampMapContextId, false, 437U, "Youngblood arrival");

            yield return SpawnGroup(1995, 1, BootcampMapContextId, false, 435U, "Wounded survivor");
            yield return SpawnGroup(1995, 2, BootcampMapContextId, false, 438U, "Reinforcement arrival");
            yield return SpawnGroup(1995, 3, BootcampMapContextId, false, 438U, "Van Valkenberg");

            yield return SpawnGroup(2005, 1, BootcampMapContextId, false, 438U, "Retry reinforcement arrival");
            yield return SpawnGroup(2005, 2, BootcampMapContextId, false, 438U, "Retry Van Valkenberg");
        }

        internal static IEnumerable<object[]> MissionSpawns()
        {
            yield return Spawn(1992, 1, 1, PracticeDummyCreatureId, 384.7, 119.4, 186.8, 0.0, 1);
            yield return Spawn(1992, 2, 1, LightningDummyCreatureId, 389.7, 119.4, 186.8, 0.0, 1);

            yield return Spawn(1994, 1, 1, 39, 286.23, 120.5, 65.35, 0.0, 2);
            yield return Spawn(1994, 2, 1, TizzikGiCreatureId, 95.1, 109.25, 150.8, 0.0, 1);
            yield return Spawn(1994, 3, 1, CaptainYoungbloodCreatureId, 93.2, 109.0, 137.5, 0.0, 1);

            yield return Spawn(1995, 1, 1, WoundedSurvivorCreatureId, -104.6, 86.1, 70.5, 0.0, 1);
            yield return Spawn(1995, 2, 1, 39, -218.0, 99.60, -78.0, 0.0, 1);
            yield return Spawn(1995, 2, 2, 50, -221.0, 99.60, -74.0, 0.0, 1);
            yield return Spawn(1995, 3, 1, CorporalVanValkenbergCreatureId, -223.0, 99.60, -76.0, 0.0, 1);

            yield return Spawn(2005, 1, 1, 39, -218.0, 99.60, -78.0, 0.0, 1);
            yield return Spawn(2005, 1, 2, 50, -221.0, 99.60, -74.0, 0.0, 1);
            yield return Spawn(2005, 2, 1, CorporalVanValkenbergCreatureId, -223.0, 99.60, -76.0, 0.0, 1);
        }

        internal static IEnumerable<object[]> MissionScenarios()
        {
            yield return Scenario(1992, 1, "bootcamp-1992-crate", "1992 equipment crate scene");
            yield return Scenario(1992, 2, "bootcamp-1992-loadout", "1992 loadout scene");
            yield return Scenario(1992, 3, "bootcamp-1992-firearm", "1992 firearm range scene");
            yield return Scenario(1992, 4, "bootcamp-1992-lightning", "1992 Lightning range scene");
            yield return Scenario(1994, 1, "bootcamp-1994-promotion", "1994 promotion");
            yield return Scenario(1994, 2, "bootcamp-1994-assault", "1994 assault");
            yield return Scenario(1994, 3, "bootcamp-1994-youngblood-delay", "1994 delayed Youngblood");
            yield return Scenario(1994, 4, "bootcamp-1994-youngblood-arrival", "1994 Youngblood arrival");
            yield return Scenario(1995, 1, "bootcamp-1995-world", "1995 missing-team scene");
            yield return Scenario(1995, 2, "bootcamp-1995-exit", "1995 exit handoff");
            yield return Scenario(1995, 3, "bootcamp-1995-fuse", "1995 extraction fuse");
            yield return Scenario(1995, 5, "bootcamp-1995-transfer", "1995 transfer handoff");
            yield return Scenario(2005, 1, "bootcamp-2005-fuse", "2005 retry fuse");
            yield return Scenario(2005, 2, "bootcamp-2005-exit", "2005 retry exit handoff");
            yield return Scenario(2005, 3, "bootcamp-2005-transfer", "2005 retry transfer");
        }

        internal static IEnumerable<object[]> MissionScenarioSteps()
        {
            yield return SpawnDynamicObjectStep(1992, 1, 1, "bootcamp-equipment-crate", 7862, 397.3, 114.0, 173.7, 0.0, true, "Spawn equipment crate");
            yield return GrantRewardPackageStep(1992, 2, 1, 58, "Grant crate loadout");
            yield return DespawnDynamicObjectStep(1992, 2, 2, "bootcamp-equipment-crate", "Remove used equipment crate");
            yield return SpawnGroupStep(1992, 3, 1, 1, "Spawn practice dummy");
            yield return GrantSkillAbilityStep(1992, 4, 1, 49U, 194U, 1, 0, "Grant Recruit Lightning");
            yield return PlayTutorialStep(1992, 4, 2, 10000015U, null, "Prompt the player to use Lightning");
            yield return SpawnGroupStep(1992, 4, 3, 2, "Spawn Lightning dummy");

            yield return GrantRewardPackageStep(1994, 1, 1, CaptureTheFlagPromotionRewardId, "Grant the promotion");
            yield return SpawnGroupStep(1994, 2, 1, 1, "Spawn escort group");
            yield return EscortSpawnGroupStep(1994, 2, 2, 1, "Escort the player");
            yield return SpawnGroupStep(1994, 2, 3, 2, "Spawn Tizzik encounter");
            yield return ScheduleScenarioStep(1994, 3, 1, 4, CaptureTheFlagYoungbloodDelayMilliseconds, "Delay Youngblood arrival");
            yield return SpawnGroupStep(1994, 4, 1, 3, "Spawn Youngblood arrival");
            yield return RevealObjectiveStep(1994, 4, 2, 3, "Reveal Youngblood debrief");
            yield return ActivateObjectiveStep(1994, 4, 3, 3, "Activate Youngblood debrief");

            yield return SpawnDynamicObjectStep(1995, 1, 1, "bootcamp-conrad-corpse", 24990, -102.4, 85.69, 66.8, 0.0, true, "Spawn Conrad corpse");
            yield return SpawnDynamicObjectStep(1995, 1, 2, "bootcamp-dropship-debris", 24911, -225.35, 99.60, -70.52, 0.0, true, "Spawn dropship debris");

            yield return SpawnGroupStep(1995, 2, 1, 2, "Spawn reinforcements");
            yield return SpawnGroupStep(1995, 2, 2, 3, "Spawn Van Valkenberg");
            yield return RevealObjectiveStep(1995, 2, 3, 4, "Reveal Van Valkenberg handoff");
            yield return ActivateObjectiveStep(1995, 2, 4, 4, "Activate Van Valkenberg handoff");

            yield return CancelDeadlineStep(1995, 3, 1, "Cancel the bomb timer");
            yield return ScheduleScenarioStep(1995, 3, 2, 2, 5000U, "Schedule the exit handoff");

            yield return TransferPlayerStep(1995, 5, 1, WildernessMapContextId, 884.11, 305.8, 347.81, 1.5613, "Transfer to Alia Das");
            yield return QualificationStep(1995, 5, 2, CharacterQualificationKey.BootcampComplete, MissionScenarioStepEntry.GrantedQualificationValue, "Mark Bootcamp complete");
            yield return SkipEntitlementStep(1995, 5, 3, true, "Unlock account bootcamp skip");

            yield return CancelDeadlineStep(2005, 1, 1, "Cancel the retry timer");
            yield return ScheduleScenarioStep(2005, 1, 2, 2, 5000U, "Schedule the retry exit handoff");

            yield return SpawnGroupStep(2005, 2, 1, 1, "Spawn retry reinforcements");
            yield return SpawnGroupStep(2005, 2, 2, 2, "Spawn retry Van Valkenberg");
            yield return RevealObjectiveStep(2005, 2, 3, 4, "Reveal retry handoff");
            yield return ActivateObjectiveStep(2005, 2, 4, 4, "Activate retry handoff");

            yield return TransferPlayerStep(2005, 3, 1, WildernessMapContextId, 884.11, 305.8, 347.81, 1.5613, "Transfer retry to Alia Das");
            yield return QualificationStep(2005, 3, 2, CharacterQualificationKey.BootcampComplete, MissionScenarioStepEntry.GrantedQualificationValue, "Mark Bootcamp complete");
            yield return SkipEntitlementStep(2005, 3, 3, true, "Unlock account bootcamp skip");
        }

        internal static IEnumerable<object[]> MissionEvidence()
        {
            yield return Evidence(1990, 1, MissionEvidenceOwnerKind.Mission, 1990, MissionEvidenceSourceKind.Client, null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionconversation.pyo_dis", 1.0,
                "missionconversation offsets 1990/1-6 establish Initiation identity");
            yield return Evidence(1990, 2, MissionEvidenceOwnerKind.Objective, 1, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\missionobjectiveindicatorlanguage.pyo_dis", 0.8,
                "position_key=area.1990.1 and area.1990.2; measured then snapped to adv_bootcamp navmesh");

            yield return Evidence(1992, 1, MissionEvidenceOwnerKind.Mission, 1992, MissionEvidenceSourceKind.Client, null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\objectiveconversation.pyo_dis", 1.0,
                "objectiveconversation binds packages 2560 and 2563 for 1992 conversations");
            yield return Evidence(1992, 2, MissionEvidenceOwnerKind.Objective, 10, MissionEvidenceSourceKind.Reconstruction,
                null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionconversation.pyo_dis", 0.8,
                "client objectiveconversation has no McAllister row for 1992, so objective 10 reconstructs the McAllister handoff from missionconversation 1992/1-4 and missiontextlanguage 21165/21166 without renumbering client objectives");
            yield return Evidence(1992, 3, MissionEvidenceOwnerKind.Reward, 58, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-reconstruction-manifest.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\modulenamelanguage.pyo_dis", 0.55,
                "crate item templates 13066/13096/13156/13186/13713 are compatible level-1 analogues; maker variants unresolved");
            yield return Evidence(1992, 4, MissionEvidenceOwnerKind.Scenario, 1, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionobjective.pyo_dis", 0.75,
                "position_key=object.supply_crate, object.practice_dummy, npc.hartmann; measured and navmesh-snapped");

            yield return Evidence(1994, 1, MissionEvidenceOwnerKind.Mission, 1994, MissionEvidenceSourceKind.Client, null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionobjective.pyo_dis", 1.0,
                "1994 objective ids 1-4 and client texts establish the revised sequence");
            yield return Evidence(1994, 2, MissionEvidenceOwnerKind.Objective, 2, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\missionobjectiveindicatorlanguage.pyo_dis", 0.8,
                "position_key=area.1994.2 and indicator.1994.2.439; cave-in trigger reconstructed from footage");
            yield return Evidence(1994, 3, MissionEvidenceOwnerKind.Objective, 1, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\creaturenamelanguage.pyo_dis", 0.45,
                "position_key=npc.tizzik_gi and npc.youngblood; boss class, attendants, and arrival timing remain low-confidence");

            yield return Evidence(1995, 1, MissionEvidenceOwnerKind.Mission, 1995, MissionEvidenceSourceKind.Client, null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\objectiveconversation.pyo_dis", 1.0,
                "objectiveconversation binds wounded survivor 2584 and Van Valkenberg 2564");
            yield return Evidence(1995, 2, MissionEvidenceOwnerKind.Objective, 2, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\missionobjectiveindicatorlanguage.pyo_dis", 0.7,
                "position_key for the missing-team search area is measured; survivor and Conrad points remain reconstructed");
            yield return Evidence(1995, 3, MissionEvidenceOwnerKind.Objective, 1, MissionEvidenceSourceKind.Documentation,
                "https://web.archive.org/web/20081017160137id_/http://www.rgtr.com:80/news/patch_notes/deployment_134_10152008.html",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionconversation.pyo_dis", 0.95,
                "D13 notes confirm normal failed-timer retry semantics; client text 21566 preserves the ten-minute retry copy");
            yield return Evidence(1995, 4, MissionEvidenceOwnerKind.Scenario, 2, MissionEvidenceSourceKind.Reconstruction,
                "https://raw.githubusercontent.com/Blizz127/tabula-rasa-server/2f0cbbdfe4bb8440286261b205f78d75fca85d0c/docs/evidence/bootcamp-d11-positions.json",
                @"C:\Users\johmil\Projects\trpython\data\generated\client\language\english\missiontextlanguage.pyo_dis", 0.75,
                "Alia Das arrival point and Rogers handoff are measured from late-game footage and snapped to the wilderness navmesh");

            yield return Evidence(2005, 1, MissionEvidenceOwnerKind.Mission, 2005, MissionEvidenceSourceKind.Client, null,
                @"C:\Users\johmil\Projects\trpython\data\generated\client\missionconversation.pyo_dis", 1.0,
                "missionconversation 2005/1-6 identifies the retry mission and its two objective ids");
            yield return Evidence(2005, 2, MissionEvidenceOwnerKind.Mission, 2005, MissionEvidenceSourceKind.Documentation,
                "https://web.archive.org/web/20090203215348/http://www.playtr.com:80/news/patch_notes/deployment_116_8152008.html",
                @"C:\Users\johmil\Projects\trpython\trpython\client\clientmethod.py", 0.9,
                "Boot Camp completion unlocks account-wide skip; runtime uses generic entitlement and qualification steps");
        }

        private static object[] Creature(
            uint id,
            string comment,
            uint classId,
            uint faction,
            uint level,
            uint maxHp,
            uint nameId) =>
            new object[] { id, comment, classId, faction, level, maxHp, nameId, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U };

        private static object[] SpawnPool(
            uint id,
            double x,
            double y,
            double z,
            double rotation,
            uint mapContextId,
            uint creatureId,
            string comment) =>
            new object[] { id, 0U, 0U, 20U, x, y, z, rotation, mapContextId, creatureId, 1U, 1U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U, 0U };

        private static object[] Objective(
            uint missionId,
            uint objectiveId,
            uint clientNameTextId,
            uint clientBodyTextId,
            uint ordinal,
            string comment,
            uint? counter0TextId = null,
            byte initialState = ObjectiveIncompleteState) =>
            new object[]
            {
                missionId, Revision, objectiveId, MissionContentRequirement.Required, clientNameTextId, clientBodyTextId,
                counter0TextId, null, null, ordinal, initialState, true, comment
            };

        private static object[] Transition(uint missionId, uint objectiveId, uint transitionId, string comment) =>
            Transition(missionId, objectiveId, transitionId, ObjectiveCompletedState, comment);

        private static object[] Transition(uint missionId, uint objectiveId, uint transitionId, byte toState, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, MissionContentRequirement.Required, 1U,
                ObjectiveIncompleteState, toState, comment
            };

        private static object[] ConversationTrigger(
            uint missionId,
            uint objectiveId,
            uint transitionId,
            uint triggerId,
            uint packageId,
            uint playerFlagId,
            string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, triggerId, MissionContentRequirement.Required,
                MissionTriggerKind.Conversation, 1U, null, null, null, null, null, null, null, null, null, packageId, playerFlagId, null, comment
            };

        private static object[] ProgressTrigger(
            uint missionId,
            uint objectiveId,
            uint transitionId,
            uint triggerId,
            byte eventKind,
            uint subjectId,
            string comment,
            uint? counterId = null,
            bool? sourceSpawnResolved = null) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, triggerId, MissionContentRequirement.Required,
                MissionTriggerKind.ProgressEvent, 1U, null, null, (byte)eventKind, subjectId, counterId, null, null, null, null, null, null, sourceSpawnResolved, comment
            };

        private static object[] TimerTrigger(
            uint missionId,
            uint objectiveId,
            uint transitionId,
            uint triggerId,
            uint durationSeconds,
            string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, triggerId, MissionContentRequirement.Required,
                MissionTriggerKind.TimerElapsed, 1U, null, null, null, null, null, null, null, null, durationSeconds, null, null, null, comment
            };

        private static object[] AreaTrigger(
            uint missionId,
            uint objectiveId,
            uint transitionId,
            uint areaId,
            string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, 1U, MissionContentRequirement.Required,
                MissionTriggerKind.AreaEntered, 1U, null, null, null, null, null, null, null, areaId, null, null, null, null, comment
            };

        private static object[] CompleteAction(uint missionId, uint objectiveId, uint transitionId, uint actionId, uint targetObjectiveId, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, actionId, MissionContentRequirement.Required,
                MissionActionKind.CompleteObjective, actionId, targetObjectiveId, ObjectiveCompletedState,
                null, null, null, null, null, null, comment
            };

        private static object[] RevealAction(uint missionId, uint objectiveId, uint transitionId, uint actionId, uint targetObjectiveId, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, actionId, MissionContentRequirement.Required,
                MissionActionKind.RevealObjective, actionId, targetObjectiveId, null,
                null, null, null, null, null, null, comment
            };

        private static object[] ActivateAction(uint missionId, uint objectiveId, uint transitionId, uint actionId, uint targetObjectiveId, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, actionId, MissionContentRequirement.Required,
                MissionActionKind.ActivateObjective, actionId, targetObjectiveId, ObjectiveIncompleteState,
                null, null, null, null, null, null, comment
            };

        private static object[] RewardAction(uint missionId, uint objectiveId, uint transitionId, uint actionId, uint rewardId, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, actionId, MissionContentRequirement.Required,
                MissionActionKind.GrantReward, actionId, null, null,
                rewardId, null, null, null, null, null, comment
            };

        private static object[] StartScenarioAction(uint missionId, uint objectiveId, uint transitionId, uint actionId, uint scenarioId, string comment) =>
            new object[]
            {
                missionId, Revision, objectiveId, transitionId, actionId, MissionContentRequirement.Required,
                MissionActionKind.StartScenario, actionId, null, null,
                null, null, scenarioId, null, null, null, comment
            };

        private static object[] RewardItem(
            uint missionId,
            uint rewardId,
            uint itemId,
            MissionRewardItemKind kind,
            uint itemTemplateId,
            uint quantity) =>
            new object[] { missionId, Revision, rewardId, itemId, kind, itemTemplateId, quantity };

        private static object[] Indicator(
            uint missionId,
            uint objectiveId,
            uint indicatorId,
            double x,
            double y,
            double z,
            double radius,
            string comment) =>
            new object[] { missionId, Revision, objectiveId, indicatorId, MissionContentRequirement.Required, x, y, z, radius, true, comment };

        private static object[] Area(
            uint missionId,
            uint areaId,
            double x,
            double y,
            double z,
            double radius,
            string comment) =>
            new object[]
            {
                missionId, Revision, areaId, MissionContentRequirement.Required, BootcampMapContextId,
                MissionAreaShape.Sphere, x, y, z, radius, null, null, null, comment
            };

        private static object[] SpawnGroup(
            uint missionId,
            uint spawnGroupId,
            uint mapContextId,
            bool enabled,
            uint? areaId,
            string comment) =>
            new object[] { missionId, Revision, spawnGroupId, MissionContentRequirement.Required, areaId, mapContextId, enabled, 1U, comment };

        private static object[] Spawn(
            uint missionId,
            uint spawnGroupId,
            uint spawnId,
            uint creatureId,
            double x,
            double y,
            double z,
            double rotation,
            uint quantity) =>
            new object[] { missionId, Revision, spawnGroupId, spawnId, creatureId, x, y, z, rotation, quantity };

        private static object[] Scenario(uint missionId, uint scenarioId, string name, string comment) =>
            new object[] { missionId, Revision, scenarioId, MissionContentRequirement.Required, name, comment };

        private static object[] SpawnDynamicObjectStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            string key,
            uint entityClassId,
            double x,
            double y,
            double z,
            double rotation,
            bool enabled,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.SpawnDynamicObject, stepId, null, null, null, null,
                key, entityClassId, null, null, null, null, null, null, null, null, null, null,
                null, x, y, z, rotation, enabled, null, null, null, comment
            };

        private static object[] SpawnGroupStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint spawnGroupId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.SpawnGroup, stepId, null, null, spawnGroupId, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] EscortSpawnGroupStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint spawnGroupId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.EscortSpawnGroup, stepId, null, null, spawnGroupId, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] DespawnDynamicObjectStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            string key,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.DespawnDynamicObject, stepId, null, null, null, null,
                key, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] GrantRewardPackageStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint rewardId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.GrantRewardPackage, stepId, null, rewardId, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] GrantSkillAbilityStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint skillId,
            uint abilityId,
            byte skillLevel,
            byte abilitySlot,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.GrantSkillAbility, stepId, null, null, null, null,
                null, null, null, null, skillId, abilityId, skillLevel, abilitySlot, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] PlayTutorialStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint tutorialId,
            uint? audioSetId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.PlayTutorial, stepId, null, null, null, null,
                null, null, null, null, null, null, null, null, tutorialId, audioSetId, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] StartDeadlineStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint delayMilliseconds,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.StartDeadline, stepId, null, null, null, null,
                null, null, null, delayMilliseconds, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] CancelDeadlineStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.CancelDeadline, stepId, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] RevealObjectiveStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint targetObjectiveId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.RevealObjective, stepId, targetObjectiveId, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] ActivateObjectiveStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint targetObjectiveId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.ActivateObjective, stepId, targetObjectiveId, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] FailObjectiveStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint targetObjectiveId,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.FailObjective, stepId, targetObjectiveId, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] ScheduleScenarioStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint targetScenarioId,
            uint delayMilliseconds,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.ScheduleScenario, stepId, null, null, null, null,
                null, null, targetScenarioId, delayMilliseconds, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, comment
            };

        private static object[] TransferPlayerStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            uint mapContextId,
            double x,
            double y,
            double z,
            double rotation,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.TransferPlayer, stepId, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                mapContextId, x, y, z, rotation, null, null, null, null, comment
            };

        private static object[] QualificationStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            CharacterQualificationKey qualificationKey,
            byte qualificationValue,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.SetQualification, stepId, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, qualificationKey, qualificationValue, null, comment
            };

        private static object[] SkipEntitlementStep(
            uint missionId,
            uint scenarioId,
            uint stepId,
            bool accountSkipEntitlement,
            string comment) =>
            new object[]
            {
                missionId, Revision, scenarioId, stepId, MissionContentRequirement.Required,
                MissionScenarioStepKind.SetAccountSkipEntitlement, stepId, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, accountSkipEntitlement, comment
            };

        private static object[] Evidence(
            uint missionId,
            uint evidenceId,
            MissionEvidenceOwnerKind ownerKind,
            uint ownerId,
            MissionEvidenceSourceKind sourceKind,
            string sourceUri,
            string localClientPath,
            double confidence,
            string note) =>
            new object[] { missionId, Revision, evidenceId, ownerKind, ownerId, sourceKind, sourceUri, localClientPath, confidence, note };
    }

    public sealed class BootcampMissionNpcCreaturePreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, CreatureEntry.TableName, typeof(CreatureEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.NpcCreatures();
    }

    public sealed class BootcampMissionNpcAppearancePreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, CreatureAppearanceEntry.TableName, typeof(CreatureAppearanceEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.NpcAppearances();
    }

    public sealed class BootcampMissionNpcPackagePreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, NpcPackageEntry.TableName, typeof(NpcPackageEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.NpcPackages();
    }

    public sealed class BootcampMissionNpcSpawnpoolPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, SpawnPoolEntry.TableName, typeof(SpawnPoolEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.NpcSpawnpools();
    }

    public sealed class BootcampMissionContentDefinitionPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionContentDefinitionEntry.TableName, typeof(MissionContentDefinitionEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionDefinitions();
    }

    public sealed class BootcampMissionPrerequisitePreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionPrerequisiteEntry.TableName, typeof(MissionPrerequisiteEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionPrerequisites();
    }

    public sealed class BootcampMissionObjectiveDefinitionPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionObjectiveDefinitionEntry.TableName, typeof(MissionObjectiveDefinitionEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionObjectives();
    }

    public sealed class BootcampMissionObjectiveTransitionPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionObjectiveTransitionEntry.TableName, typeof(MissionObjectiveTransitionEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionTransitions();
    }

    public sealed class BootcampMissionTriggerPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionTriggerEntry.TableName, typeof(MissionTriggerEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionTriggers();
    }

    public sealed class BootcampMissionActionPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionActionEntry.TableName, typeof(MissionActionEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionActions();
    }

    public sealed class BootcampMissionRewardDefinitionPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionRewardDefinitionEntry.TableName, typeof(MissionRewardDefinitionEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionRewards();
    }

    public sealed class BootcampMissionRewardItemPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionRewardItemEntry.TableName, typeof(MissionRewardItemEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionRewardItems();
    }

    public sealed class BootcampMissionIndicatorPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionIndicatorEntry.TableName, typeof(MissionIndicatorEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionIndicators();
    }

    public sealed class BootcampMissionAreaPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionAreaEntry.TableName, typeof(MissionAreaEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionAreas();
    }

    public sealed class BootcampMissionSpawnGroupPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionSpawnGroupEntry.TableName, typeof(MissionSpawnGroupEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionSpawnGroups();
    }

    public sealed class BootcampMissionSpawnPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionSpawnEntry.TableName, typeof(MissionSpawnEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionSpawns();
    }

    public sealed class BootcampMissionScenarioPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionScenarioEntry.TableName, typeof(MissionScenarioEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionScenarios();
    }

    public sealed class BootcampMissionScenarioStepPreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionScenarioStepEntry.TableName, typeof(MissionScenarioStepEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionScenarioSteps();
    }

    public sealed class BootcampMissionEvidencePreloader : PreloaderBase, IPreloader
    {
        public void Preload(MigrationBuilder migrationBuilder) => BootcampWorldContentSeedData.Insert(migrationBuilder, MissionEvidenceEntry.TableName, typeof(MissionEvidenceEntry), GetRows());
        protected override IEnumerable<object[]> GetRows() => BootcampWorldContentSeedData.MissionEvidence();
    }
}
