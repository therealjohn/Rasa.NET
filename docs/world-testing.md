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
