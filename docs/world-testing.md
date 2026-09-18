# World movement, travel and spawn checks

The first world-reliability target is **Concordia Wilderness**, map context
`1220` (`adv_foreas_concordia_wilderness`). It is the default new-character map.
The checked-in seed contains 218 spawn pools there: 183 have a nonzero configured
population and 35 are empty. Empty pools are not populated with invented defaults.

## Run the automated checks

After the SDK/dependency setup in [the setup guide](setup.md), run:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.World"
```

The fixtures use isolated in-memory worlds, actual outgoing packet queues,
deterministic clocks/random sources and disposable SQLite files. They do not
connect to a developer database or require the game client.

## Mission protocol boundary

The mission request boundary matches the local 1.16.5.0 client scripts:

- `AbandonMission` (392): `(missionId,)`
- `AssignNPCMission` (407): `(npcId, missionId)`
- `CompleteNPCMission` (430): `(npcId, missionId, selectionIdx, rating)`
- `CompleteNPCObjective` (431): `(npcId, missionId, objectiveId, playerFlagId)`
- `RewardNPCMission` (540): `(npcId, missionId, selectionIdx, rating)`

`selectionIdx` and `rating` accept only Python `int` or `None`; boolean structs,
longs, and incorrect tuple sizes are rejected. Registering these handlers does
not activate source-only mission definitions. Unknown and inactive requests do
not mutate runtime or durable state and do not publish success packets.

Objective updates use the client receiver tuple layouts for
`ObjectiveRevealed`, `ObjectiveActivated`, `ObjectiveCompleted`,
`ObjectiveFailed`, `UpdateObjectiveCounter`, and
`UpdateObjectiveItemCounter`. Each serialized objective has eight fields,
including separate generic and item counter dictionaries, nullable remaining
time, and complete X/Y/Z indicator coordinates. Objective state and current
counter values are persisted separately from immutable initial/target metadata.
Production mission activation remains separate work.

Run the focused boundary checks with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Missions.MissionProtocolTests|FullyQualifiedName~Rasa.Test.Missions.MissionTrackerTests|FullyQualifiedName~Rasa.Test.Missions.MissionRewardTests"
```

## Movement and visibility

Movement is accepted only for an active, assigned player in a world cell, with
finite position/motion values and no pending transfer or logout. Rejected input
does not change the authoritative position.

The existing grid uses 25.6-unit cells, a 32768 coordinate bias and 16-bit cell-key
components. Cell identities must not wrap onto another cell. Packed movement
coordinates retain their signed 24-bit representation. Channel sequences reject
duplicates/older messages and continue across uint32 wrap.

Cell transitions notify both sides when players leave visibility. Introductions
are queued before movement reaches a newly visible observer. Recipients are
deduplicated and disconnected clients are excluded. Losing visibility does not
unregister the unseen player from the world.

No anti-cheat speed tolerance or terrain/collision boundary is inferred from these
checks. Those require client timing or map data that is not present in the repo.

## Waypoints and dropships

Both local and dropship destinations must be discovered by that character,
available and uncontested. Selection also requires a nearby source station of
the matching type: the existing proximity ranges are two units for local
waypoints and five for dropship triggers. Discovery is persisted before its
runtime grant and notification.

Destination map context comes from the waypoint definition. It is not an alias
for the instance identifier. The current shared-world implementation advertises
instance `1`; private operations and population instances are separate work.
Menus contain one instance descriptor per map and use authored station positions.

A local transfer uses one authoritative landing position for server state,
teleport notification and movement. The existing local one-unit height offset
is retained. Position persistence occurs on acknowledgement. Dropships track
boarding, departure and map-load acknowledgement, advance only on their owning
map, and preserve runtime inventory identities across the map transition.

Configure the deadline in Game's environment-specific settings:

```json
{
  "GameConfig": {
    "TransferTimeoutSeconds": 60
  }
}
```

The value must be positive. The default is 60 seconds. If the client does not
acknowledge in time, the server restores the transfer origin and disconnects;
the client can reconnect at that origin. This prevents a late acknowledgement
from completing a later transfer. A failed position write does not report a
successful transfer. Duplicate or unsolicited acknowledgements are ignored.

Provider/query failures are logged and restore the in-memory transfer origin.
The disconnect save is best-effort and also logs database failures; durable
recovery cannot be promised while the database is unavailable. Departure
cancels owned auto-fire timers and completes clan cleanup before discarding
runtime inventory.

## Weapon ammunition and HUD packets

Run the focused gameplay checks with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay.WeaponAmmoConsolidationTests"
```

Weapon commands require an active, living owner and a valid weapon in the
selected drawer slot. Shots use the loaded template's ammunition consumption
and refire interval. Manual requests and auto-fire share the same deadline.
Reloads accumulate matching reserve stacks, retain ammunition already loaded,
and recheck inventory when their template-defined delay ends. Switching weapons,
interruption and departure invalidate pending work. Reload/draw/stow capture
their owner's combat revision at admission; missiles capture source and target
revisions at launch. Recovery and impact reject changed revisions even if the
same objects have returned to the map or revived. This includes shots left
queued while a map has no clients. A valid shot pays for ammunition at launch;
invalidating its later impact neither spends another round nor refunds that shot.
Auto-fire also captures the owner's combat revision and cannot resume after
death/revival.

Shots and reloads persist before changing runtime ammunition or sending success
packets. `ICharUnitOfWork.ExecuteTransaction(Action)` wraps one character context
in an EF relational transaction. Participating repository calls may save inside
the callback, but must use that same unit of work and propagate errors. The
callback must not publish gameplay state or packets. The operation commits after
the callback and any remaining changes; failure rolls back and clears tracking.
It does not nest transactions or retry an operation automatically.

Transaction completion explicitly detects a lost connection before commit.
Rollback skips an already-ended connection; a rollback error is logged without
replacing the original exception. The transaction boundary still rethrows.
Gameplay catches only explicit planning/stale-state rejection, repository
missing-record, database/update, checked-overflow and capability errors.
Unexpected null dereferences and unrelated `InvalidOperationException`s remain
observable with their identity and stack. Expected failures publish no grant
and allow a later request to retry from durable state.

The SQLite fixtures reopen item and inventory rows after successful operations,
injected save failures and retries. They also check drawer selection on relog,
equipment/ammunition packet pairing, and launch/impact rejection of stale or
cross-map targets. These checks do not establish native crosshair or HUD
acceptance. MySQL uses the same EF transaction API but requires separate live
provider verification.

The cross-feature regression suites exercise real gameplay entry points and
SQLite query/save boundaries:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay"
```

Successful local/dropship admission uses the same combat-cancellation hook as
world departure, before the transfer becomes pending or the position changes.
It cancels Lightning, reload/draw/stow, Sprint and auto-fire, and advances the
combat revision for queued missiles. Same-map selection and acknowledgement
need no intervening worker tick. Rejected travel does not cancel combat.
The separate corpse-eligibility revision does not change for local travel;
loot remains subject to its original ownership, lifetime and current distance.

## Owner-only corpse loot

Run the focused checks with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay.LootConsolidationTests"
```

The default maximum corpse-looting distance is **2 metres**, measured in finite
3D world coordinates, including height. This is an approved server rule, not
a reconstructed historical client value. Configure it in Game's settings:

```json
{
  "GameConfig": {
    "CorpseLootDistance": 2
  }
}
```

A configured limit must be finite and greater than zero. Invalid limits reject
both opening and claiming; they do not silently fall back to a different range.
Both requests recheck the living, active owner, account/character identity,
registered player and corpse, current map/cell membership, corpse attachment and
original lifetimes. Logging out, departure/re-entry, expired/removed corpses,
foreign ownership and non-finite or out-of-range positions cannot grant loot.
No obstacle, collision or group-distribution data is inferred.

Supported requests use the existing layouts:

- `RequestCorpseLooting` (opcode 650): `(dispenserEntityId,)`. It refreshes
  `AttachInfo`, `LootInfo`, `OverallQuality` and `CanLootItems`. It does not grant
  items or credits, and does not send an invented window-opening payload.
- `RequestLootAllFromCorpse` (opcode 651):
  `(dispenserEntityId, autoLootOnly)`. Both manual and automatic requests attempt
  the selected batch and can succeed without a preceding open request. Manual
  take-all selects every remaining item. Automatic requests select items at or
  below the player's validated `SetAutoLootThreshold` value, which defaults to
  Junk until the client sends its option. Both paths retain the same ownership,
  range, capacity, transaction and duplicate-grant checks.
- `RequestLootItemFromCorpse` claims one named remaining item into the requested
  destination slot through the same transaction path.
- `CancelCorpseLooting` detaches the client from the current dispenser without
  granting its contents.

Malformed tuple arities are rejected. Gameplay rejections return failure and
log the reason without grants or success packets; no new wire error layout is
assumed.

The planner uses loaded template categories, the existing five 50-slot personal
inventory categories and class stack maxima. It merges and splits the complete
batch before writing, and validates current durable character ownership, credits,
item identities/counts and inventory slots. A batch that does not fit grants
nothing, including credits.

Stack updates, new item rows, inventory rows and credits share one
`ICharUnitOfWork.ExecuteTransaction(Action)` operation. Immediate-save repository
methods propagate failures. No participating operation opens another unit of
work. Runtime inventory, credits and success packets are published only after
commit; rollback frees staged item identities and leaves the corpse available
for a new attempt. The fixtures use migrated SQLite repositories and reopen
state after failures before/after each write, connection loss before commit,
successful retries and inventory relog.

Claims serialize with owner departure and corpse removal. After commit, item
and credit updates precede `ActorGotLoot`, `TakenInfo`, disabled `CanLootItems`,
`GotLoot` and dispenser destruction. Queued loot packets snapshot their values
before runtime loot is cleared. Consumption is terminal even for empty or
credit-only loot. The dispenser is removed, its item identities reclaimed, and
the corpse attachment cleared. Dispenser request IDs are not recycled, so a
delayed claim cannot address a later corpse. Expiry uses the existing 20-second
corpse timer and disables/destroys the dispenser before destroying the corpse.
Loot tables and random template, quantity, credit and quality selection are
unchanged. Mission rewards use their own atomic turn-in path; party distribution
remains separate work.

Native 1.16.5.0 corpse/window interaction is **not verified**. The repo has no
established `LootCorpse`/`Use` opening payload, and the packet snapshots only
preserve existing server layouts. Verify opening, item display, manual take-all,
receipt/inventory updates and closure in the native client before declaring the
UI complete. Auto-loot threshold behavior is covered by automated manager tests,
but its native option and window presentation remain unverified. The offline
suite covers the shared transaction and migration paths without a live MySQL
server. The loot implementation adds no provider-specific SQL, schema or
dependencies.
Loss of a database commit
acknowledgement is still ambiguous; there is no durable distributed exactly-once
claim ledger. Subsequent stale inventory/credit snapshots fail closed.

## Abilities, learned state and tray

The [ability reference](abilities.md) records the approved historical tables,
explicit engine policies, transactions, lifecycle guards and native acceptance
limits. Sprint ranks 1-5 and Lightning ranks 1-2 are executable. Rank 2 uses
[Blumster's supplied arc layout](https://github.com/InfiniteRasa/Rasa.NET/issues/92#issuecomment-5716373212).
Lightning ranks 3-5 still fail explicitly because the additional Sonic, stun
and storm contracts are not established; they do not run as incomplete ranks.

Run `LightningConsolidationTests`, `AbilityTrayConsolidationTests`, and
`ProgressionPersistenceTests` together for action-table bounds, arc selection,
learned rank/cost validation, migrated tray persistence, rollback, resource
snapshots, and replay protection. Selection adds one server-owned character
column through both SQLite and MySQL migrations. Effects receive actual elapsed
time on every map tick.

## Mission persistence and lifecycle

Run the focused mission checks with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Missions"
```

Mission attempts are scoped by persistent character ID. The character database
uses `(character_id, mission_id)` as the key, so accounts and character slots do
not share mission state and one character can hold multiple attempts. Existing
rows survive the additive SQLite and MySQL migration. Character deletion
cascades to mission rows, and mission deletion cascades through objective rows
and both counter levels. Objective rows use
`(character_id, mission_id, objective_id)` and generic/item counters add their
counter key. Objective states and both counter values are
optimistic-concurrency tokens; mutation
callers supply the expected current value so stale contexts reject instead of
overwriting newer progress.

The objective-progress migrations cannot infer immutable objective definitions
from legacy mission rows, so they preserve valid character-owned rows for both
providers. On character hydration, the server maps rows to the current
operational definition and state inside one character-database transaction.
Mapped rows are retained. A row with no complete objective graph, an unsupported
state, or a non-operational definition is logged and removed through the shared
repository path; its objective children cascade. An Active row is also removed
when its persisted completable value disagrees with the required objective
states. Durable terminal Failed and Completed rows are retained when their
definition and objective graph are valid; hydration normalizes their runtime
completable value to false according to the terminal state instead of treating
the legacy bit as an Active-state consistency check. The runtime mission
dictionary is replaced only after that transaction commits. This keeps MySQL
and SQLite behavior aligned, frees mission-log capacity, and permits the same
mission ID to be accepted again without deleting unrelated valid attempts.

World mission definitions are immutable and inactive by default. Incomplete
database definitions are not hydrated, advertised by NPCs, accepted, tracked or
completed. Test definitions must opt in explicitly. This keeps the existing
partial mission rows, and any newly recovered client metadata, from appearing as
playable content before their authoritative objective and reward contracts are
known.

Mission progress uses a small typed event boundary for successful waypoint,
Logos, creature-kill, and mission-completion actions. A rule may complete one
exact Logos, creature, or completed-mission subject; complete a waypoint or
Logos objective after every distinct authored subject is durably owned; or
increment one fully specified test-only counter. Generic production counters are
not inferred. `RecordProgress` validates the active client plus runtime and
durable mission state, writes all matched changes in one serializable
transaction, and publishes counter, objective-completed, then
mission-completable deltas after commit. When one event matches multiple
objectives, every runtime counter update and counter/item-counter packet is
published first, followed by every objective-completed packet and then every
mission-completable packet. Each phase uses mission/objective definition order.

The gameplay action commits and publishes first. Waypoint progress follows
`WaypointGained`, Logos progress follows `LogosStoneAdded`, creature progress
follows the authoritative killer's XP and loot processing, and completion
progress follows reward and completion packets. A later expected mission
progress persistence failure is logged with its event kind and subject, emits no
mission delta, and does not roll back the successful waypoint, Logos, kill
reward, or mission reward. No login catch-up is synthesized. Programming errors
remain visible with their original identity and stack.

The implemented state mapping is:

- Active attempt: `MissionState.Active` with `Completeable = false`.
- Completable attempt: `MissionState.Active` with `Completeable = true`.
- Pending reward attempt: `MissionState.Success` with `Completeable = false`.
- Completed attempt: `MissionState.Completed` with `Completeable = false`.
- Failed attempt: `MissionState.Failed`.
- Abandoned attempt: the active durable row is removed and `MissionDiscarded`
  is sent.

Login sends one `MissionStatusInfo` snapshot from durable state. Acceptance
validates the active client, registered NPC, persistent giver identity, current
map instance, duplicate state and the durable 30-mission capacity, then creates
the mission and all definition-authored objective/counter rows in one
transaction. Hydration combines persisted current values with immutable
definition metadata. Abandonment reloads the durable attempt and cannot remove
completed history from a stale client.

NPC objective completion requires an active operational definition, an
incomplete durable objective, a current-map NPC, and an exact NPC-package/player
flag completion binding. Completion and explicitly authored reveal/activation
transitions load as one tracked objective graph and flush once. Runtime state
and packets include only successor transitions that were durably applied.
Packets are emitted after commit in
`ObjectiveCompleted`, `ObjectiveRevealed`, `ObjectiveActivated`, then
`MissionCompleteable(true)` order.

Turn-in infrastructure reloads the character and mission inside one serializable
character transaction. Inventory, XP, supported currencies and completion state
commit together. Runtime state and packets are published only afterward.
Sequential, reconnect and competing-client retries grant at most once. Staged
item entity IDs are released if planning or publication fails. SQLite fixtures
exercise objective persistence and competing reward transactions; offline
database checks cover MySQL model and migration SQL consistency. `selectionIdx`
must be `None` for rewards without selectable items and an in-range zero-based
integer when choices exist; non-null ratings are rejected. A durable `Success`
row can resume through `RewardNPCMission`.

NPC conversations now derive dispense, objective-complete, mission-complete,
and mission-reward entries from the character's current lifecycle state.
Vending, auction, and clan behavior remains the fallback when no mission state
applies. The recovered opening Wilderness metadata identifies missions
`1449` (Wilderness Targets of Opportunity), `1407` (Too Close For Comfort) and
`1069` (Receptive Reception), including source-backed objective text identities
and four completion-conversation bindings. Missing ordinals, initial/required
states, transitions, indicators, counters, prerequisites, repeatability, and
rewards remain null/absent, so these definitions stay inactive and never appear
in conversation packets.

The source-only catalog also preserves these bounded progress slices:

- Mission `1069`, objective `1`: exact Logos `10` (entity class `7364`, map
  `1220`). Objectives `2` and `3` retain only completion conversations
  `(168,1,Completion)` for Solis creature `42`/spawn `184` and
  `(112,1,Completion)` for Apirka creature `43`/spawn `219`.
- Mission `1407`, objective `1`: completion conversation
  `(113,1,Completion)`, corroborated by Moawi creature `38`. Objective `10`
  retains presentation and `(168,1,Completion)` conversation metadata only.
  No escort entity, route, success/failure transition, or Ranger binding is
  inferred.
- Mission `1449`, objective `1`: distinct waypoints
  `{49,50,51,57,61,73,156}`. Objective `8`: distinct Logos
  `{1,2,6,9,10,23,24,28,38,49,53,56}`. Objectives `20` through `25` bind
  exact creature IDs `{82,83,84,79,80,75}` respectively. Objective `23`
  explicitly records that Horntail's map-1220 spawn is unresolved. Spawn
  resolution for objectives `20-22` and `24-25` remains unknown rather than
  inferred from corroborating rows. Every objective outside the exact listed
  rules has no progress rule.

All three definitions have `IsOperational == false`, carry no production
rewards, and produce no character mission/objective/counter writes or mission
packets for any progress event. Source preservation is not activation.

Run the progress and adapter boundary checks with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Missions.MissionProgressTests|FullyQualifiedName~Rasa.Test.Missions.MissionDefinitionCatalogTests|FullyQualifiedName~Rasa.Test.World|FullyQualifiedName~Rasa.Test.Gameplay.LootConsolidationTests|FullyQualifiedName~Rasa.Test.Gameplay.ProgressionPersistenceTests"
```

## MySQL persistence acceptance

The repository's automated database checks compare models, snapshots, migration
ordering, and generated MySQL SQL without opening a live connection. The current
test project has no opt-in live MySQL category. Validate clean creation,
upgrades, rollback, concurrent mission rewards, and shared gameplay transactions
against an isolated disposable MySQL 8.0/8.4 instance before claiming live
provider acceptance. Do not point exploratory checks at a developer or
production database.

## Spawn timing and lifecycle

Seeded `RespawnTime` values are interpreted as **seconds**, converted once to
runtime milliseconds. The worker receives actual elapsed milliseconds for each
active-map tick. For example, seed value `20` means a 20-second cooldown.
An inactive map does not acquire an invented offline catch-up policy.

The existing limit of 64 creatures applies across the whole pool. Configured
minimum/maximum counts remain inclusive. Malformed ranges and missing templates
are reported without replacing them with default creatures. Manual modes are
not silently made automatic.

An animated wave retains its selected creatures until delivery rather than
rerolling them. Alive creatures, reserved deliveries and in-flight dropships
block another generation. The last such reference leaving starts cooldown;
remaining corpses do not delay the next generation. Each death and terminal
corpse removal changes counters once, independently of observer count.

## Acceptance still requiring external evidence

The checkout contains 77 generated `.nav` files under `navmesh`. `Rasa.Game`
loads matching files through `GameDataConfig.NavMeshPath`, uses them for ground
queries and creature paths, and falls back to straight-line movement when a map
has no usable file. The automated world suite covers map-link transfer and
lifecycle behavior, but it does not certify every generated route or real
terrain/collision interaction.

Native 1.16.5.0 client checks remain separate: two clients crossing visibility
boundaries, waypoint/dropship UI behavior, interruption/reconnect, and movement
against real terrain. These automated checks do not establish that
[InfiniteRasa/Rasa.NET#45](https://github.com/InfiniteRasa/Rasa.NET/issues/45) or the
remaining gameplay issues are complete. See the [protocol regression guide](protocol-testing.md)
for the separate first-map-load acceptance boundary.
