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
use synthetic credentials and keys. MySQL migration tests are a separate,
explicitly configured workflow in the setup guide.

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
| Missions and titles | `AssignNPCMission`, `CompleteNPCMission`, `ChangeTitle` |
| Maps and travel | `MapLoaded`, `SelectWaypoint` |
| Loot | `RequestCorpseLooting`, `RequestLootAllFromCorpse` |
| Chat and contacts | `ChannelChat`, `ClanChat`, `PartyChat`, `RadialChat`, `Whisper`, `AddFriendByName`, `RemoveFriend` |

Registration does not establish gameplay completeness. For example, cloning and
mission completion have unfinished implementations despite having request
handlers. This suite uses `RequestFamilyName` as a representative RPC payload; it
does not complete or certify those game systems. Voice and dynamic-map-marker
opcode declarations likewise do not establish working feature support.

## Bounds established by the code

| Value | Meaning |
| --- | --- |
| 8,192 bytes | Default game transport buffer, from `SocketAsyncConfig.BufferSize` |
| 2,048 bytes | Default auth transport buffer |
| 65,535 bytes | Maximum encoded protocol-frame size representable by its unsigned 16-bit length |
| 64 bytes | Existing maximum game key length |
| Eight-byte blocks | Legacy cipher block size; game front-padding count is validated separately |

Transport sizes include the configured length-prefix convention. A partial
frame is compacted before receiving more data when earlier frames have consumed
part of the buffer.

The outbound writer's 32 KiB scratch allocation is not a documented inbound
expansion limit. The signed expanded-size field is also not a safe allocation
request. No guessed per-client queue quota or expanded-payload ceiling is added
by these checks. An operational quota still needs a separate decision or
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
