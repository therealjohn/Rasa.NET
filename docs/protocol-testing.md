# Protocol regression checks

Use these checks when changing connection handling, packet decoding or the legacy
client handshake. They exercise server code without installing the game client
or connecting to a developer database.

## Run the checks

Follow the SDK and dependency setup in [the setup guide](setup.md), then run:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~Rasa.Test.Networking|FullyQualifiedName~Rasa.Test.Protocol|FullyQualifiedName~Rasa.Test.Cryptography"
```

The socket tests bind only to loopback using ephemeral ports. The crypto fixtures
use synthetic credentials and keys. The command does not start the game client
or connect to MySQL.

## Bootcamp mission and departure packet order

For authoring these behaviors, see [mission authoring](missions.md) and the
[trigger/script reference](mission-reference.md). Packet projection lives in
the Game protocol adapters; mission scripts return intents/signals and must
not write Python tuples or call clients directly.

Bootcamp mission order is covered by `BootcampProtocolTests` and the broader
mission suites. They lock down the client-visible sequence used by the
Deployment 11 starting experience:

- reconnect/login snapshot: `MissionStatusInfoPacket`
- unaccepted Bootcamp arrival: `MissionStatusInfoPacket -> DispenseRadioMissionPacket`
- mission accept: `MissionGainedPacket`
- counter progress: `UpdateObjectiveCounterPacket` before completion packets
- objective progression: `ObjectiveCompletedPacket -> ObjectiveRevealedPacket -> ObjectiveActivatedPacket`
- deadline expiry failure: `ObjectiveFailedPacket -> MissionFailedPacket`
- mission turn-in: one `CompleteNPCMission` request commits completion and rewards,
  then publishes `MissionCompleteablePacket(false) -> MissionCompletedPacket`,
  reward deltas, and `MissionRewardedPacket`
- tutorials: `DisplayPlayerTutorialNotificationPacket -> PlayTutorialAudioPacket`
- first Eloh announcement: `ForceConversePacket` greeting `1634`, which starts the client's native Lightning highlight
- NPC interaction: `ConversePacket` on `RequestNPCConverse`
- map transfer: `PreWonkavatePacket -> WonkavatePacket`

The finale sends `Use` on wreck class `24586` for the native `31 -> 91`
transition, then removes the wreck after its presentation interval.
Manual evacuation uses the normal dropship states, boarding fade, map-load
handshake and arrival flight rather than an immediate map change.
Once the assault and Van check-in are complete, entering the beam starts that
flight directly. Extraction emits neither `EnteredWaypoint` nor
`WaypointGained` for Bootcamp trigger `60`; normal public dropship menus are
unchanged. A saved discovery or stale selection cannot authorize a return trip.

Mission reward previews retain the native fixed-currency/item and selectable-item
tuple. The currency slots are credits and prestige; XP is granted separately
and has no native preview field. The shared projection fills authored rewards
for offers, gains and snapshots without changing reward amounts or granting them.

The third field of the six-field mission offer is `offerVOAudioSetId`, now
populated when the mission authors narration. Absent audio remains `None`.
Accepted/completed and ambient-announcement voices use the existing
`PlayTutorialAudio` client method after successful commits; its wire name does
not impose a tutorial-map restriction. Do not add an extra tuple field or
encode XP as a currency/audio value.

Run the focused suite with:

```powershell
dotnet test src\Rasa.Test\Rasa.Test.csproj --configuration Release --no-restore --filter "FullyQualifiedName~BootcampProtocolTests|FullyQualifiedName~MissionProtocolTests|FullyQualifiedName~MissionProgressTests|FullyQualifiedName~MissionRewardTests|FullyQualifiedName~BootcampDepartureTests"
```

`CompleteMissionRequestClaimsRewardsWithoutAnotherAcceptStep` routes acceptance,
objective completion, NPC conversation, and the decoded completion request.
It covers fixed and selectable rewards, recovery of older unrewarded `Success`
rows, and retries after reconnect. `Accept Mission` only starts a mission;
`Complete Mission` claims its rewards without a second acceptance step.

Mission gains, snapshots, reveals and offers omit unrevealed (`Inactive`)
objectives. The native mission log creates a row for every received objective
and does not hide that state itself. Revealed-but-not-activated (`NotAssigned`)
objectives and completed history remain visible; hidden objectives still exist
in durable server progress. This prevents Initiation from displaying both
"Approach the Eloh Hologram" steps before the second is revealed.

Calling for Reinforcements (`1995`) uses the shipped client objective IDs
`2, 3, 1, 4`. The survivor conversation belongs to objective `2`; the
server-only reconstruction `10` is no longer sent in mission snapshots or
objective updates. Text IDs alone cannot localize an invented objective ID:
the client indexes mission objectives by the mission/objective pair.
Legacy saves are converted without restarting an active bomb deadline.
See the [Bootcamp checks](world-testing.md) for migration and interaction coverage.

## Coverage inventory

| Boundary | Automated coverage |
| --- | --- |
| Length-prefixed transport | Invalid and oversized lengths, partial headers, buffer compaction after an earlier frame, and rejected-decryption callbacks |
| Protocol frame | The four-byte header and unsigned 16-bit size, exact frame consumption, compressed frames followed by another frame, and nonzero stream offsets |
| Reassembly | Splits at every boundary of a representative message, concurrent receive/decode, internal timeout frames, and older channel sequences followed by valid data |
| Compression | Large payloads, malformed DEFLATE/back-references, final-block completion, trailing compressed bytes, early EOF, declared-size mismatch, caller stream ownership, and pooled frame-buffer return |
| Expansion allocation | An unverified expanded-size field does not cause an allocation of that claimed size; memory grows as actual decompressed bytes arrive |
| Values and server-method payloads | Truncated counts/strings/arrays, negative collection/string lengths, unsupported flags/types, unknown methods, and trailing payload data |
| Handshake | The existing game-key length bound, short DES login payloads, queue key truncation, game cipher block/padding checks, and auth checksums at different buffer offsets |
| Login and queue lifecycle | Close/completion races, preloaded synchronous handshakes, disconnect-before-enqueue, redirect capacity, and removal of disconnected queued clients |
| Peer isolation | Invalid framing or RPC data rejects its decoder while another peer still dispatches; unrelated application exceptions remain visible |
| Compatibility | Existing auth/game cipher bytes, DES login fields, password format and uncompressed channel fields |

The incoming protocol message types remain `LoginMessage`, `MoveMessage`,
`CallServerMethodMessage` and `PingMessage`. Channel `0xFF` carries the internal
four-byte timeout message. Unknown message types and unsupported named methods
are rejected rather than routed as successfully decoded packets.

Producing the expected output length does not prove that DEFLATE completed.
The decoder retains .NET's data/history validation and uses the existing
Bouncy Castle 2.7.0 library to verify final-block completion and exact compressed
input consumption. This requires a second inflate pass, but does not retain a
second expanded payload.

Feature requests use the existing server-method router:

| Feature area | Examples of registered request methods |
| --- | --- |
| Character selection | `RequestFamilyName`, `RequestCreateCharacterInSlot`, `RequestCloneCharacterToSlot` |
| Abilities and tray | `RequestArmAbility`, `RequestPerformAbility`, `RequestSetAbilitySlot`, `RequestSwapAbilitySlots` |
| Missions and titles | `AssignNPCMission`, `AssignRadioMission`, `CompleteNPCMission`, `ChangeTitle` |
| Maps and travel | `MapLoaded`, `SelectWaypoint` |
| Loot | `RequestCorpseLooting`, `RequestLootAllFromCorpse` |
| Chat and contacts | `ChannelChat`, `ClanChat`, `PartyChat`, `RadialChat`, `Whisper`, `AddFriendByName`, `RemoveFriend` |

`RequestPerformAbility` accepts exactly four arguments
`(actionId, actionArgId, target, sourceItemId)`, or five with the client's yaw
last. Source item IDs may be Python longs (`0x2F` plus eight bytes), integer-form
IDs or the existing absent-value markers. They remain 64-bit entity IDs through
ability recovery; a legitimate consumable request must not be decoded as a null
marker or truncated to 32 bits. Unsupported tuple sizes and source types remain
invalid. The target retains its entity, absent or location forms.

Registration does not establish gameplay completeness. This suite uses
`RequestFamilyName` as a representative RPC payload; it does not certify cloning,
missions, abilities, travel, or loot. Those systems have their own focused tests,
and production mission definitions remain inactive unless their complete server
contract is available. Voice and dynamic-map-marker opcode declarations likewise
do not establish working feature support.

Likewise, `ShareMission`, `AssignSharedMission`, `DeclineSharedMission` and
`DispenseSharedMission` opcode declarations do not establish an implemented
native sharing workflow. Public NPC encounters and eligible group credit do not
automatically accept missions for party members. Keep acceptance and turn-in
per character, using verified existing client interactions.

## Bounds established by the code

| Value | Meaning |
| --- | --- |
| 8,192 bytes | Default game transport buffer, from `SocketAsyncConfig.BufferSize` |
| 2,048 bytes | Default auth transport buffer |
| 65,535 bytes | Maximum encoded protocol-frame size representable by its unsigned 16-bit length |
| 262,140 bytes | Maximum declared expanded protocol payload (`4 * ushort.MaxValue`) |
| 64 bytes | Existing maximum game key length |
| Eight-byte blocks | Legacy cipher block size; game front-padding count is validated separately |

Transport sizes include the configured length-prefix convention. A partial
frame is compacted before receiving more data when earlier frames have consumed
part of the buffer.

The outbound writer's 32 KiB scratch allocation is not the inbound expansion
limit. The signed expanded-size field is validated before inflation and cannot
request more than 262,140 bytes. No guessed per-client queue quota is added by
these checks; an operational queue quota still needs a separate decision or
client/protocol evidence.

## Native-client acceptance remains separate

These synthetic regressions do not reproduce the complete first-load sequence
reported in [InfiniteRasa/Rasa.NET#45](https://github.com/InfiniteRasa/Rasa.NET/issues/45).
Do not close that issue or remove its workaround solely because this suite passes.

To verify the reported behavior, use a cold 1.16.5.0 client to log in, select a
character and enter a map repeatedly without restarting the game server. Capture
the server error and relevant frame boundaries if it fails. Redact account
identifiers, one-time login keys, credentials and private chat from any shared
trace. Record native-client results separately from server startup, synthetic
socket checks and unit-test results.

For Bootcamp-specific client validation, pair this guide with the manual
acceptance checklist in [world-testing.md](world-testing.md).
