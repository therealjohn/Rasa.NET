using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Missions
{
    using Rasa.Data;
    using Rasa.Game;
    using Rasa.Managers;
    using Rasa.Packets.Mission.Server;
    using Rasa.Repositories.Char;
    using Rasa.Repositories.UnitOfWork;
    using Rasa.Repositories.World;
    using Rasa.Structures;
    using Rasa.Structures.Char;
    using Rasa.Structures.World;

    [TestClass]
    [DoNotParallelize]
    public class MissionReviewFindingTests
    {
        [TestMethod]
        public void DatabaseMissionLoadsOperationalAndCompletesThroughSuccessThenReward()
        {
            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var factory = new MissionLoadingFactory(
                context,
                new[]
                {
                    new NpcMissionEntry
                    {
                        Id = 321,
                        GiverId = 101,
                        ReciverId = 100,
                        Level = 5,
                        GroupType = 1,
                        CategoryId = 1,
                        Shareable = false,
                        RadioCompleteable = false,
                        Comment = "Assemble With Lieutenant Perkins"
                    },
                    new NpcMissionEntry
                    {
                        Id = 429,
                        GiverId = 101,
                        ReciverId = 100,
                        Level = 3,
                        GroupType = 2,
                        CategoryId = 2,
                        Shareable = true,
                        RadioCompleteable = true,
                        Comment = "River Recon"
                    }
                });
            var manager = new MissionManager(factory, new Dictionary<uint, Mission>());
            manager.LoadMissions();

            Assert.IsTrue(manager.LoadedMissions[321].IsOperational);
            Assert.IsTrue(manager.LoadedMissions[429].IsOperational);
            Assert.IsNull(manager.LoadedMissions[321].OperationalDiagnostic);
            Assert.IsTrue(manager.TryGetRewardInfo(321, out var reward));
            Assert.AreEqual(0, reward.FixedReward.Credits.Count);
            Assert.AreEqual(0, manager.LoadedMissions[321].Objectives.Count);

            var giver = context.AddNpc(101);
            var receiver = context.AddNpc(100);
            Assert.IsTrue(manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            Assert.IsTrue(context.Client.Player.Missions[321].Completeable);

            Assert.IsTrue(manager.TryCompleteNpcMission(
                context.Client, receiver.EntityId, 321, null, null));
            Assert.AreEqual(MissionState.Success, context.Client.Player.Missions[321].State);
            Assert.AreEqual((uint)MissionState.Success, context.ReadMission(321).MissionState);
            Assert.AreEqual(1, context.Drain().OfType<MissionCompletedPacket>().Count());

            Assert.IsTrue(manager.TryRewardNpcMission(
                context.Client, receiver.EntityId, 321, null, null));
            Assert.AreEqual(MissionState.Completed, context.Client.Player.Missions[321].State);
            Assert.AreEqual((uint)MissionState.Completed, context.ReadMission(321).MissionState);
            Assert.AreEqual(1, context.Drain().OfType<MissionRewardedPacket>().Count());
        }

        [TestMethod]
        public void IncompleteRecoveredAndUnsupportedRewardDefinitionsHaveExplicitDiagnostics()
        {
            var recovered = MissionDefinitionCatalog.CreateRecoveredInactiveDefinitions()[1069];
            StringAssert.Contains(recovered.OperationalDiagnostic, "objective");

            using var context = MissionTestContext.WithCustomDefinitions(
                new Dictionary<uint, Mission>());
            var factory = new MissionLoadingFactory(
                context,
                new[]
                {
                    new NpcMissionEntry
                    {
                        Id = 321,
                        GiverId = 101,
                        ReciverId = 100,
                        Level = 5,
                        GroupType = 1,
                        CategoryId = 1,
                        Comment = "Unsupported reward"
                    }
                },
                new[]
                {
                    new NpcMissionRewardEntry
                    {
                        Id = 321,
                        Type = 99,
                        Credits = 1,
                        ItemTemplateId = 2,
                        Quantity = 3
                    }
                });
            var manager = new MissionManager(factory, new Dictionary<uint, Mission>());
            manager.LoadMissions();

            Assert.IsFalse(manager.LoadedMissions[321].IsOperational);
            StringAssert.Contains(manager.LoadedMissions[321].OperationalDiagnostic, "reward");
            Assert.IsFalse(manager.TryGetRewardInfo(321, out _));
        }

        [TestMethod]
        public void TaskFourRepositoryContractKeepsAccountSlotGetAndNamedAggregateLookup()
        {
            using var context = new MissionTestContext();
            context.SeedCharacter(10, 3, 100);
            context.SeedMission(100, 321, (uint)MissionState.Active, false);
            using var unit = context.CreateChar();

            CollectionAssert.AreEqual(
                new uint[] { 321 },
                unit.CharacterMissions.Get(10, 3).Select(entry => entry.MissionId).ToArray());
            Assert.AreEqual(
                321U,
                unit.CharacterMissions.GetByCharacterAndMission(100, 321).MissionId);
        }

        [TestMethod]
        public void ObjectiveFailureFailsRequiredMissionAndClearRemovesTerminalState()
        {
            using var context = MissionTestContext.WithObjectiveMission();
            var giver = context.AddNpc(77);
            Assert.IsTrue(context.Manager.TryAcceptNpcMission(context.Client, giver.EntityId, 321));
            context.Drain();

            Assert.IsTrue(context.Manager.TryFailObjective(context.Client, 321, 5));
            Assert.AreEqual(MissionObjectiveState.Failed,
                context.Client.Player.Missions[321].Objectives[5].State);
            Assert.AreEqual(MissionState.Failed, context.Client.Player.Missions[321].State);
            var packets = context.Drain();
            Assert.AreEqual(1, packets.OfType<ObjectiveFailedPacket>().Count());
            Assert.AreEqual(1, packets.OfType<MissionFailedPacket>().Count());

            Assert.IsTrue(context.Manager.TryClear(context.Client, 321));
            Assert.IsFalse(context.Client.Player.Missions.ContainsKey(321));
            using var unit = context.CreateChar();
            Assert.IsNull(unit.CharacterMissions.GetByCharacterAndMission(1, 321));
            Assert.AreEqual(1, context.Drain().OfType<MissionClearedPacket>().Count());
            Assert.IsFalse(context.Manager.TryClear(context.Client, 321));
        }

        [TestMethod]
        public void ItemAcquisitionAndConsumptionAdvanceItemCountersThroughInventoryHooks()
        {
            AssertItemHook(MissionProgressEventKind.ItemAcquired, consume: false);
            AssertItemHook(MissionProgressEventKind.ItemConsumed, consume: true);
        }

        [TestMethod]
        public void SuccessfulObjectUseAdvancesMatchingInteractionObjective()
        {
            const uint objectClassId = 3147;
            using var context = MissionTestContext.WithProgressMission(
                MissionProgressRule.CompleteOnExactSubject(
                    MissionProgressEventKind.InteractionUsed,
                    objectClassId));
            context.SeedMission(1, 321, (uint)MissionState.Active, false);
            context.ReloadPlayerMissions();
            var controlPoint = new DynamicObject
            {
                EntityClassId = (EntityClasses)objectClassId,
                DynamicObjectType = DynamicObjectType.ControlPoint,
                MapContextId = context.Map.MapInfo.MapContextId,
                Position = context.Client.Player.Position,
                IsInWorld = true
            };
            controlPoint.TriggeredByPlayers.Add(context.Client);
            context.Map.ControlPoints.Add(500, controlPoint);
            var manager = new DynamicObjectManager(context, missionManager: context.Manager);

            manager.CaptureControlPointRecovery(
                context.Map,
                new ActionData(
                    context.Client.Player,
                    ActionId.UseObject,
                    DynamicObjectManager.ControlPointUseArgId,
                    0));

            Assert.AreEqual(MissionObjectiveState.Completed,
                context.Client.Player.Missions[321].Objectives[1].State);
            Assert.AreEqual(1, context.Drain().OfType<ObjectiveCompletedPacket>().Count());
        }

        [TestMethod]
        public void RewardPublicationFailureLeavesRuntimeTerminalAndCannotDuplicateCommit()
        {
            var published = 0;
            using var context = MissionTestContext.WithCompletableMission(
                429,
                _ =>
                {
                    if (published++ == 0)
                        throw new InvalidOperationException("Injected publication failure.");
                });
            var before = context.ReadRewardTotals();

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, null, null));
            context.Drain();
            Assert.IsTrue(context.Manager.TryRewardNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0, null));

            Assert.AreEqual(MissionState.Completed, context.Client.Player.Missions[429].State);
            Assert.AreEqual((uint)MissionState.Completed, context.ReadMission(429).MissionState);
            var after = context.ReadRewardTotals();
            Assert.AreEqual(before.ItemCount + 5, after.ItemCount);
            Assert.AreEqual(after.Experience, context.Client.Player.Experience);
            Assert.AreEqual(after.Credits,
                context.Client.Player.Credits[CurencyType.Credits]);
            Assert.AreEqual(after.Prestige,
                context.Client.Player.Credits[CurencyType.Prestige]);
            Assert.AreEqual(after.ItemCount, RuntimeItemCount(context.Client));

            Assert.IsFalse(context.Manager.TryRewardNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0, null));
            Assert.AreEqual(after, context.ReadRewardTotals());
        }

        [TestMethod]
        [DataRow("overflow")]
        [DataRow("capability")]
        public void UnrelatedProviderExceptionsRetainIdentityAndEscape(string kind)
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            var expected = kind == "overflow"
                ? (Exception)new OverflowException("Injected provider overflow.")
                : new NotSupportedException("Injected provider capability failure.");
            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, null, null));
            context.AfterSave = _ => throw expected;
            Exception actual = null;
            try
            {
                context.Manager.TryRewardNpcMission(
                    context.Client, context.Receiver.EntityId, 429, 0, null);
            }
            catch (Exception error)
            {
                actual = error;
            }

            Assert.AreSame(expected, actual);
        }

        [TestMethod]
        public void KnownRewardArithmeticOverflowIsRejectedAtItsGameplaySource()
        {
            using var context = MissionTestContext.WithCompletableMission(429);
            using (var unit = context.CreateChar())
                unit.Characters.UpdateCharacterCurrencies(
                    context.Client.Player.Id,
                    int.MaxValue,
                    context.Client.Player.Credits[CurencyType.Prestige]);
            context.Client.Player.Credits[CurencyType.Credits] = int.MaxValue;
            var before = context.ReadRewardTotals();

            Assert.IsTrue(context.Manager.TryCompleteNpcMission(
                context.Client, context.Receiver.EntityId, 429, null, null));
            Assert.IsFalse(context.Manager.TryRewardNpcMission(
                context.Client, context.Receiver.EntityId, 429, 0, null));

            Assert.AreEqual(before, context.ReadRewardTotals());
            Assert.AreEqual(MissionState.Success,
                context.Client.Player.Missions[429].State);
        }

        private static void AssertItemHook(MissionProgressEventKind kind, bool consume)
        {
            const uint itemClassId = 3147;
            using var context = MissionTestContext.WithItemProgressMission(kind, itemClassId, 3);
            context.SeedMission(1, 321, (uint)MissionState.Active, false);
            context.ReloadPlayerMissions();
            var item = context.CreateInventoryItem(28, itemClassId, 3);
            var inventory = new InventoryManager(context, context.Manager);

            var placed = inventory.AddItemToInventory(context.Client, item);
            Assert.IsNotNull(placed);
            if (consume)
            {
                context.Drain();
                inventory.ReduceStackCount(
                    context.Client, InventoryType.Personal, placed, 3);
            }

            Assert.AreEqual(3U,
                context.Client.Player.Missions[321].Objectives[1].ItemCounters[itemClassId]);
            Assert.AreEqual(1,
                context.Drain().OfType<UpdateObjectiveItemCounterPacket>().Count());
        }

        private static long RuntimeItemCount(Client client) =>
            client.Player.Inventory.PersonalInventory
                .Where(entityId => entityId != 0)
                .Select(EntityManager.Instance.GetItem)
                .Where(item => item != null)
                .Sum(item => (long)item.StackSize);

        private sealed class MissionLoadingFactory : IGameUnitOfWorkFactory
        {
            private readonly MissionTestContext _charFactory;
            private readonly IReadOnlyList<NpcMissionEntry> _missions;
            private readonly IReadOnlyList<NpcMissionRewardEntry> _rewards;

            internal MissionLoadingFactory(
                MissionTestContext charFactory,
                IReadOnlyList<NpcMissionEntry> missions,
                IReadOnlyList<NpcMissionRewardEntry> rewards = null)
            {
                _charFactory = charFactory;
                _missions = missions;
                _rewards = rewards ?? Array.Empty<NpcMissionRewardEntry>();
            }

            public ICharUnitOfWork CreateChar() => _charFactory.CreateChar();
            public IWorldUnitOfWork CreateWorld() =>
                new MissionWorldUnitOfWork(_missions, _rewards);
        }

        private sealed class MissionWorldUnitOfWork : IWorldUnitOfWork
        {
            internal MissionWorldUnitOfWork(
                IReadOnlyList<NpcMissionEntry> missions,
                IReadOnlyList<NpcMissionRewardEntry> rewards)
            {
                NpcMissions = new MissionRepository(missions);
                NpcMissionRewards = new RewardRepository(rewards);
            }

            public IActionRepository Actions => null;
            public IEquipmentRepository Equipment => null;
            public ICreatureRepository Creatures => null;
            public IEntityClassRepository EntityClasses => null;
            public IFootlockerRepository Footlockers => null;
            public ILogosRepository Logoses => null;
            public IMapInfoRepository MapInfos => null;
            public IMapLinkRepository MapLinks => null;
            public IKraftwerksRepository Kraftwerks => null;
            public IMapRegionRepository MapRegions => null;
            public IMapMarkerRepository MapMarkers => null;
            public IRecipeRepository Recipes => null;
            public INpcMissionRepository NpcMissions { get; }
            public INpcMissionRewardRepository NpcMissionRewards { get; }
            public INpcPackageRepository NpcPackages => null;
            public IPlayerRandomNameRepository RandomNames => null;
            public ISpawnpoolRepository Spawnpools => null;
            public ITeleporterRepository Teleporters => null;
            public void Complete() { }
            public void Reject() { }
            public IDbContextTransaction BeginTransaction() =>
                throw new NotSupportedException();
            public void Dispose() { }
        }

        private sealed class MissionRepository : INpcMissionRepository
        {
            private readonly IReadOnlyList<NpcMissionEntry> _missions;
            internal MissionRepository(IReadOnlyList<NpcMissionEntry> missions) =>
                _missions = missions;
            public List<NpcMissionEntry> Get() => _missions.ToList();
        }

        private sealed class RewardRepository : INpcMissionRewardRepository
        {
            private readonly IReadOnlyList<NpcMissionRewardEntry> _rewards;
            internal RewardRepository(IReadOnlyList<NpcMissionRewardEntry> rewards) =>
                _rewards = rewards;
            public List<NpcMissionRewardEntry> Get(uint missionId) =>
                _rewards.Where(entry => entry.Id == missionId).ToList();
        }
    }
}
