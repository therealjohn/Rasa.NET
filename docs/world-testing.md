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
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay.WeaponAmmoTests"
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
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay.GameplayPersistenceTests|FullyQualifiedName~Rasa.Test.Gameplay.GameplayTravelTests"
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
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Gameplay.LootTests"
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
  the entire batch and can succeed without a preceding open request. This
  preserves the existing take-all behavior by explicit user decision.
  `SetAutoLootThreshold` remains unimplemented; threshold filtering is tracked
  in [InfiniteRasa/Rasa.NET#93](https://github.com/InfiniteRasa/Rasa.NET/issues/93).
  Automatic requests retain the same ownership, range, capacity, transaction
  and duplicate-grant checks as manual requests.

Malformed tuple arities are rejected. Gameplay rejections return failure and
log the reason without grants or success packets; no new wire error layout is
assumed. Cancel/per-item requests are not implemented by this slice.

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
unchanged. Mission rewards and party distribution remain separate work.

Native 1.16.5.0 corpse/window interaction is **not verified**. The repo has no
established `LootCorpse`/`Use` opening payload, and the packet snapshots only
preserve existing server layouts. Verify opening, item display, manual take-all,
receipt/inventory updates and closure in the native client before declaring the
UI complete. Auto-loot filtering also remains unsupported. Separate live MySQL
tests cover the shared transaction and migration paths, and the offline suite
also runs in Linux. The loot slice adds no provider-specific SQL, schema or
dependencies. Loss of a database commit
acknowledgement is still ambiguous; there is no durable distributed exactly-once
claim ledger. Subsequent stale inventory/credit snapshots fail closed.

## Abilities, learned state and tray

The [ability reference](abilities.md) records the approved historical tables,
explicit engine policies, transactions, lifecycle guards and native acceptance
limits. Sprint ranks 1-5 and Lightning ranks 1-2 are executable. Rank 2 uses
[Blumster's supplied arc layout](https://github.com/InfiniteRasa/Rasa.NET/issues/92#issuecomment-5716373212).
Lightning ranks 3-5 still fail explicitly because the additional Sonic, stun
and storm contracts are not established; they do not run as incomplete ranks.

Run `AbilityTests`, `AbilityTrayTests`, `HudStateTests`, `LightningArcTests` and
`LightningRecoveryTests` together for learned
rank/cost validation, real migrated tray persistence, rollback, resource
snapshots, interruption/replay and elapsed upkeep. Selection adds one
server-owned character column through both SQLite and MySQL migrations.
Effects now receive actual elapsed time on every map tick.

## MySQL persistence checks

The opt-in `MySqlCompatibilityTests.P3.cs` cases exercise the real additive
selection migration, XP/level writes, training/tray persistence, and shared
item/inventory/credit/ammunition transactions. A forced server-side constraint
failure after earlier saves must roll back the whole batch and allow a retry.

Set `RASA_TEST_MYSQL_CONNECTION` to an isolated local test server, then run:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-build --no-restore --filter "TestCategory=MySql"
```

The fixture rejects the default MySQL port and an existing database in the
connection string. It creates and removes its own random schemas; do not point
it at a developer or production database. Without an explicit endpoint these
tests report inconclusive rather than using a default database.

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

There are no usable navigation/collision assets or loaded patrol-path data in
this checkout. Existing wander/combat/path-following scaffolding is not an
obstacle-aware route solver. Such routing is explicitly deferred, not simulated
by straight-line movement or fabricated map geometry.

Native 1.16.5.0 client checks remain separate: two clients crossing visibility
boundaries, waypoint/dropship UI behavior, interruption/reconnect, and movement
against real terrain. These automated checks do not establish that
[InfiniteRasa/Rasa.NET#45](https://github.com/InfiniteRasa/Rasa.NET/issues/45) or the
remaining gameplay issues are complete. See the [protocol regression guide](protocol-testing.md)
for the separate first-map-load acceptance boundary.
