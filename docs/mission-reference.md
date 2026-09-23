# Mission pack and script reference

Use [Mission authoring and operations](missions.md) for the end-to-end workflow.
This reference describes the implemented format, not a proposed general-purpose
scripting language.

## Source map

| Contract | Source |
| --- | --- |
| Pack, scene, experience and client-manifest documents | [MissionPackDocument.cs](../src/Rasa.Game/Missions/Content/MissionPackDocument.cs) |
| JSON serialization | [MissionPackCodec.cs](../src/Rasa.Game/Missions/Content/MissionPackCodec.cs) |
| Pack/scene validation and publication | [MissionPackValidation.cs](../src/Rasa.Game/Missions/Content/MissionPackValidation.cs), [MissionPackStore.cs](../src/Rasa.Game/Missions/Content/MissionPackStore.cs) |
| Normalized row fields | `src\Rasa.DBL\Structures\World\Mission*Entry.cs` |
| Executable objective trigger shapes | [MissionObjectiveRuntimeAnalyzer.cs](../src/Rasa.Game/Managers/MissionObjectiveRuntimeAnalyzer.cs), [MissionProgressRuleAuthoring.cs](../src/Rasa.Game/Managers/MissionProgressRuleAuthoring.cs) |
| Scene observations, intents and bindings | [SceneContracts.cs](../src/Rasa.Missions/Scenes/SceneContracts.cs) |
| Script discovery and data sequences | [SceneScriptRegistry.cs](../src/Rasa.Missions/Scenes/SceneScriptRegistry.cs) |
| Predicates and registered requirement handlers | [MissionRequirements.cs](../src/Rasa.Missions/Runtime/MissionRequirements.cs) |
| Authoritative custom fact providers | [MissionRequirementFactsAdapter.cs](../src/Rasa.Game/Missions/Persistence/MissionRequirementFactsAdapter.cs) |
| CLI options and database handling | [Program.cs](../src/Rasa.MissionTool/Program.cs) |

## JSON conventions

Properties are case-sensitive camelCase. Unknown properties fail deserialization.
Use JSON enum names with their defined case, such as `"ProgressEvent"` or
`"PublicSpawn"`. Fields declared as bytes, such as normalized `eventKind`,
`initialState`, `fromState` and `toState`, remain numeric.

Polymorphic predicates and intents use a leading `"$kind"` discriminator.
Dictionary keys, including numeric objective/sequence IDs, are JSON strings.
EF navigation properties are excluded: packs carry rows and keys, not nested
EF object graphs. Optional nullable fields may be omitted; do not populate
columns belonging to another trigger/action kind.

### Pack envelope

| Field | Meaning |
| --- | --- |
| `schemaVersion` | `1` |
| `release` | Nonempty release name, at most 32 characters; identical on every pack in one publication |
| `enabled` | Whether this mission/experience is active in the selected release |
| `synthetic` | Test/example content; an enabled synthetic pack is rejected |
| `definition` | Normalized mission identity, client metadata and abandonment policy |
| `prerequisites` | Existing mission-state, level and flag prerequisite rows |
| `objectives`, `transitions`, `triggers`, `actions` | Objective graph and executable rules |
| `rewards`, `rewardItems` | Reward packages and fixed/selectable items |
| `indicators`, `areas` | Client navigation indicators and authoritative area triggers |
| `spawnGroups`, `spawns` | Normalized spawn references; not a replacement for typed scene actor/sequence bindings |
| `scenarios`, `scenarioSteps` | Normalized scenario declarations and legacy data; step rows alone are not runtime scripts |
| `evidence` | Reviewed source locations and reconstruction notes |
| `scene` | Optional typed script/sequence, actor, requirement and credit bindings |
| `experience` | Alternative private-map experience document; not a second mission in the same pack |

Every normalized row repeats its mission's `missionId` and `contentRevision`.
Revisions are nonempty strings of at most 32 characters. Cross-links such as
`objectiveId`, `transitionId`, `rewardId` and `scenarioId` refer to rows within
that mission/revision unless the field explicitly names another mission.

`definition.requirement` and the same field on normalized rows mean
`"Required"` or `"Optional"` content validation criticality. They are not player
eligibility predicates. `definition.abandonmentPolicy` is `"Allowed"` or
`"Prohibited"`. `scene.requirement` is a separate player-admission expression.

`giverId` and `receiverId` identify creature definitions. Conversation triggers
use NPC **package** IDs; public actor leases use **spawn** IDs. Do not substitute
one ID namespace for another. Preserve the existing JSON spelling
`radioCompleteable`.

`shareable` and `radioCompleteable` are client metadata, not automatic
implementation of all native share/radio flows. The current admission path for
radio offers is the starting-experience offer. Group credit is configured
separately in `scene.credit`.

### Client manifest and evidence

`client-bindings.json` contains a `source` description and a `missions`
dictionary. This is a fragment showing an existing binding, not a full release:

```json
{
  "source": "Reviewed Deployment 11 bindings",
  "missions": {
    "1990": {
      "nameTextId": 21132,
      "objectives": {
        "1": { "nameTextId": 21148, "bodyTextId": 21149 }
      }
    }
  }
}
```

Enabled missions must match the manifest and include evidence. Evidence rows
use `ownerKind`/`ownerId`, `sourceKind`, a `sourceUri` or `localClientPath`,
`confidence`, and `reconstructionNote`. A generated manifest or nonzero
confidence does not verify a source. Check counter text, greetings, assets and
behavior against their actual sources as well; the manifest is not an exhaustive
client/content verifier.

## Objective triggers

Each transition has a sequence, source/destination objective states and typed
triggers/actions. Use one executable trigger shape per transition. Mixing
conversation, progress, area, objective-state and timer triggers in one
transition is rejected.

| Trigger `kind` | Required binding |
| --- | --- |
| `Conversation` | `npcPackageId`, `playerFlagId`; matches the requesting character's native objective-completion choice |
| `ProgressEvent` | Numeric `eventKind` and the event-specific parameters below |
| `AreaEntered` | `areaId` referencing an authored area |
| `ObjectiveState` | `relatedObjectiveId`, numeric `relatedState` |
| `TimerElapsed` | Positive `durationSeconds` for that objective's deadline |

Progress event IDs come from `MissionProgressEventKind`, not client opcodes:

| `eventKind` | Event | Parameter meaning |
| --- | --- | --- |
| `0` | `WaypointAcquired` | `subjectId` is a waypoint ID |
| `1` | `LogosAcquired` | `subjectId` is a Logos ID |
| `2` | `CreatureKilled` | `subjectId` is a creature ID; `sourceSpawnResolved` records source resolution when used |
| `3` | `MissionCompleted` | `subjectId` is the completed mission ID |
| `4`, `5` | `ItemAcquired`, `ItemConsumed` | `subjectId` is an item **class**, not item-template ID |
| `6` | `InteractionUsed` | `subjectId` is the interacted entity class |
| `7` | `AreaEntered` | Prefer the typed `AreaEntered` trigger to bind mission/area scope |
| `8` | `ItemEquipped` | `subjectId` is an item class, or a template when `sourceSpawnResolved: true` |
| `9` | `AbilityHit` | `subjectId` is action ID; `counterId` is target creature ID |
| `10` | `ScenarioEvent` | `subjectId` is signal event ID; `counterId` is scenario/sequence ID |
| `11` | `DeadlineElapsed` | Prefer `TimerElapsed` with a duration; the runtime supplies mission/objective scope |
| `12` | `ObjectiveStateReached` | Prefer `ObjectiveState` to supply related objective/state scope |
| `13` | `ObjectHit` | `subjectId` is entity class; `counterId` is action ID |

An exact event can complete an objective without a counter. For an item counter,
set `initialValue` and `targetValue`, omit `counterId`, and supply the associated
client counter-text binding. For a generic counter, specify `counterId`,
`initialValue` and `targetValue` together. Targets must exceed initial values.
Do not combine counter ranges with `sourceSpawnResolved`.

Multiple `ProgressEvent` triggers in one transition mean an all-distinct-subject
set **only for waypoints or Logos**. They are not a general AND expression for
arbitrary events. Counter-text fields on objective rows are
`clientCounter0TextId`, `clientCounter1TextId` and `clientCounter2TextId`.

### Objective states and actions

Normalized objective-state values are `0 = NotAssigned` (revealed),
`1 = Incomplete` (active), `2 = Completed`, `3 = Failed`, `4 = Inactive`
(unrevealed). Only `Inactive` is omitted from client objective projections.
Keep hidden objectives in server definitions and durable progress.

| Action `kind` | Parameters and behavior |
| --- | --- |
| `RevealObjective` | `targetObjectiveId`; make a hidden successor visible |
| `ActivateObjective` | `targetObjectiveId`, `objectiveState`; activate an eligible successor |
| `CompleteObjective` | `targetObjectiveId`, `objectiveState`; the transition action planner completes its current objective, not arbitrary unrelated objectives |
| `GrantReward` | `rewardId`; identifies the turn-in package, not an immediate objective-time grant |
| `StartScenario` | `scenarioId`; also bind that ID in the typed scene |
| `ActivateSpawnGroup` | `spawnGroupId`; also bind `scene.names["spawn-group-<id>"]` to its typed spawn sequence |
| `ShowIndicator` | `indicatorId` |
| `SetPlayerFlag` | `playerFlagId`, `playerFlagValue` |
| `ShowAmbientConversation` | `npcPackageId` holds a **greeting text ID** for this action; the required `playerFlagId` column is not an NPC conversation gate here |

The serialized row schema retains some historical overloaded fields. The
validator enforces each kind's allowed shape. Do not infer meaning from a column
name alone.

Authored rewards need a `GrantReward` reference, and at most one distinct
turn-in reward ID is selected per mission. Reward item rows distinguish fixed
and selectable items; selectable rewards require a valid native selection.
Scene reward grants cannot contain selectable alternatives.

## Requirements

These expressions belong in `scene.requirement` (admission),
`scene.objectiveRequirements["<objectiveId>"]` (credit/completion), or
`scene.turnInRequirement`. A requirement-only `scene` may omit `script`.

| `$kind` | Additional fields |
| --- | --- |
| `all`, `any` | `items`: array of requirement expressions |
| `not` | `item`: one expression |
| `level` | `minimum` |
| `mission` | `missionId`; optional enum `state`, optional `accepted` boolean |
| `flag` | `flagId`, `value` |
| `custom` | `key`: registered pure handler |

For `mission`, `accepted: true` requires presence in the journal. Otherwise
history can satisfy it. Without `state`, a completion requirement accepts
`Success` or `Completed`. Explicit mission state names are `Active`, `Success`,
`Failed`, `NotAssigned` and `Completed`; these are not objective states.

Example requirement-only scene fragment:

```json
{
  "requirement": {
    "$kind": "all",
    "items": [
      { "$kind": "level", "minimum": 4 },
      {
        "$kind": "not",
        "item": { "$kind": "custom", "key": "character.starting-experience-completed" }
      }
    ]
  },
  "objectiveRequirements": {
    "1": { "$kind": "flag", "flagId": 7, "value": 1 }
  },
  "turnInRequirement": { "$kind": "level", "minimum": 4 }
}
```

Use only real server-owned flags/IDs established by the mission's content. This
fragment demonstrates shape, not a proposed game rule.

Current custom handler keys and supplied boolean facts:

| Handler key | Required fact |
| --- | --- |
| `example.even-level` | `character.level-is-even` |
| `account.starting-experience-entitlement` | `account.starting-experience-entitled` |
| `character.starting-experience-completed` | `character.starting-experience-completed` |

Handlers implement `IMissionRequirementHandler.RequiredFacts` and
`Evaluate(IReadOnlyDictionary<string, bool>)`. The Game adapter supplies declared
facts for runtime queries and durable transactions. Adding an unknown fact
string is not sufficient. Trees are bounded to 32 levels of nesting.

## Scene bindings and intents

| Scene field | Meaning |
| --- | --- |
| `script`, `stateVersion` | Registered script key and exact checkpoint schema version |
| `actors` | Role-keyed `SceneActorDefinition` records |
| `routes` | Route-keyed ordered waypoints |
| `sequences` | Numeric-keyed lists of world/character intents, signals and timers |
| `names` | Named entry points mapped to sequence IDs |
| `credit` | Objective-ID-keyed group-credit policies |
| `publicEncounter` | Reservation of a public static actor on mission acceptance |
| `recovery` | Accepted metadata values: `RestoreCheckpoint`, `RestartAttempt`, `Fail`; currently not an automatic runtime dispatcher |

`SceneContext` contains `Run`, `Bindings` and `UtcNow`. `SceneRun` supplies
identity, version, generation, checkpoint, status, owning character and mission
ID. It does not expose clients, EF contexts or arbitrary world/player queries.
`SceneObservation` supplies typed lifecycle/callback data. Use requirement
bindings for player eligibility, not storage access from scripts.

### Actors and routes

`actors` keys must equal the embedded `role`. `templateId` means:

| Actor `kind` | `templateId` |
| --- | --- |
| `PublicSpawn` | Exact static spawn-pool ID; use a matching public lease outside a private experience |
| `Creature` | Existing creature definition ID |
| `Object`, `PracticeTarget` | Existing entity class ID |

Created actors need `position: { "x": ..., "y": ..., "z": ... }`. Other fields
include `orientation`, `initiallyInteractable`, `initialObjectState`,
`windupMilliseconds`, loot mission/reward/objective bindings, and
`missionId`/`groupId`/`spawnId` attribution. Use supported native object states.

`sharedKey` is for **private experience-owned** actors, not sharing an NPC among
parties in the main world. Declare the shared role consistently in the
experience and mission bindings. This lets props or a next-mission giver outlive
a cleared assignment. Merely declaring a role does not spawn it; an ensure
operation must execute.

Routes have `key`, `points`, `speed` and `resumeAtDestination`. Each point has a
position, orientation and optional `pauseMilliseconds`. Units are world
coordinates, radians, metres/second and milliseconds. `resumeAtDestination`
is an explicit final-pose restoration choice, not ordinary escort progression.
All scripted routes require complete loaded-navmesh paths.

### Sequence operations

Each `world` intent carries `operationKey` and, where applicable, `role`.

| `$kind` | Additional fields |
| --- | --- |
| `ensure` | Optional `restorePosition` |
| `remove` | None |
| `interaction` | `enabled`; optional `objectState`, `ifPresent` |
| `route` | `route`; optional `startWaypoint` |
| `follow` | `characterId` (`0` means the run owner), optional `enabled` |
| `presentation` | `kind`: `Tutorial`, `Audio` or `Greeting`; `value` is the corresponding existing ID |
| `transfer` | `mapContextId`, `position`, `orientation` |
| `restore-pose` | `position`, `orientation`; restricted to a static actor in the owning private map |

Each `character` intent carries `operationKey`:

| `$kind` | Additional fields |
| --- | --- |
| `reward` | `missionId`, `rewardId` |
| `ability` | `skillId`, `abilityId`, `level`, optional `slot` |
| `qualification` | Numeric `qualification`, `present` |
| `entitlement` | `enabled`; currently the account's Bootcamp-skip entitlement, not a generic entitlement system |
| `objective` | `missionId`, `objectiveId`, objective enum `state` |
| `deadline` | `missionId`, `kind`: `Start`, `Satisfy`, `Cancel`; `milliseconds` for start |

Operation keys are nonempty, at most 96 characters and unique across authored
world/character sequences in a pack. Timers have a nonempty name of at most
64 characters, `milliseconds`, target `sequenceId`, `clockPolicy` (`WallClock`
or `ActiveScene`) and optional `cancel`. A non-cancelled timer must name a
declared target sequence. C# decisions use UTC `DueAtUtc` rather than a
`milliseconds` field.

Signals contain `missionId`, `sequenceId` and `eventId`; receiving objectives
use the `ScenarioEvent` trigger mapping above. One decision allows at most
64 world intents + character intents + signals. Checkpoints must be JSON
objects no larger than 16,384 UTF-8 bytes.

### Public encounters and group credit

`publicEncounter` binds `missionId`, exact `spawnId`, actor `role`, `scriptKey`
and `ownerLossPolicy` (`Reset`, `Wait`, `Continue`). Keep the role/script/spawn
consistent with the scene. The current admission binding reserves one static
actor per mission start; it is not a general multi-NPC reservation language.

The actor stays public. A competing run waits for its return/reset/respawn.
Owner-loss policy describes connection departure; intentional assignment
abandonment cancels that assignment's owned encounter. It does not transfer
ownership to a newly joined party member.

`credit` entries use `mode: "Personal"`, `"NearbyParty"` or
`"EncounterParticipants"`. Non-personal modes require an explicit positive
finite `radius`; participant mode additionally requires run participation.
Sharing is supported for eligible creature-kill/scenario-event objectives, not
talk, item consumption, equipment, acceptance or turn-in choices.
`definition.shareable` does not turn this on.

### Private experience packs

An `experience` replaces the pack's mission rows. It declares `key`, `revision`,
`mapContextId`, `privatePerCharacter: true`, its own `scene`, optional
`missionTriggers`, and optional `actorPolicies`. Experiences currently support
real per-character private maps only; they are not a main-world phasing feature.

Each mission trigger names a `missionId`, `event` (`Accepted` or `Rewarded`) and
the experience's `sequenceId`. Use these for staging shared actors across a
mission chain, rather than coupling their lifetime to a journal entry.
Experience initialization also uses its script's normal `Started` observation.

`actorPolicies` keys are creature definition IDs on that experience's map.
Policies can author invulnerability, defense radius/target tags, participation
tracking, scenario-kill rewards and loot distributions. See
[ActorGameplayPolicy.cs](../src/Rasa.Missions/Definitions/ActorGameplayPolicy.cs)
and the actual [Bootcamp experience](../content/missions/bootcamp/experience.json).
This binding currently comes from experience content; adding a policy to an
ordinary mission as an unknown top-level field does not configure a public map.

## CLI reference

### Explicit PowerShell wrapper

Use `scripts\Update-MissionPacks.ps1` for validation/diff and optional publication
without repeating low-level commands. It requires an existing migrated SQLite
World database and restored build dependencies.

| Parameter | Contract |
| --- | --- |
| `-WorldDatabasePath <file.db>` | Required physical file, including lowercase `.db`; no configuration inference or automatic creation |
| `-PackDirectory <directory>` | Optional complete release directory with `client-bindings.json`; defaults to repository Bootcamp packs |
| `-Publish` | Explicitly publish after successful build, validation and diff; otherwise preview only |

Explicit relative paths are caller-relative. Default assets and the tool project
are script/repository-relative. The wrapper preserves native failure exit codes
and uses exit code `1` for its own validation errors. No interactive prompts,
automatic restore/migration/reset, force publication or startup/build hook is
provided. See the [local and CI examples](missions.md#update-an-existing-world-database).

### Low-level MissionTool

Invoke with:

```powershell
dotnet run --project src\Rasa.MissionTool\Rasa.MissionTool.csproj --configuration Release --no-restore -- <operation> <arguments>
```

| Operation | Effect |
| --- | --- |
| `validate` | Validate packs/native manifest and active normalized content against an existing World database |
| `diff` | Validate, then summarize normalized row/enablement differences; not a full scene/experience or immutability diff |
| `publish` | Validate and transactionally publish an immutable release, selecting it as the database's active release |
| `export` | Export selected effective normalized rows/stored scene bindings and a manifest; does not generically export experience sets |

| Argument | Applies to | Contract |
| --- | --- | --- |
| `--database <base-path>` | All | Required SQLite World base path **without `.db`**; no MySQL mode |
| `--directory <path>` | All | Required; input folder for validate/diff/publish, output folder for export |
| `--client-manifest <path>` | validate/diff/publish | Defaults to `client-bindings.json` in the input directory; an explicit relative path is relative to the process working directory |
| `--initialize-empty` | New disposable database only | Refuses an existing base path or `<base-path>.db`; applies World migrations before the operation |
| `--missions <id,id,...>` | export | Required comma-separated existing mission IDs |
| `--revision <name>` | export | Required existing normalized revision; not a publish-time override |
| `--release <name>` | export | Required output release label; publish reads labels from the documents |
| `--bootcamp-scenes` | export | Bootcamp-specific legacy-data conversion; also generates its experience pack |

Input loading is nonrecursive. It skips `client-bindings.json` and `*.schema.json`;
other `.json` files in the folder must be packs. Supply exactly one mission pack
per mission ID. Current validation supports at most one experience pack in a
publication (experience packs use validation identity `0`). Do not put two
alternative versions of one mission in the same input directory.

Export overwrites its output filenames and does not clean stale files.
Use a new output directory. Publication requires at least one enabled
non-experience mission; inactive examples alone cannot publish. Unknown CLI
capabilities such as a provider switch, server hot reload or a complete release
export must not be inferred from the API.

Errors are printed as `Mission content operation failed: ...` and return exit
code `1`; successful operations return `0`. The command with no arguments prints
usage as an error; there is no dedicated `--help` mode.
