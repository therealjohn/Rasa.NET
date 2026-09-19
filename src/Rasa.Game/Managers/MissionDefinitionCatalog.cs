using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Rasa.Managers
{
    using Data;
    using Structures;

    public static class MissionDefinitionCatalog
    {
        public static IReadOnlyDictionary<uint, Mission> CreateRecoveredInactiveDefinitions()
        {
            var definitions = new Dictionary<uint, Mission>
            {
                [1069] = RecoveredMission(
                    1069,
                    "Receptive Reception",
                    6038,
                    Objective(1, 6042, 6620,
                        progressRule: MissionProgressRule.CompleteOnExactSubject(
                            MissionProgressEventKind.LogosAcquired, 10)),
                    Objective(2, 13793, 13794,
                        conversations: new[] { Completion(168, 1) }),
                    Objective(3, 13796, 13797,
                        conversations: new[] { Completion(112, 1) })),
                [1407] = RecoveredMission(
                    1407,
                    "Too Close For Comfort",
                    12099,
                    Objective(1, 12104, 12105,
                        conversations: new[] { Completion(113, 1) }),
                    Objective(10, 12128, 12129,
                        conversations: new[] { Completion(168, 1) })),
                [1449] = RecoveredMission(
                    1449,
                    "Wilderness Targets of Opportunity",
                    12764,
                    Objective(1, 12769, 12770, 12789,
                        progressRule: MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                            MissionProgressEventKind.WaypointAcquired,
                            new HashSet<uint> { 49, 50, 51, 57, 61, 73, 156 })),
                    Objective(3, 12773, 12774, 12805),
                    Objective(4, 12775, 12776, 12808),
                    Objective(5, 12771, 12772, 12802),
                    Objective(6, 12777, 12778, 12809),
                    Objective(7, 12779, 12780, 12824),
                    Objective(8, 12781, 12782,
                        progressRule: MissionProgressRule.CompleteWhenAllDistinctSubjectsObserved(
                            MissionProgressEventKind.LogosAcquired,
                            new HashSet<uint> { 1, 2, 6, 9, 10, 23, 24, 28, 38, 49, 53, 56 })),
                    Objective(20, 12810, 12811,
                        progressRule: Creature(82)),
                    Objective(21, 12812, 12813,
                        progressRule: Creature(83)),
                    Objective(22, 12814, 12815,
                        progressRule: Creature(84)),
                    Objective(23, 12816, 12817,
                        progressRule: Creature(79, false)),
                    Objective(24, 12818, 12819,
                        progressRule: Creature(80)),
                    Objective(25, 12820, 12821,
                        progressRule: Creature(75)),
                    Objective(40, 12852, 12853, 12854),
                    Objective(41, 12855, 12856),
                    Objective(46, 12859, 12860),
                    Objective(47, 12861, 12862),
                    Objective(48, 12863, 12864, 12865),
                    Objective(49, 12866, 12867),
                    Objective(50, 12868, 12869),
                    Objective(51, 12870, 12871),
                    Objective(52, 12872, 12873),
                    Objective(53, 12874, 12875),
                    Objective(54, 12876, 12877),
                    Objective(55, 13213, 13214, 13685),
                    Objective(58, 17853, 17854))
            };
            return new ReadOnlyDictionary<uint, Mission>(definitions);
        }

        private static Mission RecoveredMission(
            uint missionId,
            string name,
            uint clientNameTextId,
            params MissionObjectiveDefinition[] objectives) =>
            new(
                missionId,
                name,
                clientNameTextId,
                missionGiver: null,
                missionReciver: null,
                level: null,
                groupType: null,
                categoryId: null,
                shareable: null,
                radioCompletable: null,
                objectives,
                enableOperational: true);

        private static MissionObjectiveDefinition Objective(
            uint objectiveId,
            uint nameTextId,
            uint bodyTextId,
            uint? counterTextId = null,
            IReadOnlyList<MissionObjectiveConversation> conversations = null,
            MissionProgressRule progressRule = null) =>
            new(
                objectiveId,
                nameTextId,
                bodyTextId,
                counterTextId.HasValue
                    ? new uint?[] { counterTextId, null, null }
                    : new uint?[] { null, null, null },
                ordinal: null,
                initialState: null,
                isRequired: null,
                counters: new Dictionary<uint, MissionObjectiveCounterDefinition>(),
                itemCounters: new Dictionary<uint, MissionObjectiveItemCounterDefinition>(),
                conversations: conversations ?? Array.Empty<MissionObjectiveConversation>(),
                progressRule: progressRule);

        private static MissionObjectiveConversation Completion(uint npcPackageId, uint playerFlagId) =>
            new(npcPackageId, playerFlagId, MissionObjectiveConversationType.Completion);

        private static MissionProgressRule Creature(
            uint creatureId,
            bool? spawnResolved = null) =>
            MissionProgressRule.CompleteOnExactSubject(
                MissionProgressEventKind.CreatureKilled,
                creatureId,
                spawnResolved);
    }
}
