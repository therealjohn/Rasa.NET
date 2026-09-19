# Task 1 Report: Adopt the existing mission-runtime baseline

## Scope
- Task brief: `.superpowers/sdd/task-1-brief.md`
- Base development commit before merge: `2a3e4bb8f9f153ebf64805cbd420f855f850c78b`
- Approved baseline merged: `origin/therealjohn/missions`
- Requirement: preserve mission-branch behavior with a normal merge commit, then validate the existing mission/runtime baseline before Bootcamp work starts.

## Source branch verification
Verified `origin/therealjohn/missions` still contained the required commits `0010ecd` and `670bd17`, plus the current remote tip that was merged.

```text
3748347 (origin/therealjohn/missions, therealjohn/missions, therealjohn-pr-95-overlap) Fix Docker validation model targeting
fc59bbf Strengthen final platform validation
670bd17 Fix final integration review findings
b6d233a Ignore Docker local state
f4d2653 Reconcile configuration and maintained docs
0010ecd Fix objective failure publication convergence
610380e Fix final Task 7 mission blockers
ea37c94 Fix remaining Task 7 mission findings
```

## Merge details
- Merge command: `git merge --no-ff --no-edit origin/therealjohn/missions`
- Merge strategy result: clean merge with no manual conflict resolution required.
- Merge commit after adding the required trailer: `66b20281c16d3d14b81a09405e929fe20636661a`
- Merge shortstat versus the pre-merge implementation branch: ` 767 files changed, 186367 insertions(+), 10579 deletions(-)`
- Preserved unrelated untracked path: `.playwright-mcp/` (left untouched).
- Preserved task scaffolding paths outside the merge: `.superpowers/sdd/task-1-brief.md` and `.superpowers/sdd/progress.md` remained untracked.

```text
commit 66b20281c16d3d14b81a09405e929fe20636661a
Merge: 2a3e4bb 3748347
Author:     therealjohn <1501196+therealjohn@users.noreply.github.com>
AuthorDate: Fri Sep 18 23:18:01 2026 -0400
Commit:     therealjohn <1501196+therealjohn@users.noreply.github.com>
CommitDate: Fri Sep 18 23:20:06 2026 -0400

    Merge remote-tracking branch 'origin/therealjohn/missions' into therealjohn-bootcamp-mission-roadmap
    
    Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

## Acceptance check: MissionManager baseline APIs present
After the merge, `MissionManager` on the implementation branch exposes the lifecycle, progress, and reward surfaces expected by the task brief.

```text
src\Rasa.Game\Managers\MissionManager.cs:589 internal bool TryCompleteNpcMission(...)
src\Rasa.Game\Managers\MissionManager.cs:891 internal bool TryCompleteNpcObjective(...)
src\Rasa.Game\Managers\MissionManager.cs:1042 internal bool TryRewardNpcMission(...)
src\Rasa.Game\Managers\MissionManager.cs:1071 internal bool TryFailObjective(...)
src\Rasa.Game\Managers\MissionManager.cs:1148 internal bool TryFailMission(...)
src\Rasa.Game\Managers\MissionManager.cs:1245 internal bool TryAbandon(...)
src\Rasa.Game\Managers\MissionManager.cs:1288 internal bool RecordProgress(...)
src\Rasa.Game\Managers\MissionManager.cs:1351 internal MissionProgressPublicationPlan PlanProgress(...)
src\Rasa.Game\Managers\MissionManager.cs:1626 internal bool TryGetRewardInfo(...)
```

## Validation commands and exact results
Commands run exactly as required by the brief:

```powershell
dotnet tool restore
dotnet test src\Rasa.Test\Rasa.Test.csproj --filter "FullyQualifiedName~Rasa.Test.Missions"
dotnet test Rasa.NET.sln
dotnet build Rasa.NET.sln -c Release --no-restore
```

Exact summary lines captured from the run:

```text
Restore was successful.
Passed!  - Failed:     0, Passed:   116, Skipped:     0, Total:   116, Duration: 9 s - Rasa.Test.dll (net10.0)
Passed!  - Failed:     0, Passed:   444, Skipped:     0, Total:   444, Duration: 31 s - Rasa.Test.dll (net10.0)
Build succeeded.
    26 Warning(s)
    0 Error(s)
Time Elapsed 00:00:06.71
```

Build warnings remained, but the build succeeded with `26 Warning(s)` and `0 Error(s)`. Representative warning sources:

```text
src\Rasa.DBL\Services\DbContext\MySqlMigrationHistoryRepository.cs: EF1001 internal API warnings (3 lines).
src\Rasa.Game\Managers\ManifestationManager.cs: CS0219 unused local warnings (3 lines).
src\Rasa.Game\Managers\MapChannelManager.cs: CS0414 unused field warning.
src\Rasa.Game\Memory\PythonWriter.cs: CA2022 inexact read warning.
src\Rasa.Test\Memory\NonContiguousMemoryStreamTests.cs: MSTEST0017 and CA2022 analyzer warnings.
```

## Files and conflicts changed
- Conflicts requiring resolution: None.
- Files changed by the merge relative to the implementation branch: 767

```text
A	.config/dotnet-tools.json
A	.dockerignore
M	.github/workflows/dotnet.yml
M	.gitignore
A	.superpowers/sdd/task-3-report.md
M	Dockerfile
M	README.md
M	Rasa.NET.sln
M	docker-compose.yml
A	docs/abilities.md
M	docs/docker_setup.md
A	docs/protocol-testing.md
M	docs/setup.md
A	docs/world-testing.md
A	global.json
A	navmesh/adv_afs_arena.nav
A	navmesh/adv_arieki_ligo_ashendesert.nav
A	navmesh/adv_arieki_ligo_ashendesert_avernusoutpost.nav
A	navmesh/adv_arieki_ligo_ashendesert_baneconscriptfacility.nav
A	navmesh/adv_arieki_ligo_ashendesert_indracaverns.nav
A	navmesh/adv_arieki_ligo_burningsteps.nav
A	navmesh/adv_arieki_ligo_burningsteps_magmacaverns.nav
A	navmesh/adv_arieki_ligo_crucible_incurablesward.nav
A	navmesh/adv_arieki_ligo_crucible_wbfacility.nav
A	navmesh/adv_arieki_ligo_staaljunkyard.nav
A	navmesh/adv_arieki_ligo_thunderhead.nav
A	navmesh/adv_arieki_ligo_thunderhead_faultlever.nav
A	navmesh/adv_arieki_ligo_thunderhead_quassostation.nav
A	navmesh/adv_arieki_ligo_thunderhead_rivasaattacolony.nav
A	navmesh/adv_arieki_torden_abyss.nav
A	navmesh/adv_arieki_torden_abyss_dybukkar.nav
A	navmesh/adv_arieki_torden_abyss_omegalabs.nav
A	navmesh/adv_arieki_torden_abyss_tampei.nav
A	navmesh/adv_arieki_torden_incline.nav
A	navmesh/adv_arieki_torden_incline_commtower.nav
A	navmesh/adv_arieki_torden_incline_ojasaattahive.nav
A	navmesh/adv_arieki_torden_incline_wardenbotfactory.nav
A	navmesh/adv_arieki_torden_mires.nav
A	navmesh/adv_arieki_torden_mires_banefluxitemines.nav
A	navmesh/adv_arieki_torden_mires_energyweaponcenter.nav
A	navmesh/adv_arieki_torden_mires_tahrendrabase.nav
A	navmesh/adv_arieki_torden_plains.nav
A	navmesh/adv_arieki_torden_plains_attacolony.nav
A	navmesh/adv_arieki_torden_plains_brannwaterrefinery.nav
A	navmesh/adv_arieki_torden_plains_penalresearch.nav
A	navmesh/adv_bootcamp.nav
A	navmesh/adv_earth_unitedstates_manhattan_01.nav
A	navmesh/adv_earth_unitedstates_manhattan_01_shared.nav
A	navmesh/adv_foreas_concordia_divide.nav
A	navmesh/adv_foreas_concordia_divide_minoscaverns.nav
A	navmesh/adv_foreas_concordia_divide_purgasstation2.nav
A	navmesh/adv_foreas_concordia_divide_timoramines.nav
A	navmesh/adv_foreas_concordia_divide_torcastraprison.nav
A	navmesh/adv_foreas_concordia_elohcommtower2.nav
A	navmesh/adv_foreas_concordia_palisades.nav
A	navmesh/adv_foreas_concordia_palisades_devilsden.nav
A	navmesh/adv_foreas_concordia_palisades_elohtemples.nav
A	navmesh/adv_foreas_concordia_palisades_treebackcamp.nav
A	navmesh/adv_foreas_concordia_palisades_warnetcaverns.nav
A	navmesh/adv_foreas_concordia_wilderness.nav
A	navmesh/adv_foreas_concordia_wilderness_cavesofdonn02.nav
A	navmesh/adv_foreas_concordia_wilderness_cavesofdonn_epic.nav
A	navmesh/adv_foreas_concordia_wilderness_clrf.nav
A	navmesh/adv_foreas_concordia_wilderness_guardianprom.nav
A	navmesh/adv_foreas_concordia_wilderness_pravusresearch.nav
A	navmesh/adv_foreas_howlingmaw1.nav
A	navmesh/adv_foreas_howlingmaw_cuthahbase.nav
A	navmesh/adv_foreas_howlingmaw_deathburrow.nav
A	navmesh/adv_foreas_valverde_descent.nav
A	navmesh/adv_foreas_valverde_descent_inferno_outpost.nav
A	navmesh/adv_foreas_valverde_descent_therefuge.nav
A	navmesh/adv_foreas_valverde_marshes.nav
A	navmesh/adv_foreas_valverde_marshes_banesupplydepot.nav
A	navmesh/adv_foreas_valverde_marshes_logosresearchfacility.nav
A	navmesh/adv_foreas_valverde_marshes_villageruins.nav
A	navmesh/adv_foreas_valverde_marshes_wetlandrefinery.nav
A	navmesh/adv_foreas_valverde_plateau.nav
A	navmesh/adv_foreas_valverde_plateau_maligobasev3.nav
A	navmesh/adv_foreas_valverde_plateau_sanctusgrotto.nav
A	navmesh/adv_foreas_valverde_plateau_temporalchamber.nav
A	navmesh/adv_foreas_valverde_plateau_ustoryard.nav
A	navmesh/adv_foreas_valverde_pools.nav
A	navmesh/adv_foreas_valverde_pools_livetargetpensv2.nav
A	navmesh/adv_foreas_valverde_pools_retread_caves.nav
A	navmesh/adv_foreas_valverde_pools_test_weapons_center.nav
A	navmesh/adv_wargame_edmundrange2.nav
A	navmesh/adv_wargame_indoorarena.nav
A	navmesh/adv_wargame_provinggroundsv002.nav
A	navmesh/adv_zepic_pve_arena.nav
A	navmesh/characterselection.nav
A	navmesh/test_nexus2.nav
A	navmesh/test_pvpcontrolpoint01.nav
M	src/Rasa.Auth/Auth/Client.cs
M	src/Rasa.Auth/Auth/CommunicatorClient.cs
M	src/Rasa.Auth/Auth/Server.cs
M	src/Rasa.Auth/Packets/Auth/Client/LoginPacket.cs
M	src/Rasa.Auth/Rasa.Auth.csproj
M	src/Rasa.Auth/Structures/ServerInfo.cs
A	src/Rasa.ClientData/GeoMesh.cs
A	src/Rasa.ClientData/GlmArchive.cs
A	src/Rasa.ClientData/MapFile.cs
A	src/Rasa.ClientData/MapGeometry.cs
A	src/Rasa.ClientData/MeshLibrary.cs
A	src/Rasa.ClientData/Rasa.ClientData.csproj
A	src/Rasa.ClientData/TerrainHeightmap.cs
M	src/Rasa.Communicator/Communicator.cs
M	src/Rasa.Communicator/Rasa.Communicator.csproj
M	src/Rasa.DBL/Configuration/ContextSetup/MySqlDbContextConfigurationService.cs
M	src/Rasa.DBL/Context/Char/CharContext.cs
M	src/Rasa.DBL/Context/RasaDbContextBase.cs
M	src/Rasa.DBL/Context/World/WorldContext.cs
A	src/Rasa.DBL/Migrations/MySqlAuth/20260916172946_Net10IdentityMetadata.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlAuth/20260916172946_Net10IdentityMetadata.cs
M	src/Rasa.DBL/Migrations/MySqlAuth/MySqlAuthContextModelSnapshot.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260911054802_Fix_friend_and_ignored_tables.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260911054802_Fix_friend_and_ignored_tables.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260912051500_Add_petition_table.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260912051500_Add_petition_table.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260912140000_Add_petition_status.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260912140000_Add_petition_status.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260913120000_Add_character_slot_unique.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260913120000_Add_character_slot_unique.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260915140000_Add_auction_table.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260915140000_Add_auction_table.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260915200000_Add_clan_lockbox_log.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260915200000_Add_clan_lockbox_log.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260916173014_Net10IdentityMetadata.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260916173014_Net10IdentityMetadata.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260917130734_AbilityTraySelection.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260917130734_AbilityTraySelection.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260917200310_MissionCharacterState.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260917200310_MissionCharacterState.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260918002055_MissionObjectiveProgress.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlChar/20260918002055_MissionObjectiveProgress.cs
M	src/Rasa.DBL/Migrations/MySqlChar/MySqlCharContextModelSnapshot.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260913180000_Add_map_link.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260913180000_Add_map_link.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260914120000_Add_kraftwerks.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260914120000_Add_kraftwerks.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915090000_Add_service_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915090000_Add_service_npcs.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915120000_Add_map_region.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915120000_Add_map_region.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915120000_Fix_service_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915120000_Fix_service_npcs.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915180000_Add_recipe.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915180000_Add_recipe.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915200000_Add_mission_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260915200000_Add_mission_npcs.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916100000_Add_map_marker.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916100000_Add_map_marker.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916140000_Add_minion_creatures.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916140000_Add_minion_creatures.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916160000_Retune_weapon_heat.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260916160000_Retune_weapon_heat.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917100000_Regenerate_item_template.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917100000_Regenerate_item_template.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917120000_Add_actions.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917120000_Add_actions.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917120000_Retune_weapon_tool_type.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917120000_Retune_weapon_tool_type.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917180000_Add_creature_class_flag.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917180000_Add_creature_class_flag.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917210000_Add_skill_character.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260917210000_Add_skill_character.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260918100000_Fix_logos_shrines.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260918100000_Fix_logos_shrines.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260918140000_Place_remaining_logos.Designer.cs
A	src/Rasa.DBL/Migrations/MySqlWorld/20260918140000_Place_remaining_logos.cs
M	src/Rasa.DBL/Migrations/MySqlWorld/MySqlWorldContextModelSnapshot.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260911054802_Fix_friend_and_ignored_tables.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260911054802_Fix_friend_and_ignored_tables.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260912051500_Add_petition_table.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260912051500_Add_petition_table.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260912140000_Add_petition_status.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260912140000_Add_petition_status.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260913120000_Add_character_slot_unique.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260913120000_Add_character_slot_unique.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260915140000_Add_auction_table.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260915140000_Add_auction_table.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260915200000_Add_clan_lockbox_log.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260915200000_Add_clan_lockbox_log.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260917130621_AbilityTraySelection.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260917130621_AbilityTraySelection.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260917200225_MissionCharacterState.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260917200225_MissionCharacterState.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260918001335_MissionObjectiveProgress.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteChar/20260918001335_MissionObjectiveProgress.cs
M	src/Rasa.DBL/Migrations/SqliteChar/SqliteCharContextModelSnapshot.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260913180000_Add_map_link.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260913180000_Add_map_link.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260914120000_Add_kraftwerks.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260914120000_Add_kraftwerks.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915090000_Add_service_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915090000_Add_service_npcs.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915120000_Add_map_region.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915120000_Add_map_region.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915120000_Fix_service_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915120000_Fix_service_npcs.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915180000_Add_recipe.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915180000_Add_recipe.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915200000_Add_mission_npcs.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260915200000_Add_mission_npcs.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916100000_Add_map_marker.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916100000_Add_map_marker.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916140000_Add_minion_creatures.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916140000_Add_minion_creatures.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916160000_Retune_weapon_heat.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260916160000_Retune_weapon_heat.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917100000_Regenerate_item_template.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917100000_Regenerate_item_template.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917120000_Add_actions.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917120000_Add_actions.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917120000_Retune_weapon_tool_type.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917120000_Retune_weapon_tool_type.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917180000_Add_creature_class_flag.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917180000_Add_creature_class_flag.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917210000_Add_skill_character.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260917210000_Add_skill_character.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260918100000_Fix_logos_shrines.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260918100000_Fix_logos_shrines.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260918140000_Place_remaining_logos.Designer.cs
A	src/Rasa.DBL/Migrations/SqliteWorld/20260918140000_Place_remaining_logos.cs
M	src/Rasa.DBL/Migrations/SqliteWorld/SqliteWorldContextModelSnapshot.cs
M	src/Rasa.DBL/Properties/AssemblyInfo.cs
M	src/Rasa.DBL/Rasa.DBL.csproj
M	src/Rasa.DBL/Repositories/Auth/Account/AuthAccountRepository.cs
M	src/Rasa.DBL/Repositories/Auth/Account/IAuthAccountRepository.cs
A	src/Rasa.DBL/Repositories/Char/Auction/AuctionRepository.cs
A	src/Rasa.DBL/Repositories/Char/Auction/IAuctionRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharUnitOfWork.cs
M	src/Rasa.DBL/Repositories/Char/Character/CharacterRepository.cs
M	src/Rasa.DBL/Repositories/Char/Character/ICharacterRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterInventory/CharacterInventoryRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterInventory/ICharacterInventoryRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterLockbox/CharacterLockboxRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterMission/CharacterMissionRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterMission/ICharacterMissionRepository.cs
A	src/Rasa.DBL/Repositories/Char/CharacterMissionProgress/CharacterMissionProgressRepository.cs
A	src/Rasa.DBL/Repositories/Char/CharacterMissionProgress/ICharacterMissionProgressRepository.cs
M	src/Rasa.DBL/Repositories/Char/CharacterOption/CharacterOptionRepository.cs
M	src/Rasa.DBL/Repositories/Char/Clan/ClanRepository.cs
M	src/Rasa.DBL/Repositories/Char/Clan/IClanRepository.cs
M	src/Rasa.DBL/Repositories/Char/ClanInventory/ClanInventoryRepository.cs
M	src/Rasa.DBL/Repositories/Char/ClanInventory/IClanInventoryRepository.cs
A	src/Rasa.DBL/Repositories/Char/ClanLockboxLog/ClanLockboxLogRepository.cs
A	src/Rasa.DBL/Repositories/Char/ClanLockboxLog/IClanLockboxLogRepository.cs
M	src/Rasa.DBL/Repositories/Char/ClanMember/ClanMemberRepository.cs
M	src/Rasa.DBL/Repositories/Char/ClanMember/IClanMemberRepository.cs
M	src/Rasa.DBL/Repositories/Char/Friend/FriendRepository.cs
M	src/Rasa.DBL/Repositories/Char/Friend/IFriendRepository.cs
M	src/Rasa.DBL/Repositories/Char/GameAccount/GameAccountRepository.cs
M	src/Rasa.DBL/Repositories/Char/GameAccount/IGameAccountRepository.cs
M	src/Rasa.DBL/Repositories/Char/ICharUnitOfWork.cs
M	src/Rasa.DBL/Repositories/Char/Ignored/IIgnoredRepository.cs
M	src/Rasa.DBL/Repositories/Char/Ignored/IgnoredRepository.cs
M	src/Rasa.DBL/Repositories/Char/Items/ItemRepository.cs
A	src/Rasa.DBL/Repositories/Char/Petition/IPetitionRepository.cs
A	src/Rasa.DBL/Repositories/Char/Petition/PetitionRepository.cs
M	src/Rasa.DBL/Repositories/UnitOfWork/DelegatingCharUnitOfWork.cs
M	src/Rasa.DBL/Repositories/UnitOfWork/DelegatingUnitOfWorkBase.cs
M	src/Rasa.DBL/Repositories/UnitOfWork/DelegatingWorldUnitOfWork.cs
M	src/Rasa.DBL/Repositories/UnitOfWork/IUnitOfWork.cs
M	src/Rasa.DBL/Repositories/UnitOfWork/UnitOfWork.cs
A	src/Rasa.DBL/Repositories/World/ActionRepository.cs
M	src/Rasa.DBL/Repositories/World/CreatureRepository.cs
M	src/Rasa.DBL/Repositories/World/ICreatureRepository.cs
M	src/Rasa.DBL/Repositories/World/IWorldUnitOfWork.cs
A	src/Rasa.DBL/Repositories/World/KraftwerksRepository.cs
A	src/Rasa.DBL/Repositories/World/MapLinkRepository.cs
A	src/Rasa.DBL/Repositories/World/MapMarkerRepository.cs
A	src/Rasa.DBL/Repositories/World/MapRegionRepository.cs
A	src/Rasa.DBL/Repositories/World/RecipeRepository.cs
M	src/Rasa.DBL/Repositories/World/WorldUnitOfWork.cs
A	src/Rasa.DBL/Services/DbContext/MySqlMigrationHistoryRepository.cs
A	src/Rasa.DBL/Services/Preloader/ActionCostPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ActionItemRequirementPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ActionLevelPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ActionPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ActionPropertyPreloader.cs
A	src/Rasa.DBL/Services/Preloader/CreatureClassFlagPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ItemTemplateActionPreloader.cs
M	src/Rasa.DBL/Services/Preloader/ItemTemplatePreloader.cs
M	src/Rasa.DBL/Services/Preloader/ItemTemplateWeaponPreloader.cs
A	src/Rasa.DBL/Services/Preloader/KraftwerksPreloader.cs
M	src/Rasa.DBL/Services/Preloader/LogosPreloader.cs
A	src/Rasa.DBL/Services/Preloader/MapLinkPreloader.cs
A	src/Rasa.DBL/Services/Preloader/MapMarkerPreloader.cs
A	src/Rasa.DBL/Services/Preloader/MapRegionPreloader.cs
A	src/Rasa.DBL/Services/Preloader/MinionCreaturePreloader.cs
A	src/Rasa.DBL/Services/Preloader/MinionCreatureStatsPreloader.cs
A	src/Rasa.DBL/Services/Preloader/MissionNpcAppearancePreloader.cs
A	src/Rasa.DBL/Services/Preloader/MissionNpcCreaturePreloader.cs
A	src/Rasa.DBL/Services/Preloader/MissionNpcPackagePreloader.cs
A	src/Rasa.DBL/Services/Preloader/MissionNpcSpawnpoolPreloader.cs
A	src/Rasa.DBL/Services/Preloader/RecipeInputPreloader.cs
A	src/Rasa.DBL/Services/Preloader/RecipePreloader.cs
A	src/Rasa.DBL/Services/Preloader/ServiceNpcAppearancePreloader.cs
A	src/Rasa.DBL/Services/Preloader/ServiceNpcCreaturePreloader.cs
A	src/Rasa.DBL/Services/Preloader/ServiceNpcSpawnpoolPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ServiceNpcVendorItemPreloader.cs
A	src/Rasa.DBL/Services/Preloader/ServiceNpcVendorPreloader.cs
A	src/Rasa.DBL/Services/Preloader/SkillCharacterPreloader.cs
A	src/Rasa.DBL/Structures/Char/AuctionEntry.cs
M	src/Rasa.DBL/Structures/Char/CharacterEntry.cs
M	src/Rasa.DBL/Structures/Char/CharacterMissionEntry.cs
A	src/Rasa.DBL/Structures/Char/CharacterMissionObjectiveCounterEntry.cs
A	src/Rasa.DBL/Structures/Char/CharacterMissionObjectiveEntry.cs
A	src/Rasa.DBL/Structures/Char/CharacterMissionObjectiveItemCounterEntry.cs
A	src/Rasa.DBL/Structures/Char/ClanLockboxLogEntry.cs
A	src/Rasa.DBL/Structures/Char/ClanRosterEntry.cs
M	src/Rasa.DBL/Structures/Char/FriendEntry.cs
M	src/Rasa.DBL/Structures/Char/IgnoredEntry.cs
A	src/Rasa.DBL/Structures/Char/PetitionEntry.cs
A	src/Rasa.DBL/Structures/World/ActionCostEntry.cs
A	src/Rasa.DBL/Structures/World/ActionEntry.cs
A	src/Rasa.DBL/Structures/World/ActionItemRequirementEntry.cs
A	src/Rasa.DBL/Structures/World/ActionLevelEntry.cs
A	src/Rasa.DBL/Structures/World/ActionPropertyEntry.cs
A	src/Rasa.DBL/Structures/World/CreatureClassFlagEntry.cs
A	src/Rasa.DBL/Structures/World/ItemTemplateActionEntry.cs
M	src/Rasa.DBL/Structures/World/ItemTemplateRequirementEntry.cs
A	src/Rasa.DBL/Structures/World/KraftwerksEntry.cs
A	src/Rasa.DBL/Structures/World/MapLinkEntry.cs
A	src/Rasa.DBL/Structures/World/MapMarkerEntry.cs
A	src/Rasa.DBL/Structures/World/MapRegionEntry.cs
A	src/Rasa.DBL/Structures/World/RecipeEntry.cs
A	src/Rasa.DBL/Structures/World/RecipeInputEntry.cs
A	src/Rasa.DBL/Structures/World/SkillCharacterEntry.cs
M	src/Rasa.Game/Config/GameConfig.cs
M	src/Rasa.Game/Config/GameDataConfig.cs
A	src/Rasa.Game/Data/AbilityProperty.cs
A	src/Rasa.Game/Data/AuctionCategory.cs
A	src/Rasa.Game/Data/CharacterClass.cs
A	src/Rasa.Game/Data/ChatChannelId.cs
A	src/Rasa.Game/Data/Cipher.cs
A	src/Rasa.Game/Data/ClanLockboxTab.cs
A	src/Rasa.Game/Data/ClanRank.cs
A	src/Rasa.Game/Data/CombatRegen.cs
A	src/Rasa.Game/Data/CreatureFlag.cs
M	src/Rasa.Game/Data/DynamicObjectType.cs
A	src/Rasa.Game/Data/Gestures.cs
A	src/Rasa.Game/Data/GmLevel.cs
A	src/Rasa.Game/Data/Harvest.cs
A	src/Rasa.Game/Data/HarvestYield.cs
A	src/Rasa.Game/Data/InventoryTransactionType.cs
A	src/Rasa.Game/Data/LockboxTab.cs
M	src/Rasa.Game/Data/LootQuality.cs
A	src/Rasa.Game/Data/MapLinkKind.cs
A	src/Rasa.Game/Data/MapMarkerType.cs
A	src/Rasa.Game/Data/MinionStance.cs
A	src/Rasa.Game/Data/MissionObjectiveConversationType.cs
A	src/Rasa.Game/Data/MissionObjectiveState.cs
A	src/Rasa.Game/Data/MissionProgressEventKind.cs
M	src/Rasa.Game/Data/MissionState.cs
A	src/Rasa.Game/Data/NotificationId.cs
A	src/Rasa.Game/Data/PetitionStatus.cs
A	src/Rasa.Game/Data/PetitionType.cs
A	src/Rasa.Game/Data/PlayerNotificationType.cs
M	src/Rasa.Game/Data/QueueState.cs
A	src/Rasa.Game/Data/ServerFlag.cs
M	src/Rasa.Game/Data/SkillId.cs
A	src/Rasa.Game/Data/TimerType.cs
M	src/Rasa.Game/Data/ToolType.cs
A	src/Rasa.Game/Data/TutorialId.cs
A	src/Rasa.Game/Data/WeaponHeat.cs
M	src/Rasa.Game/Game/Client.cs
M	src/Rasa.Game/Game/Handlers/ClientPacketHandler.cs
M	src/Rasa.Game/Game/Server.cs
M	src/Rasa.Game/GameProgram.cs
M	src/Rasa.Game/Login/LoginClient.cs
M	src/Rasa.Game/Login/LoginManager.cs
A	src/Rasa.Game/Managers/AbilityManager.cs
M	src/Rasa.Game/Managers/ActorActionManager.cs
M	src/Rasa.Game/Managers/ActorManager.cs
M	src/Rasa.Game/Managers/AuctionHouseManager.cs
M	src/Rasa.Game/Managers/BehaviorManager.cs
M	src/Rasa.Game/Managers/CellManager.cs
M	src/Rasa.Game/Managers/CharacterManager.cs
M	src/Rasa.Game/Managers/ChatCommandsManager.cs
M	src/Rasa.Game/Managers/ClanManager.cs
M	src/Rasa.Game/Managers/CommunicatorManager.cs
M	src/Rasa.Game/Managers/CreatureManager.cs
M	src/Rasa.Game/Managers/DynamicObjectManager.cs
M	src/Rasa.Game/Managers/EntityClassManager.cs
M	src/Rasa.Game/Managers/EntityManager.cs
M	src/Rasa.Game/Managers/GameEffectManager.cs
A	src/Rasa.Game/Managers/GestureManager.cs
A	src/Rasa.Game/Managers/InventoryManager.Loot.cs
A	src/Rasa.Game/Managers/InventoryManager.Missions.cs
M	src/Rasa.Game/Managers/InventoryManager.cs
M	src/Rasa.Game/Managers/ItemManager.cs
A	src/Rasa.Game/Managers/KnowledgeBaseManager.cs
A	src/Rasa.Game/Managers/KraftwerksManager.cs
M	src/Rasa.Game/Managers/LogosManager.cs
A	src/Rasa.Game/Managers/LookingForGroupManager.cs
M	src/Rasa.Game/Managers/LootDispenserManager.cs
M	src/Rasa.Game/Managers/ManifestationManager.cs
M	src/Rasa.Game/Managers/MapChannelManager.cs
A	src/Rasa.Game/Managers/MapErrorManager.cs
A	src/Rasa.Game/Managers/MapLinkManager.cs
A	src/Rasa.Game/Managers/MapMarkerManager.cs
M	src/Rasa.Game/Managers/MapTriggerManager.cs
A	src/Rasa.Game/Managers/MinionManager.cs
M	src/Rasa.Game/Managers/MissileManager.cs
A	src/Rasa.Game/Managers/MissionDefinitionCatalog.cs
M	src/Rasa.Game/Managers/MissionManager.cs
A	src/Rasa.Game/Managers/NavMeshManager.cs
A	src/Rasa.Game/Managers/NotificationManager.cs
M	src/Rasa.Game/Managers/NpcManager.cs
M	src/Rasa.Game/Managers/PartyManager.cs
A	src/Rasa.Game/Managers/PetitionManager.cs
A	src/Rasa.Game/Managers/RecipeManager.cs
A	src/Rasa.Game/Managers/RegionManager.cs
A	src/Rasa.Game/Managers/ServerFlagManager.cs
M	src/Rasa.Game/Managers/SocialManager.cs
M	src/Rasa.Game/Managers/SpawnPoolManager.cs
A	src/Rasa.Game/Managers/SummonManager.cs
A	src/Rasa.Game/Managers/ToolActionManager.cs
A	src/Rasa.Game/Managers/TradeManager.cs
M	src/Rasa.Game/Memory/ProtocolBufferReader.cs
M	src/Rasa.Game/Memory/ProtocolBufferWriter.cs
A	src/Rasa.Game/Memory/ProtocolInflater.cs
M	src/Rasa.Game/Memory/PythonReader.cs
A	src/Rasa.Game/Memory/PythonSize.cs
M	src/Rasa.Game/Memory/PythonWriter.cs
A	src/Rasa.Game/Packets/ActionTarget.cs
A	src/Rasa.Game/Packets/Clan/Client/PurchaseClanLockboxTabPacket.cs
A	src/Rasa.Game/Packets/Clan/Server/ClanLockboxLogsPacket.cs
A	src/Rasa.Game/Packets/Clan/Server/UpdateClanLockboxTabCountPacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/DisplayPlayerNotificationPacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/DisplayPlayerTutorialNotificationPacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/DisplaySystemMessagePacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/FatalErrorPacket.cs
M	src/Rasa.Game/Packets/ClientMethod/Server/GotLootPacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/NonFatalErrorPacket.cs
A	src/Rasa.Game/Packets/ClientMethod/Server/PlayTutorialAudioPacket.cs
M	src/Rasa.Game/Packets/Communicator/Both/EmotePacket.cs
M	src/Rasa.Game/Packets/Communicator/Both/ShoutPacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/ChangeClanNamePacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/ChangeFirstNamePacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/ChangeLastNamePacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/GotoMobPacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/ToggleAfkPacket.cs
M	src/Rasa.Game/Packets/Communicator/Client/WhoPacket.cs
A	src/Rasa.Game/Packets/Communicator/Server/PlayerAfkPacket.cs
A	src/Rasa.Game/Packets/Communicator/Server/PlayerInactiveWarningPacket.cs
M	src/Rasa.Game/Packets/Communicator/Server/WhisperAckPacket.cs
M	src/Rasa.Game/Packets/Communicator/Server/WhisperFailAckPacket.cs
M	src/Rasa.Game/Packets/Communicator/Server/WhisperSelfPacket.cs
M	src/Rasa.Game/Packets/Communicator/Server/WhoAckPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestCraftItemNewPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestCraftItemPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestExtractModulePacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestIntegrateItemPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestRetrieveAllFinishedItemsPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestRetrieveFinishedCraftItemPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestSalvageItemPacket.cs
A	src/Rasa.Game/Packets/Crafting/Client/RequestUpgradeItemPacket.cs
A	src/Rasa.Game/Packets/Crafting/Server/CraftingResultPacket.cs
A	src/Rasa.Game/Packets/Crafting/Server/CraftingStatusPacket.cs
M	src/Rasa.Game/Packets/Game/Client/RequestCreateCharacterInSlotPacket.cs
A	src/Rasa.Game/Packets/Game/Server/ClearServerFlagPacket.cs
A	src/Rasa.Game/Packets/Game/Server/DisplayMapErrorsPacket.cs
A	src/Rasa.Game/Packets/Game/Server/ServerFlagsPacket.cs
A	src/Rasa.Game/Packets/Game/Server/ServerPerformanceMetricsPacket.cs
A	src/Rasa.Game/Packets/Game/Server/SetServerFlagPacket.cs
M	src/Rasa.Game/Packets/Inventory/Client/ClanLockbox_DepositItemInTabPacket.cs
A	src/Rasa.Game/Packets/Inventory/Client/RequestTakeItemFromInboxInventoryPacket.cs
A	src/Rasa.Game/Packets/Inventory/Server/AddAuctionItemPacket.cs
A	src/Rasa.Game/Packets/Inventory/Server/AddInboxItemPacket.cs
M	src/Rasa.Game/Packets/Inventory/Server/InventoryCreatePacket.cs
A	src/Rasa.Game/Packets/Inventory/Server/RemoveAuctionItemPacket.cs
A	src/Rasa.Game/Packets/Inventory/Server/RemoveInboxItemPacket.cs
M	src/Rasa.Game/Packets/Login/Client/ClientKeyPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Client/RemoveLookingForGroupAdPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Client/RequestCreateLookingForGroupAdPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Client/RequestLookingForGroupSearchPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Server/DisplayLookingForGroupMessagePacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Server/LookingForGroupAdPlacedPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Server/LookingForGroupAdRemovedPacket.cs
A	src/Rasa.Game/Packets/LookingForGroup/Server/LookingForGroupSearchResultsPacket.cs
A	src/Rasa.Game/Packets/LootDispenser/Client/CancelCorpseLootingPacket.cs
A	src/Rasa.Game/Packets/LootDispenser/Client/RequestLootItemFromCorpsePacket.cs
M	src/Rasa.Game/Packets/LootDispenser/Server/ActorGotLootPacket.cs
M	src/Rasa.Game/Packets/LootDispenser/Server/CanLootItemsPacket.cs
A	src/Rasa.Game/Packets/LootDispenser/Server/LootCorpsePacket.cs
M	src/Rasa.Game/Packets/LootDispenser/Server/LootInfoPacket.cs
M	src/Rasa.Game/Packets/LootDispenser/Server/TakenInfoPacket.cs
A	src/Rasa.Game/Packets/Manifestation/Client/RequestUseCloneCreditPacket.cs
A	src/Rasa.Game/Packets/Manifestation/Server/CloneCreditsChangedPacket.cs
A	src/Rasa.Game/Packets/Manifestation/Server/CloneCreditsPacket.cs
D	src/Rasa.Game/Packets/Manifestation/Server/PerformRecovery/LightningRecovery.cs
A	src/Rasa.Game/Packets/MapChannel/Client/AbandonMissionPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Client/AssignNPCMissionPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Client/CompleteNPCMissionPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/CompleteNPCObjectivePacket.cs
M	src/Rasa.Game/Packets/MapChannel/Client/LevelSkillsPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/RequestDetachGameEffectPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Client/RequestGesturePacket.cs
M	src/Rasa.Game/Packets/MapChannel/Client/RequestPerformAbilityPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/RequestRepairPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/RequestToolActionPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/RequestUnstickPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Client/RewardNPCMissionPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/AbilityDrawerPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AbilityRecoveryPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/ActionInterruptPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/ActionReuseTimerRestartedPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/AttributeInfoPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionBuyoutFailedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionBuyoutSuccessPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/AuctionCreationFailedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionExpiredPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionSoldPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionStatusFailedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/AuctionStatusSuccessPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/CancelAuctionFailedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/CancelAuctionSuccessPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/GameEffectAttachedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/GestureEffectAttachedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/IsTrialAccountPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/ItemStatusPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/LockInfoPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/LogoutTimeRemainingPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/MapMarkerInfoPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/NotificationPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/PlayerEnteredCombatPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/PlayerExitedCombatPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/QueryFailedPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/ToolActionRecoveryPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/UpdateMapMarkerPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/UpdateRegionsPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/UserActionFailedPacket.cs
M	src/Rasa.Game/Packets/MapChannel/Server/WeaponInfoPacket.cs
A	src/Rasa.Game/Packets/MapChannel/Server/WeaponJammedPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionAssistMePacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionAssistTargetPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionCommandPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionFollowMePacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionFollowTargetPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionGoPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionStayPacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionTargetMePacket.cs
A	src/Rasa.Game/Packets/Minion/Client/MinionTargetPacket.cs
A	src/Rasa.Game/Packets/Minion/Server/MinionAckPacket.cs
A	src/Rasa.Game/Packets/Minion/Server/MinionAddedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/MissionClearedPacket.cs
M	src/Rasa.Game/Packets/Mission/Server/MissionCompleteablePacket.cs
A	src/Rasa.Game/Packets/Mission/Server/MissionCompletedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/MissionDiscardedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/MissionFailedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/MissionRewardedPacket.cs
M	src/Rasa.Game/Packets/Mission/Server/MissionStatusInfoPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/ObjectiveActivatedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/ObjectiveCompletedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/ObjectiveFailedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/ObjectiveRevealedPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/UpdateObjectiveCounterPacket.cs
A	src/Rasa.Game/Packets/Mission/Server/UpdateObjectiveItemCounterPacket.cs
M	src/Rasa.Game/Packets/Party/Client/AcceptPartyInvitesChangedPacket.cs
M	src/Rasa.Game/Packets/Party/Client/InviteSquadPacket.cs
M	src/Rasa.Game/Packets/Party/Client/KickUserFromPartyByIdPacket.cs
M	src/Rasa.Game/Packets/Party/Client/KickUserFromPartyPacket.cs
M	src/Rasa.Game/Packets/Party/Client/MakeUserPartyLeaderByIdPacket.cs
M	src/Rasa.Game/Packets/Party/Client/MakeUserPartyLeaderPacket.cs
M	src/Rasa.Game/Packets/Party/Client/PartyJoinRequestResponsePacket.cs
M	src/Rasa.Game/Packets/Party/Client/SendJoinRequestToPartyByNamePacket.cs
M	src/Rasa.Game/Packets/Party/Client/SendJoinRequestToSquadLeaderPacket.cs
A	src/Rasa.Game/Packets/Party/PartyArgs.cs
M	src/Rasa.Game/Packets/Party/Server/AddSquadMemberPacket.cs
A	src/Rasa.Game/Packets/Party/Server/DisplayPartyMessagePacket.cs
A	src/Rasa.Game/Packets/Party/Server/JoinSquadRequestReceivedPacket.cs
A	src/Rasa.Game/Packets/Party/Server/PartyDisbandedPacket.cs
A	src/Rasa.Game/Packets/Party/Server/RemoveSquadMemberPacket.cs
A	src/Rasa.Game/Packets/Party/Server/RequestToJoinLeaderConfirmationRequestPacket.cs
M	src/Rasa.Game/Packets/Party/Server/SetPartyLeaderPacket.cs
A	src/Rasa.Game/Packets/Party/Server/SquadMemberListPacket.cs
A	src/Rasa.Game/Packets/Party/Server/VoiceChatAvailablePacket.cs
A	src/Rasa.Game/Packets/Petition/Client/AddToPetitionPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/CancelPetitionPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/CreateBugReportPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/CreateHelpRequestPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/RetrieveKBArticlePacket.cs
A	src/Rasa.Game/Packets/Petition/Client/RetrievePetitionPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/SearchKBPacket.cs
A	src/Rasa.Game/Packets/Petition/Client/SearchPetitionsPacket.cs
A	src/Rasa.Game/Packets/Petition/PetitionArgs.cs
A	src/Rasa.Game/Packets/Petition/Server/AddToPetitionAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/CancelPetitionAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/CreatePetitionAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/RetrieveKBArticleAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/RetrievePetitionAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/SearchKBAckPacket.cs
A	src/Rasa.Game/Packets/Petition/Server/SearchPetitionsAckPacket.cs
M	src/Rasa.Game/Packets/Protocol/CallServerMethodMessage.cs
M	src/Rasa.Game/Packets/Protocol/ProtocolPacket.cs
M	src/Rasa.Game/Packets/Queue/Client/ClientKeyPacket.cs
M	src/Rasa.Game/Packets/Queue/Client/QueueLoginPacket.cs
M	src/Rasa.Game/Packets/Social/Client/AddFriendByNamePacket.cs
A	src/Rasa.Game/Packets/Social/Client/AddFriendPacket.cs
M	src/Rasa.Game/Packets/Social/Client/AddIgnoreByNamePacket.cs
A	src/Rasa.Game/Packets/Social/Client/RemoveFriendByNamePacket.cs
A	src/Rasa.Game/Packets/Social/Client/RemoveIgnoreByNamePacket.cs
A	src/Rasa.Game/Packets/Social/SocialArgs.cs
A	src/Rasa.Game/Packets/Summon/Client/InviteFriendToJoinPacket.cs
A	src/Rasa.Game/Packets/Summon/Client/RequestInvitationToJoinPacket.cs
A	src/Rasa.Game/Packets/Summon/Client/RespondToAddAndJoinFriendPacket.cs
A	src/Rasa.Game/Packets/Summon/Client/RespondToJoinFriendPacket.cs
A	src/Rasa.Game/Packets/Summon/Client/RespondToRequestToJoinPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/CannotInvitePacket.cs
A	src/Rasa.Game/Packets/Summon/Server/CannotJoinPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/InvitationCancelledPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/InvitationDeclinedPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/InvitedToAddAndJoinFriendPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/InvitedToJoinFriendPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/JoinFriendCancelledPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/JoinFriendDeclinedPacket.cs
A	src/Rasa.Game/Packets/Summon/Server/RequestToJoinPacket.cs
A	src/Rasa.Game/Packets/Summon/SummonArgs.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestAcceptTradeRequestPacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestAddItemToTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestCancelTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestChangeEnergyUnitAmountPacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestConfirmTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestRemoveItemFromTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/RequestUnconfirmTradePacket.cs
A	src/Rasa.Game/Packets/Trade/Client/TradeArgs.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeAddItemPacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeCompletedPacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeConfirmChangePacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeCreatePacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeDestroyPacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeEnergyUnitsChangePacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeInvitePacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeRemoveItemPacket.cs
A	src/Rasa.Game/Packets/Trade/Server/TradeUpdateItemPacket.cs
M	src/Rasa.Game/Properties/AssemblyInfo.cs
M	src/Rasa.Game/Queue/QueueClient.cs
M	src/Rasa.Game/Queue/QueueManager.cs
M	src/Rasa.Game/Rasa.Game.csproj
M	src/Rasa.Game/Structures/ActionData.cs
A	src/Rasa.Game/Structures/ActionInfo.cs
M	src/Rasa.Game/Structures/Actor.cs
A	src/Rasa.Game/Structures/AuctionStatus.cs
M	src/Rasa.Game/Structures/AutoFireTimer.cs
M	src/Rasa.Game/Structures/BehaviorState.cs
M	src/Rasa.Game/Structures/ChatChannel.cs
D	src/Rasa.Game/Structures/ChatChannelPlayerLink.cs
M	src/Rasa.Game/Structures/ClanMemberData.cs
A	src/Rasa.Game/Structures/CraftingJob.cs
M	src/Rasa.Game/Structures/Creature.cs
M	src/Rasa.Game/Structures/DynamicObject.cs
M	src/Rasa.Game/Structures/EntityClass.cs
M	src/Rasa.Game/Structures/Friend.cs
M	src/Rasa.Game/Structures/GameEffect.cs
A	src/Rasa.Game/Structures/GameplayRejectionException.cs
M	src/Rasa.Game/Structures/IgnoredPlayer.cs
A	src/Rasa.Game/Structures/InvalidProgressionLevelException.cs
M	src/Rasa.Game/Structures/Inventory.cs
M	src/Rasa.Game/Structures/Item.cs
A	src/Rasa.Game/Structures/KbArticleInfo.cs
A	src/Rasa.Game/Structures/LookingForGroupAd.cs
A	src/Rasa.Game/Structures/LookingForGroupAdInfo.cs
M	src/Rasa.Game/Structures/LootDispenser.cs
M	src/Rasa.Game/Structures/LootItem.cs
M	src/Rasa.Game/Structures/Manifestation.cs
M	src/Rasa.Game/Structures/MapCell.cs
M	src/Rasa.Game/Structures/MapChannel.cs
A	src/Rasa.Game/Structures/MapLink.cs
A	src/Rasa.Game/Structures/MapMarkerState.cs
A	src/Rasa.Game/Structures/MapRegion.cs
M	src/Rasa.Game/Structures/Mission.cs
A	src/Rasa.Game/Structures/MissionConversationState.cs
M	src/Rasa.Game/Structures/MissionIndicator.cs
M	src/Rasa.Game/Structures/MissionInfo.cs
M	src/Rasa.Game/Structures/MissionLog.cs
M	src/Rasa.Game/Structures/MissionObjective.cs
A	src/Rasa.Game/Structures/MissionObjectiveConversation.cs
M	src/Rasa.Game/Structures/MissionObjectiveCounter.cs
A	src/Rasa.Game/Structures/MissionObjectiveDefinition.cs
A	src/Rasa.Game/Structures/MissionObjectiveItemCounter.cs
A	src/Rasa.Game/Structures/MissionObjectiveLog.cs
A	src/Rasa.Game/Structures/MissionProgressEvent.cs
A	src/Rasa.Game/Structures/MissionProgressRule.cs
A	src/Rasa.Game/Structures/MissionWire.cs
M	src/Rasa.Game/Structures/Party.cs
M	src/Rasa.Game/Structures/PartyMember.cs
A	src/Rasa.Game/Structures/PetitionInfo.cs
A	src/Rasa.Game/Structures/PlayerTransfer.cs
A	src/Rasa.Game/Structures/Recipe.cs
M	src/Rasa.Game/Structures/SpawnPool.cs
A	src/Rasa.Game/Structures/TradeSession.cs
A	src/Rasa.Game/Structures/UsableLock.cs
M	src/Rasa.Game/appsettings.json
A	src/Rasa.Game/kb-articles.json
A	src/Rasa.NavMesh/BuildSettings.cs
A	src/Rasa.NavMesh/NavMeshBuilder.cs
A	src/Rasa.NavMesh/Program.cs
A	src/Rasa.NavMesh/Rasa.NavMesh.csproj
A	src/Rasa.NavMesh/data/entity_meshes.csv
A	src/Rasa.Navigation/NavMeshFile.cs
A	src/Rasa.Navigation/NavMeshFlags.cs
A	src/Rasa.Navigation/NavMeshQuery.cs
A	src/Rasa.Navigation/Rasa.Navigation.csproj
M	src/Rasa.Shared/Data/CommOpcode.cs
A	src/Rasa.Shared/Packets/Communicator/AccountLockChangedPacket.cs
M	src/Rasa.Shared/Rasa.Shared.csproj
A	src/Rasa.Test/Compatibility/ContainerLayoutModels.cs
A	src/Rasa.Test/Compatibility/ContainerLayoutModelsTests.cs
A	src/Rasa.Test/Compatibility/PlatformCompatibilityTests.cs
A	src/Rasa.Test/Cryptography/CompatibilityTests.cs
A	src/Rasa.Test/Database/MySqlPlatformCompatibilityTests.cs
A	src/Rasa.Test/Database/PersistenceIntegrationTests.cs
A	src/Rasa.Test/Gameplay/AbilityTrayConsolidationTests.cs
A	src/Rasa.Test/Gameplay/AttributeProgressionTests.cs
A	src/Rasa.Test/Gameplay/CurrencyCallSiteTests.cs
A	src/Rasa.Test/Gameplay/LightningConsolidationTests.cs
A	src/Rasa.Test/Gameplay/LootConsolidationTests.cs
A	src/Rasa.Test/Gameplay/ProgressionPersistenceTests.cs
A	src/Rasa.Test/Gameplay/ProgressionTestContext.cs
A	src/Rasa.Test/Gameplay/WeaponAmmoConsolidationTests.cs
A	src/Rasa.Test/Gameplay/WeaponAmmoContext.cs
A	src/Rasa.Test/Memory/ArrayPoolTracker.cs
A	src/Rasa.Test/Memory/ArrayPoolTrackerTests.cs
M	src/Rasa.Test/Memory/NonContiguousMemoryStreamTests.cs
A	src/Rasa.Test/Missions/MissionDefinitionCatalogTests.cs
A	src/Rasa.Test/Missions/MissionLifecycleTests.cs
A	src/Rasa.Test/Missions/MissionPersistenceTests.cs
A	src/Rasa.Test/Missions/MissionProgressTests.cs
A	src/Rasa.Test/Missions/MissionProtocolTests.cs
A	src/Rasa.Test/Missions/MissionReviewFindingTests.cs
A	src/Rasa.Test/Missions/MissionRewardTests.cs
A	src/Rasa.Test/Missions/MissionTestContext.cs
A	src/Rasa.Test/Missions/MissionTrackerTests.cs
A	src/Rasa.Test/Networking/GameInboundOwnershipTests.cs
A	src/Rasa.Test/Networking/HandshakeValidationTests.cs
A	src/Rasa.Test/Networking/LengthedSocketTests.cs
A	src/Rasa.Test/Networking/ProtocolFramingTests.cs
A	src/Rasa.Test/Networking/ProtocolInflaterTests.cs
A	src/Rasa.Test/Networking/ProtocolValueReaderTests.cs
A	src/Rasa.Test/Networking/RpcPayloadTests.cs
A	src/Rasa.Test/Protocol/CompressedPacketTests.cs
M	src/Rasa.Test/Rasa.Test.csproj
A	src/Rasa.Test/World/DepartureFailureTests.cs
A	src/Rasa.Test/World/DropshipTravelTests.cs
A	src/Rasa.Test/World/MovementEncodingTests.cs
A	src/Rasa.Test/World/MovementTests.cs
A	src/Rasa.Test/World/SpawnPoolLifecycleTests.cs
A	src/Rasa.Test/World/SpawnPoolTests.cs
A	src/Rasa.Test/World/WaypointPersistenceTests.cs
A	src/Rasa.Test/World/WaypointTravelTests.cs
A	src/Rasa.Test/World/WorldLifecycleTests.cs
A	src/Rasa.Test/World/WorldTestContext.cs
M	src/Rasa.Utils/Commands/CommandProcessor.cs
M	src/Rasa.Utils/Cryptography/AuthCryptManager.cs
M	src/Rasa.Utils/Extensions/BinaryReaderExtensions.cs
M	src/Rasa.Utils/Extensions/BinaryWriterExtensions.cs
M	src/Rasa.Utils/Memory/BufferManager.cs
M	src/Rasa.Utils/Networking/LengthedSocket.cs
M	src/Rasa.Utils/Packets/PacketRouter.cs
M	src/Rasa.Utils/Properties/AssemblyInfo.cs
M	src/Rasa.Utils/Rasa.Utils.csproj
M	src/Rasa.Utils/Threading/MainLoop.cs
M	src/Rasa.Utils/Timer/TimedItem.cs
M	src/Rasa.Utils/Timer/Timer.cs
```

## Self-review
- I merged the approved mission baseline directly rather than reimplementing or flattening mission behavior.
- The merge remained a normal two-parent merge commit.
- No merge conflicts occurred, so there was no risk of accidental behavior rewrites during conflict resolution.
- The required mission-focused test run, the full solution test run, and the release build all passed before any Bootcamp-specific changes were introduced.
- I left the unrelated `.playwright-mcp/` directory untouched.
- I did not modify or stage the task brief or progress scratch file.

## Working tree state after validation, before the report commit
```text
## therealjohn-bootcamp-mission-roadmap
?? .playwright-mcp/
?? .superpowers/sdd/progress.md
?? .superpowers/sdd/task-1-brief.md
```
