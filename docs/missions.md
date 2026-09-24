# Mission authoring and operations

Mission content is deployed by **EF Core database migrations**, just like the
other World data. There is no mission pack, publication command, release
activation step, or separate database path to provide to another tool.

This branch's transition to migration-owned content targets **fresh databases**.
It does not convert previously published mission databases or old experimental
saves. Database files are never deleted automatically. Use a fresh development
database path, or remove your own disposable databases when you intend to start
over.

## Start the servers

With SQLite selected in database configuration, run Auth and Game normally,
including from Visual Studio. The existing initialization flow creates each
database and applies its pending migrations. World migrations install complete
Bootcamp mission definitions, scene bindings and the private experience before
Game validates mission readiness. A second startup applies nothing already
recorded in `__EFMigrationsHistory`.

With MySQL, apply the normal migrations explicitly before starting the matching
server build:

```powershell
dotnet ef database update --project .\src\Rasa.DBL --startup-project .\src\Rasa.Game --context MySqlAuthContext
dotnet ef database update --project .\src\Rasa.DBL --startup-project .\src\Rasa.Game --context MySqlCharContext
dotnet ef database update --project .\src\Rasa.DBL --startup-project .\src\Rasa.Game --context MySqlWorldContext
```

Game does not migrate MySQL automatically. Mission data and a required C# script
can ship together as one code/database release. World and Char are separate
databases and their migrations are not one distributed transaction; finish the
required updates before admitting players.

See [setup](setup.md#database-configuration) for provider configuration and
[Docker setup](docker_setup.md) for container paths.

## Where the code belongs

| Module | Responsibility |
| --- | --- |
| `Rasa.Missions` | Independent objective rules, requirements, typed scripts, scene decisions and shared content definitions |
| `Rasa.DBL` | Schema, normalized content rows, fixed migration data and migration helpers |
| `Rasa.Game\Missions` | Loading migrated content, transactions, protocol projection, scene execution, world adapters and group credit |

Do not add a branch for each new mission to `MissionApplication`. Most missions
need data only. Unusual behavior belongs in a small registered C# script using
the existing typed scene interface.

Current Bootcamp scene data lives in
`src\Rasa.DBL\Services\Preloader\Missions\BootcampMissionDataV1.*.cs`.
The older C# World preloaders supply the normalized objective, reward and world
rows; `SeedMigratedBootcamp` binds those definitions to the modular scripts.
`MissionDataMigration` provides typed helpers for inserting/updating scene and
experience bindings and enabling a completed mission definition.

The [data and script reference](mission-reference.md) explains the fields and
ID namespaces. Ordinary and escort examples are C# fixtures in
`src\Rasa.Test\Missions\Content\MissionAuthoringExampleData.cs`.

## Add a mission

1. Identify the native mission, objective, text, NPC-package and asset IDs.
   Do not invent client text IDs or treat an opcode declaration as proof that
   the client supports the desired interaction.
2. Add a **data-only** migration for SQLite and MySQL. Keep schema changes in
   separate migrations. Both provider migrations should call the same fixed
   C# data helper where their operations are equivalent.
3. Insert the normalized definition, objectives, transitions, triggers, actions
   and any reward, area, indicator or spawn rows. Keep their `MissionId` and
   `ContentRevision` consistent. Normal EF `InsertData`, `UpdateData` and
   `DeleteData` operations are appropriate.
4. If scripting is needed, construct a `MissionSceneDefinition` in C# and insert
   it with `MissionDataMigration.InsertScene`. Declare actors, routes and named
   sequences explicitly; merely declaring an actor does not spawn it.
5. Enable the completed definition with
   `MissionDataMigration.EnableMission(migration, missionId, revision)`.
   Unreconstructed legacy definitions stay disabled. At most one revision of a
   mission may be enabled.
6. Exercise acceptance, progress, reward, failure, abandonment and reconnect
   through the real Game entry points. Verify the native client separately.

An enabled mission is not exempt from validation. Game checks normalized
contracts, script/state versions, requirement handlers, actor/route bindings,
sequence targets and operation-key constraints before use.

## Change mission data

Add a new migration rather than changing a migration that has already been
applied. Migration-owned C# data must also remain fixed: do not make an old
migration call a mutable "latest mission" factory or read loose files from the
working directory.

For example, a data migration can adjust an existing indicator's radius:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rasa.Services.Preloader.Missions
{
    internal static class AdjustBombIndicatorV2
    {
        internal static void Up(MigrationBuilder migration) =>
            migration.UpdateData(
                "mission_indicator",
                new[] { "mission_id", "content_revision", "objective_id", "indicator_id" },
                new object[] { 1995U, "deployment_11", 3U, 436U },
                "radius", 10.0);

        internal static void Down(MigrationBuilder migration) =>
            migration.UpdateData(
                "mission_indicator",
                new[] { "mission_id", "content_revision", "objective_id", "indicator_id" },
                new object[] { 1995U, "deployment_11", 3U, 436U },
                "radius", 8.0);
    }
}
```

This is an illustrative change, not another required Bootcamp migration. The
provider migration classes delegate their `Up`/`Down` methods to the helper.

Scene bindings use `InsertScene` or `UpdateScene`; private experiences use
`InsertExperience` or `UpdateExperience`. Those helpers serialize the typed C#
definitions into their database columns. That internal serialization is not a
JSON authoring or deployment workflow.

When moving an actor, also update relevant indicators and areas. A private
experience-owned role must agree between the mission and experience definitions.
Validate its complete footprint and route against the intended surface, especially
at bridges and other stacked geometry; finding a nearby navmesh polygon is not
proof of visible, reachable placement.

## Write a scripted mission

Implement `ISceneScript` in `Rasa.Missions` and register it with
`[MissionScript("your.script.key", 1)]`. Its interface receives a `SceneContext`
and typed `SceneObservation`, and returns a `SceneDecision`.

Scripts have no Game clients, EF contexts or direct world mutation. Return
world intents, character intents, signals and timers; Game owns their
transaction and publication behavior. Use shared actor roles and named routes
rather than parsing runtime entity IDs or legacy scenario-key strings.

`MissionSceneDefinition` contains:

- `Script` and `StateVersion`, identifying the registered implementation.
- `Actors`, `Routes`, `Sequences` and `Names`, defining its authored scene.
- Optional `DefeatSequences`, mapping actor roles to durable sequence inputs
  after confirmed deaths, independent of player kill-reward eligibility.
- `Requirement`, `ObjectiveRequirements` and `TurnInRequirement`, binding
  admission, progress and turn-in eligibility.
- `Credit` and optional `PublicEncounter`, controlling eligible group credit
  and public actor reservation.
- Optional `Audio`, binding briefing narration, accepted/completed voice cues
  and audio paired with mission announcements. This is shared by all missions,
  not restricted to Bootcamp.

Use `data.sequence` for ordinary authored sequences. Bootcamp scripts and
`example.escort` show how to add unusual behavior without enlarging the
mission manager.
`BootcampExtractionScene` is an example of counting a finite assault in the
script checkpoint, waiting for both ship arrival and enemy defeats, then
unlocking an NPC objective. Enemy movement and combat use shared world intents.

Recovery is defined by the script checkpoint, route resume settings and
public encounter policy. There is no generic `Recovery` string that dispatches
an automatic recovery strategy.

## Private experiences and public encounters

Bootcamp remains a separate private map per character. Its
`MissionExperienceDefinition` owns actors that must survive individual mission
completion, such as Youngblood. `SharedKey` means sharing across that character's
experience, not creating personal copies of public main-world NPCs.

Main-world actors remain public. An escort reserves the exact static spawn
before the assignment is accepted. Competing starts wait for the authored
return/reset/respawn. Departure policy may be `Reset`, `Wait` or `Continue`;
deliberately abandoning the initiating assignment cancels that run.

Group credit requires each recipient's matching active objective and the
authored eligibility policy. It does not accept missions automatically or copy
another character's progress, dialogue or reward choice.

Valid private-map static actors can be deferred until their normal spawn worker
runs. Such operations remain `Pending`, without a failure diagnostic, and
complete through the normal actor-available notification. Missing or invalid
spawns and unowned public leases remain explicit failures.

## Revisions, persistence and safety

EF migration history controls deployment. `ContentRevision` and script
`StateVersion` still identify saved-state contracts; they are not separately
published release names. Compatible position/text/data corrections can be
ordinary data updates. A later change to objective identities or persisted
checkpoint shape must explicitly handle the corresponding character state.

Keep journal assignments, completion history, reward receipts, deadlines and
scene receipts intact during ordinary gameplay. Clearing a completed journal
entry must not allow its rewards again. Inventory and progress changes commit
before success packets.

General character flags are stored in `character_flag`; `Manifestation.PlayerFlags`
is their login-restored cache. Mission flag actions must not write only to the
dictionary. Use the owning character transaction so a failed objective/reward
operation also rolls back its flag changes. The generic repository is available
to future reward and door checks; it is not tied to a mission assignment.
See [flag storage and IDs](mission-reference.md#persistent-character-flags).

`PersistentCharacterFlags` replaces `character_qualification` in the Char schema.
This branch change deliberately targets **fresh databases**: it does not copy
old qualifications or attempt to reconstruct flags that existed only in memory.
Use fresh disposable SQLite databases as agreed for testing; the operator
chooses when to remove old files. The server does not delete databases.
MySQL remains manually migrated. Historical migrations remain unchanged even
though the final schema no longer contains the qualification table.

This redesign has no upgrade contract for old experimental databases. It also
does not add an automatic reset, database deletion or background publication hook.
Back up real databases before applying future schema/data changes.

## Focused verification

```powershell
dotnet test .\src\Rasa.Test\Rasa.Test.csproj --no-restore --filter "FullyQualifiedName~MissionMigrationTests|FullyQualifiedName~MissionAuthoringLocalityTests"
```

The migration checks exercise fresh SQLite initialization, repeat startup,
enabled content, script validation and provider-equivalent seed operations.
Use the affected gameplay suites for the mission being changed, then the
[native-client checklist](world-testing.md#native-client-bootcamp-acceptance-checklist).
Offline MySQL model/SQL checks are not a live MySQL acceptance result.

The published Game executable's `--check-mission-assets` command verifies
navigation files and compiled Bootcamp script bindings without opening databases
or listeners. It no longer searches for mission JSON packs.
