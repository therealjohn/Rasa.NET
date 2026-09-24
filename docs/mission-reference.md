# Mission data and script reference

Use [Mission authoring and operations](missions.md) for the migration workflow.
Mission content is authored in C# and deployed by EF migrations. No pack
envelope, release manifest or publish command is required.

## Source map

| Contract | Source |
| --- | --- |
| Shared scene and experience definitions | [MissionSceneDefinition.cs](../src/Rasa.Missions/Content/MissionSceneDefinition.cs) |
| Internal scene serialization | [MissionContentCodec.cs](../src/Rasa.Missions/Content/MissionContentCodec.cs) |
| Migration helpers | [MissionDataMigration.cs](../src/Rasa.DBL/Services/Preloader/Missions/MissionDataMigration.cs) |
| Fixed Bootcamp migration data | [BootcampMissionDataV1.cs](../src/Rasa.DBL/Services/Preloader/Missions/BootcampMissionDataV1.cs) |
| Normalized row fields | `src\Rasa.DBL\Structures\World\Mission*Entry.cs` |
| Objective trigger shapes | [MissionObjectiveRuntimeAnalyzer.cs](../src/Rasa.Game/Managers/MissionObjectiveRuntimeAnalyzer.cs), [MissionProgressRuleAuthoring.cs](../src/Rasa.Game/Managers/MissionProgressRuleAuthoring.cs) |
| Scene observations, intents and bindings | [SceneContracts.cs](../src/Rasa.Missions/Scenes/SceneContracts.cs) |
| Script discovery and sequences | [SceneScriptRegistry.cs](../src/Rasa.Missions/Scenes/SceneScriptRegistry.cs) |
| Predicates and handlers | [MissionRequirements.cs](../src/Rasa.Missions/Runtime/MissionRequirements.cs) |
| Authoritative fact providers | [MissionRequirementFactsAdapter.cs](../src/Rasa.Game/Missions/Persistence/MissionRequirementFactsAdapter.cs) |

## Migrated definitions

Normalized rows repeat `MissionId` and `ContentRevision`. Revisions are nonempty
strings of at most 32 characters. Objective, transition, reward and scenario
cross-links stay within that mission/revision unless a field explicitly names
another mission.

`MissionContentDefinitionEntry.Enabled` controls runtime selection. Keep
unreconstructed/example data disabled and enable at most one revision per
mission. A migration can update an enabled definition; there is no separate
release-immutability rule.

`Requirement` on normalized rows is `MissionContentRequirement.Required` or
`Optional`, describing validation criticality. It is not a player-eligibility
predicate. `AbandonmentPolicy` is `Allowed` or `Prohibited`.

`GiverId` and `ReceiverId` identify creature definitions. Conversation triggers
use NPC **package** IDs; public actor reservations use static **spawn** IDs.
Keep those namespaces distinct.

`Shareable` and `RadioCompleteable` are client metadata, not automatic group
acceptance or implementation of every native sharing flow. Supply real native
mission/objective/text IDs and source evidence. A nonzero confidence value does
not verify the source.

## Objective triggers

Use one executable trigger shape per transition. Mixing conversation, progress,
area, objective-state and timer trigger shapes in one transition is rejected.

| `MissionTriggerKind` | Required binding |
| --- | --- |
| `Conversation` | `NpcPackageId`, `PlayerFlagId` |
| `ProgressEvent` | `EventKind` and event-specific parameters below |
| `AreaEntered` | `AreaId` |
| `ObjectiveState` | `RelatedObjectiveId`, `RelatedState` |
| `TimerElapsed` | Positive `DurationSeconds` |

Progress event values come from `MissionProgressEventKind`, not client opcodes:

| Value | Event | Parameter meaning |
| --- | --- | --- |
| 0 | `WaypointAcquired` | `SubjectId` is a waypoint ID |
| 1 | `LogosAcquired` | `SubjectId` is a Logos ID |
| 2 | `CreatureKilled` | `SubjectId` is a creature ID |
| 3 | `MissionCompleted` | `SubjectId` is the mission ID |
| 4, 5 | `ItemAcquired`, `ItemConsumed` | `SubjectId` is an item class, not template ID |
| 6 | `InteractionUsed` | `SubjectId` is the interacted entity class |
| 7 | `AreaEntered` | Prefer the typed area trigger |
| 8 | `ItemEquipped` | `SubjectId` is class, or template with `SourceSpawnResolved = true` |
| 9 | `AbilityHit` | `SubjectId` is action ID; `CounterId` is target creature ID |
| 10 | `ScenarioEvent` | `SubjectId` is signal event ID; `CounterId` is scenario/sequence ID |
| 11 | `DeadlineElapsed` | Prefer `TimerElapsed`; runtime supplies mission/objective scope |
| 12 | `ObjectiveStateReached` | Prefer the typed objective-state trigger |
| 13 | `ObjectHit` | `SubjectId` is entity class; `CounterId` is action ID |

For item counters, set `InitialValue` and `TargetValue`, omit `CounterId`, and
provide the native counter-text binding. Generic counters require all three.
Targets must exceed initial values. Do not combine counter ranges with
`SourceSpawnResolved`.

Multiple progress triggers form an all-distinct-subject set only for waypoints
or Logos; they are not a general AND expression. Counter text uses
`ClientCounter0TextId`, `ClientCounter1TextId`, `ClientCounter2TextId`.

Objective states are `NotAssigned = 0` (revealed), `Incomplete = 1` (active),
`Completed = 2`, `Failed = 3`, `Inactive = 4` (hidden). Only `Inactive` is omitted
from client projections. Hidden objectives remain in definitions and storage.

| `MissionActionKind` | Binding |
| --- | --- |
| `RevealObjective` | `TargetObjectiveId` |
| `ActivateObjective` | `TargetObjectiveId`, `ObjectiveState` |
| `CompleteObjective` | Current transition's `TargetObjectiveId`, `ObjectiveState` |
| `GrantReward` | `RewardId`, the turn-in package rather than an immediate grant |
| `StartScenario` | `ScenarioId`, also declared in the typed scene |
| `ActivateSpawnGroup` | `SpawnGroupId`; bind `Names["spawn-group-<id>"]` to its sequence |
| `ShowIndicator` | `IndicatorId` |
| `SetPlayerFlag` | `PlayerFlagId`, `PlayerFlagValue` |
| `ShowAmbientConversation` | `NpcPackageId` is the greeting text ID for this historical shape |

Reward rows distinguish fixed and selectable items. At most one turn-in reward
ID is selected per mission; selectable rewards require a native selection.
Scene reward grants cannot contain selectable alternatives.

## Requirements

Bind `MissionRequirement` expressions to `MissionSceneDefinition.Requirement`,
`ObjectiveRequirements[objectiveId]`, or `TurnInRequirement`.

| C# type | Arguments |
| --- | --- |
| `AllRequirements`, `AnyRequirement` | List of expressions |
| `NotRequirement` | One expression |
| `LevelRequirement` | Minimum level |
| `MissionStateRequirement` | Mission ID, optional state, optional accepted flag |
| `FlagRequirement` | Server-owned flag ID and value |
| `CustomRequirement` | Registered pure handler key |

```csharp
var requirement = new AllRequirements(new MissionRequirement[]
{
    new LevelRequirement(4),
    new NotRequirement(new CustomRequirement("character.starting-experience-completed"))
});
```

`MissionStateRequirement.Accepted = true` requires journal presence; otherwise
history can satisfy it. With no explicit state, completion accepts `Success` or
`Completed`. These are mission states, not objective states.

Current custom keys are `example.even-level`,
`account.starting-experience-entitlement`, and
`character.starting-experience-completed`. Handlers declare their required
boolean facts; Game must provide them for both runtime queries and durable
transactions. Unknown handlers/facts fail explicitly. Nesting is bounded to 32.

## Scene definitions

`MissionSceneDefinition` contains `Script`, `StateVersion`, role-keyed `Actors`,
named `Routes`, numeric `Sequences`, named sequence entry points, eligibility
requirements, `Credit`, and an optional `PublicEncounter`.

Scripts receive only `SceneContext` and `SceneObservation`, not clients or EF
contexts. Decisions contain typed intents, signals, timers and JSON checkpoints.
Internal database/checkpoint JSON does not introduce a file-based publish step.

| `SceneActorKind` | `TemplateId` means |
| --- | --- |
| `PublicSpawn` | Exact existing static spawn-pool ID |
| `Creature` | Creature definition ID |
| `Object`, `PracticeTarget` | Entity class ID |

Role dictionary keys must equal `SceneActorDefinition.Role`. Created actors need
`ScenePosition(X, Y, Z)`, with Y as height. Other fields include `Orientation`,
`InitiallyInteractable`, `InitialObjectState`, `WindupMilliseconds`, loot
bindings and mission/group/spawn attribution.

`SharedKey` is for private experience-owned actors. It does not make personal
copies of main-world NPCs. Keep the role consistent between mission and
experience definitions.

`SceneRoute` contains ordered `SceneWaypoint` records, speed in metres/second
and optional `ResumeAtDestination`. Waypoint orientation is radians; pauses are
milliseconds. Scripted movement needs a complete loaded-navmesh route.

| World intent | Purpose |
| --- | --- |
| `EnsureActorIntent` | Ensure the authored actor is present |
| `RemoveActorIntent` | Remove/release its role |
| `SetInteractionIntent` | Set enabled/object-state properties |
| `RunRouteIntent` | Run an authored route |
| `FollowActorIntent` | Follow a character; ID zero means owner |
| `PresentationIntent` | Tutorial, audio or greeting using an existing ID |
| `TransferIntent` | Transfer to a map and authored position |
| `RestoreActorPoseIntent` | Recover a static actor in its owning private map |

Character intents are `GrantRewardIntent`, `GrantAbilityIntent`,
`SetQualificationIntent`, `SetEntitlementIntent`, `ObjectiveIntent`, and
`MissionDeadlineIntent`. Game applies them through its transaction adapters.

Operation keys are nonempty, at most 96 characters and unique across a scene's
authored sequences. Timers have names of at most 64 characters, a target sequence
and `WallClock` or `ActiveScene` policy. One decision permits at most 64 world
intents, character intents and signals combined. A checkpoint is a JSON object
limited to 16,384 UTF-8 bytes.

## Public encounters and private experiences

`PublicEncounterBinding` identifies mission, spawn, role, script and owner-loss
policy (`Reset`, `Wait`, `Continue`). Main-world NPCs remain public; admission
reserves the existing spawn. Deliberate initiator abandonment cancels its run.

`MissionCreditPolicy` supports `Personal`, `NearbyParty` and
`EncounterParticipants`. Shared modes require a positive finite radius and
matching active objectives; participant mode also requires participation.
Only eligible creature-kill/scenario-event objectives share credit, not personal
dialogue, inventory actions, acceptance or reward choices.

`MissionExperienceDefinition` has a key, revision, map context, private-per-character
flag, scene, mission triggers and actor policies. Triggers respond to `Accepted`
or `Rewarded` for a mission and name an experience sequence.

Actor policies can configure invulnerability, defense, participation,
scenario-kill rewards and loot. The fixed C# Bootcamp example is
[BootcampMissionDataV1.Experience.cs](../src/Rasa.DBL/Services/Preloader/Missions/BootcampMissionDataV1.Experience.cs).

## Migration helper interface

All helpers take a `MigrationBuilder`. Provider migration classes can share
these C# operations:

| Method | Operation |
| --- | --- |
| `EnableMission` | Set the selected definition's enabled flag |
| `InsertScene`, `UpdateScene` | Write a typed scene for a mission/revision |
| `InsertExperience`, `UpdateExperience` | Write a typed private experience and enabled flag |

Use ordinary EF data operations for normalized rows. Keep migration-owned data
fixed, add new migrations for changes, and scaffold provider schema changes
with EF rather than hand-editing generated snapshots.
