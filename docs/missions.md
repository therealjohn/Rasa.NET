# Mission authoring and operations

## Author a mission

Mission contributors work in `content\missions`, not in general game managers.
The deployed source is a selected release in the World database. JSON packs are
the reviewed authoring source; the publisher validates and installs them
transactionally. Do not independently edit the published rows.

An ordinary mission needs a definition, verified client objective/text bindings,
prerequisites, objective transitions, rewards and evidence. It does not need a C#
script. A scene can use the `data.sequence` script or a compiled implementation of
`ISceneScript` in `Rasa.Missions`. Compiled scripts cannot reference Game, DBL,
networking or inventory services.

The checked-in Bootcamp packs were exported from the effective World data at
`3643b0a`, including the later placement, combat, crate and Reinforcements
migrations. The native projection and navigation behavior also incorporates
`ec3b640`. The compiler used to export historical Bootcamp rows is an authoring
tool, not a second runtime interpreter.

`content\missions\examples` contains an inactive data-only kill/collect/talk pack
and a typed public-escort pack. They deliberately reuse reviewed identifiers in
an isolated example catalog; they are not new native missions or translations,
and cannot be activated while marked synthetic. The production-path escort tests
provide a real valid mesh and verify route completion, reset and second-player
admission. Real content must supply its own verified client and world bindings.

1. Create a pack with a new content revision and release name. Keep the mission
   and objective identifiers from verified shipped-client evidence.
2. Include every required prerequisite definition in the release. Add evidence
   and update the reviewed client binding manifest when new client bindings have
   actually been verified.
3. For scenes, name actor roles and routes. Bind public NPC roles to exact static
   spawn identities, not merely a creature class. Scripted routes require a loaded
   navmesh and complete grounded paths.
4. Use distinct operation keys for character grants and world changes. Character
   grant receipts survive replay; actor effects are reconciled after commit.
5. Validate the complete release, inspect its diff, and publish explicitly while
   the server is stopped or drained.

```powershell
dotnet run --project src\Rasa.MissionTool -- validate --database C:\test\world --directory content\missions\bootcamp
dotnet run --project src\Rasa.MissionTool -- diff --database C:\test\world --directory content\missions\bootcamp
dotnet run --project src\Rasa.MissionTool -- publish --database C:\test\world --directory content\missions\bootcamp
```

These CLI commands require an explicit SQLite World database path. They never
select a character database or start a server. The EF-backed publisher also
supports a `MySqlWorldContext`; it must be invoked with an explicitly configured
provider context. A successful offline MySQL model/SQL check is not a live
MySQL transaction test.

`--initialize-empty` is for a new disposable World database only and refuses an
existing file. It applies World schema/content migrations. Never use a live
database as an export fixture.

```powershell
dotnet run --project src\Rasa.MissionTool -- export `
  --database C:\test\new-world --initialize-empty `
  --directory C:\test\exported-missions --missions 1990,1992,1994,1995,2005 `
  --revision deployment_11 --release review-release
```

Exports include the effective rows, not just the original seed. The optional
`--bootcamp-scenes` conversion is specific to migrating the reviewed tutorial;
new content is authored directly in the typed pack format.

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

NPC conversation/status queries use the same stage predicates as mutations.
Acceptance, ordinary objective credit, scene completion intents and turn-in
recheck authoritative durable facts within their transactions. Frozen group
credit evaluates its predicates in the event-capture transaction, as described
below. Failure/reset and
objective reveal/activation are lifecycle operations, not completion credit.

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
16 KiB. A decision is limited to 64 operations and a dispatch cycle to 128
transitions. Unexpected script exceptions fault the run with diagnostics.

The Game adapters own transactions, packet projection, navigation and actor
mutation. They retain the existing transaction-before-publication reward helpers.
Objective-triggered scene inputs are durable messages, so a committed objective
does not lose its world scene if dispatch or publication fails.

Actor handles identify a run, role, generation and exact map lifetime. Reset
invalidates prior handles and asynchronous callbacks. Pending world effects have
explicit outcomes; a failed spawn or route is not an arrival event.

Wall-clock deadlines continue while a character is absent. Active-scene waits
save their remaining duration on detach. Route waypoints and completion return
authoritative observations through the same scene boundary.

Public-map departure detaches the connection, not necessarily the shared run.
Re-entry binds the current client only after its transfer finishes and it is
ingame. The authored `Wait` policy suspends the run and its active waits;
`Continue` retains the run for eligible participants; `Reset` reserves the
static NPC until reset completes. A failed clock-pause write keeps runtime work
suspended and retries using the original departure time.

Abandoning an assignment cancels its scenes, timers, messages and pending world
effects in the same transaction as removing the assignment. Commit and recovery
paths check assignment identity and generation. Authored failure transitions
remain separate, and independent experience state is retained. Post-commit
cleanup removes only actors and spawn pools created by that run/generation on
that map; leased static NPCs use their own return/reset lifecycle.
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

## Releases, saved state and rollback

Assignments pin content revisions. Scenes additionally pin script/state versions.
Published mission revisions and release membership are immutable. Publish a new
revision instead of changing an in-use definition in place.
Scene bindings are immutable too, including adding or removing a binding.
The first scene binding may be attached to an unpublished normalized revision;
an existing binding cannot be silently removed. Reactivating an older release
must preserve its complete experience set. Disabled experience packs are not
persisted as active bindings, and synthetic packs cannot be activated.

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
