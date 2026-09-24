# Mission authoring and operations

This guide is for content contributors and C# developers adding missions to the
merged mission runtime. For field names, numeric event/state values and command
arguments, use the [mission pack and script reference](mission-reference.md).

## Choose an implementation

Mission contributors work in `content\missions`, not in general game managers.
The deployed source is a selected release in the World database. JSON packs are
the reviewed authoring source; the publisher validates and installs them
transactionally. Do not independently edit the published rows.

An ordinary mission needs a definition, verified client objective/text bindings,
prerequisites, objective transitions, rewards and evidence. It does not need a C#
script. A scene can use the `data.sequence` script or a compiled implementation of
`ISceneScript` in `Rasa.Missions`. Compiled scripts cannot reference Game, DBL,
networking or inventory services.

| What you need | Where to author it |
| --- | --- |
| Kill, collect, equip, talk, area or deadline objectives | Mission JSON rows and their transitions |
| A sequence of existing spawn, interaction, presentation or timer operations | `scene.sequences` with the registered `data.sequence` script |
| Branching choreography, route callbacks or custom checkpoint logic | A registered `ISceneScript` in `src\Rasa.Missions\Content\<area>` |
| A prerequisite not expressible by the built-in predicates | A pure registered requirement handler, using supported facts |
| A genuinely new world capability or authoritative fact | A reusable engine/Game adapter extension, with its own tests; not a mission-ID branch |

The checked-in Bootcamp packs were exported from the effective World data at
`3643b0a`, including the later placement, combat, crate and Reinforcements
migrations. The native projection and navigation behavior also incorporate
`ec3b640`. The compiler used to export historical Bootcamp rows is an authoring
tool, not a second runtime interpreter.

`content\missions\examples` contains an inactive data-only kill/collect/talk pack
and a typed public-escort pack. They deliberately reuse reviewed identifiers in
an isolated example catalog; they are not new native missions or translations,
and cannot be activated while marked synthetic. The production-path escort tests
provide a real valid mesh and verify route completion, reset and second-player
admission. Real content must supply its own verified client and world bindings.

The examples illustrate server behavior, not publish-ready quests: the ordinary
example is tested through completion readiness, not a native-client turn-in.
Enabled content also needs a referenced turn-in reward and complete validation
against real World/client bindings. Do not make an example playable by simply
flipping `enabled` or `synthetic`.

## Update an existing World database

Use [Update-MissionPacks.ps1](../scripts/Update-MissionPacks.ps1) for the routine
local or CI workflow. It requires a repository checkout, PowerShell 5.1 or 7,
the pinned .NET SDK, restored repository dependencies, and an existing migrated
SQLite World database.

```powershell
# Preview: validate and show changes, without publishing.
.\scripts\Update-MissionPacks.ps1 -WorldDatabasePath 'D:\RasaData\rasaworld.db'

# Apply explicitly after stopping or draining Game.
.\scripts\Update-MissionPacks.ps1 -WorldDatabasePath 'D:\RasaData\rasaworld.db' -Publish
```

Replace the example path with the physical World file Game actually uses.
Unlike the low-level CLI, the wrapper takes the **filename including lowercase
`.db`** and converts it to the CLI's base path. It never infers a database from
Game configuration. Relative explicit paths resolve from the caller's current
directory; the default packs resolve from the script's repository, not the caller.

The script checks the paths and manifest, incrementally builds MissionTool with
`--no-restore`, then validates and shows the diff. Only `-Publish` permits
publication. Missing paths or failed build/validation/diff stop the workflow;
native tool exit codes are preserved. Restore dependencies using the
[repository setup](setup.md#applying-migrations) if build assets are missing.

Use `-PackDirectory` to select a complete candidate release, including its
`client-bindings.json`. CI can use the same noninteractive interface after its
normal restore/setup stage:

```powershell
pwsh -NoProfile -NonInteractive -File .\scripts\Update-MissionPacks.ps1 `
  -WorldDatabasePath 'D:\RasaData\rasaworld.db' `
  -PackDirectory 'D:\artifacts\mission-release' -Publish
```

There are no confirmation prompts, force-overwrite, database-creation, migration,
reset or server-stop switches. The existing publisher still enforces immutable
revisions/releases. Preview success is not a promise that publication will
accept a changed immutable binding. The script does not restore packages,
change package feeds or support MySQL. Neither building nor starting Auth/Game
runs this script automatically. Restart Game after successful publication.

For a new disposable World database, use the initialization exercise below;
the wrapper deliberately refuses a missing file.

## Try the authoring workflow safely

Run PowerShell from the repository root after the SDK/dependency setup in
[setup.md](setup.md). This exercise validates and publishes the existing Bootcamp
packs into a **new disposable World database**, not the server's database.

`--database` takes the SQLite **base path without `.db`**. The connection factory
appends `.db`; passing `world.db` would open `world.db.db`. Auth, Char and World
files are not interchangeable.

```powershell
$tool = 'src\Rasa.MissionTool\Rasa.MissionTool.csproj'
$world = Join-Path $env:TEMP ('rasa-mission-authoring-' + [guid]::NewGuid().ToString('N'))
$packs = 'content\missions\bootcamp'

dotnet run --project $tool --configuration Release --no-restore -- validate --database $world --directory $packs --initialize-empty
dotnet run --project $tool --configuration Release --no-restore -- diff --database $world --directory $packs
dotnet run --project $tool --configuration Release --no-restore -- publish --database $world --directory $packs
```

Stop if any command exits nonzero. Expected results are `Validated 6 packs`,
mission `KEEP`/`ACTIVATE` and experience-binding summaries, then
`Published release bootcamp-modular-v1, manifest ...`. Initialization applies
World migrations and therefore writes the new database even though the
validation result says "no database changes"; that message describes validation,
not initialization. The physical file is `$world + '.db'`.

Without `--initialize-empty`, `validate` and `diff` do not publish or migrate.
`publish` validates again and commits the release in one transaction. No command
starts Auth/Game or touches a Char database. Run this workflow against a
disposable copy when preparing an existing server upgrade; do not initialize or
experiment on a live database.

## Author a new mission

1. **Establish the client and World identities.** Recover the mission ID, objective
   IDs, name/body/counter text IDs, NPC conversation bindings and rewards from
   reviewed evidence. The client indexes objectives by mission/objective ID;
   inventing a new ID or reusing text from another mission does not create client
   support. Record uncertainty instead of activating incomplete content.
2. **Create a release directory.** Put the complete candidate release's mission
   packs and one `client-bindings.json` directly in it. The CLI reads only that
   directory, not child directories. Copy retained packs as well as new ones:
   publication selects a complete release, not a patch to the active release.
3. **Author a mission pack.** Use the inactive ordinary example for field shape.
   Set `schemaVersion: 1`, a release name and a content revision. Every normalized
   row must repeat the same `missionId` and `contentRevision`. Keep dictionary
   keys and related objective, transition, reward, scenario, spawn and area IDs
   consistent. JSON properties use camelCase; unknown fields are rejected.
4. **Define the objective graph.** Specify ordinals, initial/required states,
   typed triggers and ordered actions. Reveal and activate successors explicitly.
   One transition uses one supported trigger shape; all/any prerequisite trees
   are separate from event-trigger composition. Use the
   [trigger reference](mission-reference.md#objective-triggers) for counter and
   subject meanings.
5. **Bind rewards and requirements.** A turn-in reward needs a `GrantReward`
   action referencing the one selected reward package; defining `rewards` alone
   is insufficient. Scenario/loot grants can reference other packages. Define
   admission, objective and turn-in predicates separately.
6. **Add World assets and any scene.** Packs reference existing creature, item,
   NPC-package, entity-class and map data; they do not create all those templates.
   Add provider-paired World migrations only for new World assets/schema.
   Ordinary mission additions use packs, not changes to central mission code.
7. **Update the client manifest and evidence.** Enabled non-synthetic missions
   must match the manifest's mission/objective/text mappings and contain evidence.
   A manifest is a reviewed allowlist, not discovery of what the game client
   supports. Exporting one does not verify the IDs against the client.
8. **Validate and exercise the release.** Run `validate`, inspect `diff`, test
   publication on disposable storage and drive the actual acceptance/progress/
   turn-in paths. Perform the native-client checks before treating reconstructed
   content as accepted. Set `enabled: true` only for evidence-ready content.

Use a new content revision for changed normalized rows or scene bindings.
Change `contentRevision` on **all** of that mission's rows. Use a new release
name when changing membership, enablement or the experience set; update `release`
on every included pack. Unchanged missions may retain their old revisions.

## Add a data-driven scene

A mission's optional `scene` object binds its script, named actors, routes and
sequences. Sequence `0` is the `Started` event delivered on acceptance.
`data.sequence` handles `Started`, `Signal` and `TimerElapsed`; it does not
automatically react to route completion, actor death or owner loss.

To start a later sequence from an objective, author a `StartScenario` action
whose `scenarioId` matches a declared `scenarios` row **and**
`scene.sequences["<id>"]`. The normalized row satisfies the mission graph's
reference checks; the typed scene executes the operations. Use the normalized
scenario's `Automatic` start policy for automatic objective-driven dispatch.
`PlayerTriggered` suppresses that automatic start and needs an existing
authoritative interaction path. Do not add only a `scenarioSteps` row and expect
the removed interpreter to execute it.

`ActivateSpawnGroup` routes through `scene.names["spawn-group-<id>"]` to a typed
sequence. Define that name/sequence and its actor operations as well as the
normalized spawn-group reference.

Keep world operations in `world`, inventory/mission mutations in `character`,
objective-credit events in `signals`, and waits in `timers`. These are separate
lists, not an arbitrary interleaved scripting language. Wait for a timer or an
authoritative route result before crediting an event that depends on it.
See [scene fields and intents](mission-reference.md#scene-bindings-and-intents).

## Add a typed C# script

Place the implementation in `src\Rasa.Missions\Content\<area>`, implement
`ISceneScript`, and add `[MissionScript("<stable-key>", 1)]`. The registry scans
the `Rasa.Missions` assembly at startup. Registered types need a public
parameterless constructor; duplicate keys and mismatched state versions fail.
Rebuild Game and MissionTool when adding a script, not only the core library.

For example, this handler uses the existing `guide` actor and `outbound` route
bindings. It is a script example, not a complete publishable mission:

```csharp
using Rasa.Missions.Scenes;

namespace Rasa.Missions.Content.ExampleZone
{
    [MissionScript("example-zone.scout-escort", 1)]
    public sealed class ScoutEscortScene : ISceneScript
    {
        public SceneDecision Handle(SceneContext context, SceneObservation observation) =>
            observation.Kind switch
            {
                SceneEventKind.Started => new SceneDecision(
                    "{\"phase\":\"escorting\"}",
                    worldIntents: new WorldIntent[]
                    {
                        new EnsureActorIntent("ensure-guide", "guide"),
                        new RunRouteIntent("escort-route", "guide", "outbound")
                    }),
                SceneEventKind.RouteCompleted => new SceneDecision(
                    "{\"phase\":\"arrived\"}",
                    signals: new[]
                    {
                        new SceneMissionSignal(context.Run.MissionId, 1, 1)
                    },
                    status: SceneStatus.Ended),
                _ => new SceneDecision(context.Run.Checkpoint)
            };
    }
}
```

Bind `scene.script` to that key and `stateVersion` to `1`. For a public existing
NPC, also declare a matching `publicEncounter` lease; an actor role alone does
not reserve it. Bind the example's signal to a `ProgressEvent` trigger with
`eventKind: 10`, `counterId: 1` (sequence/scenario ID) and `subjectId: 1` (event ID).
The mission/objective must already be eligible when the signal occurs.

The example handles one route. Production scripts must also handle their
authored death/failure/reset cases and distinguish callbacks when several routes
exist. Ending a scene is not itself mission failure or a turn-in reward: emit
the appropriate authored mission intent/signal explicitly.

Script instances are shared by the registry. Keep per-run state in
`context.Run.Checkpoint`, not mutable instance fields, captured clients, static
dictionaries or async tasks. Derive time from `context.UtcNow`; return intents
instead of querying storage, sending packets or granting items directly. The
runtime owns persistence and replay. Give logically different operations stable,
distinct keys; reusing a receipted key does not request a second grant.

Use the existing [public escort script](../src/Rasa.Missions/Content/Examples/PublicEscortScene.cs)
and [scene contracts](../src/Rasa.Missions/Scenes/SceneContracts.cs) as compilable
references. A new capability belongs behind the shared world/character adapter
interfaces, not in a script-specific branch of a general manager.

## Export existing content

Export requires mission IDs, their existing revision and the desired release
label. It writes `<missionId>.json` and `client-bindings.json` to the output
directory, overwriting those filenames. Use a new output directory so an export
cannot overwrite hand-authored packs or leave stale extra packs in a candidate
release.

```powershell
$exportDirectory = Join-Path $env:TEMP ('rasa-mission-export-' + [guid]::NewGuid().ToString('N'))
dotnet run --project $tool --configuration Release --no-restore -- export `
  --database $world --directory $exportDirectory `
  --missions 1990,1992,1994,1995,2005 `
  --revision deployment_11 --release bootcamp-modular-v1 --bootcamp-scenes
```

This continues the disposable-database exercise above. Exports include effective
normalized rows and stored per-mission scene bindings, not just the original seed.
Generic `export` does not export the release's experience packs. The optional
`--bootcamp-scenes` conversion is specific to migrating the reviewed tutorial;
it generates the Bootcamp typed scenes and `experience.json`, including shared
actors such as Youngblood. It is not a general converter for arbitrary missions.
Retain the reviewed source experience packs when assembling other full releases.
New content is authored directly in the typed pack format.

## Bind requirements to the right stage

The pack's `scene` document can carry requirements even when no script is
needed. `requirement` controls offers and acceptance, `objectiveRequirements`
maps existing objective IDs to progress/completion predicates, and
`turnInRequirement` controls reward turn-in. Admission predicates are not
re-evaluated as later-stage gates. Bind a predicate to those stages explicitly
if it must remain true after acceptance.

Requirements compose with `all`, `any` and `not`. Common predicates read
character level, mission journal/history and server-owned flags. An unusual
predicate uses `custom` with an attribute-registered pure
`IMissionRequirementHandler` in `Rasa.Missions`. The handler declares its fact
keys; the Game fact adapter supplies them without exposing database access to
the handler. Missing handlers, unsupported fact keys and nonexistent objective
bindings fail publication and startup validation. Missing runtime account facts
are rejected explicitly rather than treated as false, including under `not`.

See [requirement JSON and supported facts](mission-reference.md#requirements)
for the exact discriminator and property names. Custom handlers use
`[MissionRequirementHandler("<key>")]` and a public parameterless constructor;
they are discovered in the core assembly. Reusing existing facts needs no
per-mission Game change. A new fact needs a supported-key entry and both live and
transactional implementations in `MissionRequirementFactsAdapter`, plus tests
for cache hydration/convergence. Handlers do not receive an EF context.

NPC conversation/status queries use the same stage predicates as mutations.
Acceptance, ordinary objective credit, scene completion intents and turn-in
recheck authoritative durable facts within their transactions. Frozen group
credit evaluates its predicates in the event-capture transaction, as described
below. Failure/reset and objective reveal/activation are lifecycle operations,
not completion credit.

Bootcamp finale and retry admission use
`not(character.starting-experience-completed)`. Completion belongs to the
character's starting-experience state or qualification. Account skip entitlement
does not substitute for that fact: a new Pending character on an entitled
account can choose normal Bootcamp and complete its own chain.

## Scene contracts

`ISceneScript.Handle` receives a versioned checkpoint, immutable bindings, an
explicit UTC clock and a typed observation. It returns a new checkpoint, world
intents, character intents, mission signals and named timer changes.

The runtime checks script/state versions, actor and route references, operation
keys and bounds before committing. Checkpoints are JSON objects limited to
16 KiB. A decision is limited to 64 world intents, character intents and signals
combined; timer names are separately validated. A dispatch cycle is bounded to
128 transitions. Unexpected script exceptions fault the run with diagnostics.

`scene.recovery` currently validates the names `RestoreCheckpoint`,
`RestartAttempt` and `Fail`; it is not passed into `SceneBindings` or used as an
automatic policy dispatcher. Do not rely on changing that string to implement
recovery. Test the script checkpoint, route resume settings and public encounter
owner-loss/reset behavior that actually execute.

The Game adapters own transactions, packet projection, navigation and actor
mutation. They retain the existing transaction-before-publication reward helpers.
Objective-triggered scene inputs are durable messages, so a committed objective
does not lose its world scene if dispatch or publication fails.

Actor handles identify a run, role, generation and exact map lifetime. Reset
invalidates prior handles and asynchronous callbacks. Pending world effects have
explicit outcomes; a failed spawn or route is not an arrival event.

The world adapter can also defer a static actor operation while a valid
automatic spawn pool in the character's private map is waiting to produce it.
Deferred operations remain durably `Pending`, without a failure diagnostic or
another scene commit. They retry and respond to the normal actor-available
notification. Missing/invalid spawn definitions and public actors without the
required lease remain failures. A deferred actor is not treated as present, and
its dependent route or interaction is not marked successful.

Wall-clock deadlines continue while a character is absent. Active-scene waits
save their remaining duration on detach. Route waypoints and completion return
authoritative observations through the same scene boundary.

Public-map departure detaches the connection, not necessarily the shared run.
Re-entry binds the current client only after its transfer finishes and it is
ingame. The authored public-encounter `Wait` policy suspends the run and its
active waits;
`Continue` retains the run for eligible participants; `Reset` reserves the
static NPC until reset completes. A failed clock-pause write keeps runtime work
suspended and retries using the original departure time.

Deliberately abandoning an assignment cancels its scenes, timers, messages and
pending world effects in the same transaction as removing the assignment. Commit and recovery
paths check assignment identity and generation. Authored failure transitions
remain separate, and independent experience state is retained. Post-commit
cleanup removes only actors and spawn pools created by that run/generation on
that map; leased static NPCs use their own return/reset lifecycle.
`Continue` on connection departure does not mean continuing after the initiating
character deliberately abandons. Another participant cannot cancel a different
owner's assignment-run.

In Bootcamp, Youngblood is experience-owned because he gives the next mission
after Capture the Flag is rewarded and cleared from the journal. Reconnect
restores that actor without authorizing work from the cleared assignment.

## Public encounters and personal progress

Bootcamp is a real per-character private map. Main-world NPCs remain public.
There are no personal/party NPC copies, leader-copy arbitration or Party Sync.

A stateful public encounter reserves one existing actor. Admission persists the
lease with the assignment before publishing success. The actor remains reserved
through return/reset. An unreachable return uses the normal authored respawn
delay without manufacturing a kill, XP or loot. Restart recovery retains the
reservation until the real static spawn exists and reset completes.

Group credit is opt-in. Every recipient must have the corresponding active
objective and meet the authored range and participation policy at the event.
Recipients and assignment generations are frozen before delivery. Each
character's credit and receipt commit together, so one failed recipient can retry
without repeating credit for the others.
Objective requirements are evaluated from authoritative facts when the event is
frozen. Changing a predicate later neither grants retrospective eligibility nor
removes earned credit. Delivery still requires the exact assignment/generation
and corresponding incomplete objective. Failed runtime convergence remains
pending rather than recording an empty credit plan as applied.

Joining a party never accepts a mission, copies completed objectives or grants
past credit. Turn-in and reward selection remain personal. Native shared-mission
packet flows are not implemented without verified shipped-client tuple/flow
evidence.

If accepting a mission immediately starts an escort and disables its giver,
later party members cannot automatically accept from that unavailable NPC. Use
an existing, verified giver/start flow or wait for a later run. Do not add new
player UI or invent a party-accept packet contract.

## Test an authored mission

For a new pack or script, add tests at the affected seams rather than adding
mission-specific branches to a general test fixture. Use these existing examples:

| Concern | Starting point |
| --- | --- |
| Data-only and compiled-script authoring locality | `MissionAuthoringLocalityTests` |
| Codec, unknown fields and native binding rejection | `MissionPackTests` |
| Loaded-pack admission, objective and turn-in requirements | `MissionRequirementProductionTests` |
| Immutable mission/scene/experience releases | `MissionReleaseImmutabilityTests` |
| Actual public actor admission, navigation and reset | `PublicEscortSceneTests`, `PublicActorRecoveryTests` |
| Frozen group eligibility and partial retry | `GroupMissionCreditTests` |
| Disconnect, transfer, abandonment and owned-actor cleanup | `PublicSceneLifecycleTests`, `SceneAssignmentLifecycleTests`, `PublicSceneActorCleanupTests` |
| Private experience and cross-mission actor lifetime | `BootcampEndToEndTests`, including clear-1994/cold-reconnect/accept-1995 |

An initial authoring check is:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~MissionPackTests|FullyQualifiedName~MissionAuthoringLocalityTests|FullyQualifiedName~MissionRequirementProductionTests|FullyQualifiedName~MissionReleaseImmutabilityTests"
```

The wrapper has separate subprocess-level contract tests using an isolated
dotnet substitute for command ordering, explicit targets and failure stopping.
They do not publish to a developer database:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~MissionPackScriptTests"
```

PowerShell must be available to execute those tests. On Windows, host-specific
cases exercise both PowerShell 7 and Windows PowerShell when installed.

Then run the affected scene/world/mission suites. Exercise a real handler and
spawn/route path; a definition that loads or a row that persists is not proof
that the client can see and use its NPC. Test cold reconnect, duplicate events,
failed writes, abandonment/reacceptance, and a second eligible or ineligible
player. Use real navigation for scripted movement.

For native-client acceptance, verify initial hidden objectives, revealed
objectives, conversation choices, manual reward selection, NPC busy/available
state and route/reset behavior. Keep the
[protocol](protocol-testing.md) and [world](world-testing.md) checks separate
from synthetic server tests; passing server tests is not a retail-client result.

## Releases, saved state and rollback

Assignments pin content revisions. Scenes additionally pin script/state versions.
Published mission revisions and release membership are immutable. Publish a new
revision instead of changing an in-use definition in place.
Scene bindings are immutable too, including adding or removing a binding.
The first scene binding may be attached to an unpublished normalized revision;
an existing binding cannot be silently removed. Reactivating an older release
must preserve its complete experience set. Disabled experience packs are not
persisted as active bindings, and synthetic packs cannot be activated.

`validate` checks shapes, references and active content; it is not a complete
preview of the publisher's immutability decisions. `diff` summarizes normalized
mission rows and enablement, but does not show every scene/experience change.
Publication performs the transactional immutability checks. Test it on a
disposable copy before deployment.

Game loads the World database's selected release, not loose source JSON on each
tick. Publishing switches that database pointer; it does not hot-reload a
running server. Stop/drain, publish and restart. Merely building Game, applying
migrations, copying JSON beside the executable or creating a character does
not publish a release.

The known Conrad placement defect in Bootcamp `deployment_11` is handled by
`BootcampConradPlacementCompatibility` in the Game content adapter. It projects
only the exact legacy corpse bindings and indicator onto clear ground when the
catalog loads. The checked-in/published coordinates remain immutable, and existing
assignments keep their revision and progress. Rebuild/restart Game and reconnect;
do not change or republish that revision to apply this correction. New content
should author the intended position normally in a new revision rather than rely
on this compatibility rule. See the
[Conrad recovery and native-client checks](world-testing.md#native-client-bootcamp-acceptance-checklist).

The same legacy Bootcamp scripts also use `BootcampBombInventory` to materialize
the bomb as native mission item `11519`. Pickup, planting and retry update that
item within the existing character transaction; a per-assignment issuance
receipt prevents duplicate grants. Resume can backfill an old unplanted save
without changing its deadline. This is a Bootcamp compatibility adapter, not a
new general pack field or permission to rewrite published revisions.

The CLI connects to SQLite only. For MySQL, apply the provider's migrations and
invoke the same `MissionPackStore.Validate`, `Diff` and `Publish` methods with an
explicitly configured `MySqlWorldContext` from provider-aware tooling. There is
no `--provider`, `--connection-string` or MySQL CLI mode. Do not pass a MySQL
database name to the SQLite command. Live MySQL transaction/concurrency
acceptance remains separate from model/snapshot and generated-SQL checks.

Keep active journal state separate from completion/outcome history. Clearing a
completed journal entry must not permit another nonrepeatable reward. A failed
outcome is not a reward claim. Experience world state and actor outcomes are
independent of journal cleanup.

For a cutover:

1. Stop admission and drain active encounters.
2. Back up the affected World and Char stores, including mission histories,
   grant/credit receipts, scene messages, timers, effects and actor outcomes.
3. Apply the reviewed provider schema migrations and publish the complete
   validated release.
4. Start with disposable test characters and verify the native-client checklist
   in [world-testing.md](world-testing.md).

Do not reset live databases, characters, inventory, XP, account entitlements or
reward receipts. If experimental active progress needs reset, inventory and
archive the exact affected assignments first and review a character/run-scoped
reset separately. There is no automatic startup reset.

Rolling back code is different from downgrading the database. Do not run schema
`Down` migrations after new receipts have been written unless those records have
been preserved and the rollback has been reviewed. Keep compatible additive
schemas and archived content revisions during a rollback.
The historical survivor-objective merge is intentionally irreversible through
`Down`; restoring its pre-migration state requires a reviewed backup restore.

Although old revisions remain stored, startup selects one revision per mission.
An assignment pinned to a missing/different selected revision is quarantined,
not automatically migrated or replayed against the new script. Plan the drain,
compatible migration or explicitly scoped reset before switching releases.

Published Game output includes `navmesh` assets and the `missions` packs and
client manifests. Default navmesh discovery checks the working directory,
application directory and then a source checkout. Explicit custom paths are
never silently replaced.

From an unrelated working directory, run the published executable with
`--check-mission-assets` to verify default navigation discovery, pack parsing and
the included client manifest without opening databases or listeners.

Observers can use `.missions [character-id]` in the existing GM command interface
to inspect assignment/release versions, scene checkpoints, actor leases and
failed/pending world operations. The command is read-only and does not reset
missions or grant credit.

| Symptom | Check |
| --- | --- |
| `No active mission release` | Publish a complete validated release into the same World database that Game uses |
| `Mission content operation failed` | Read the following diagnostic; the CLI uses exit code 1, and a successful `diff` is not proof that publication will accept a changed immutable binding |
| Unexpected empty database or `.db.db` file | Pass the database base path without `.db`; check the physical file before running a command |
| `missing script` or unsupported requirement fact | Rebuild the tool/Game against the core containing the registered handler and its supported fact adapter |
| Immutable mission, scene or release conflict | Create the required new content revision and/or release name; do not edit published rows |
| Quarantined assignment after a release switch | Its pinned revision differs from the active release; follow the reviewed migration/reset plan |
| NPC route never starts | Confirm navmesh discovery, complete walkable route and public actor lease; missing/failed navigation is not replaced with a straight line |
