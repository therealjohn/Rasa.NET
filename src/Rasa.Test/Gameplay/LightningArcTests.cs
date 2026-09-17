extern alias RasaGame;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.Manifestation.Server;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.MapChannel.Server.PerformRecovery;
    using Rasa.Structures;
    using Rasa.Test.World;
    using ClientState = RasaGame::Rasa.Data.ClientState;

    [TestClass]
    [DoNotParallelize]
    public class LightningArcTests
    {
        [TestMethod]
        [DataRow(1, false, 38, 180, 0, 25)]
        [DataRow(2, false, 38, 240, 210, 50)]
        [DataRow(2, true, 38, 300, 210, 50)]
        [DataRow(2, false, 78, 276, 241, 50)]
        [DataRow(2, true, 78, 345, 241, 50)]
        public void CompletionPublishesActualElectricComponentsAndSpendsPowerOnceWithoutAmmo(
            int rank, bool maximum, int mind, int primaryDamage, int arcDamage, int power)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            context.Roll = (low, high) => maximum ? high - 1 : low;
            context.Client.Player.Attributes[Attributes.Mind].CurrentMax = mind;
            context.Client.Player.Attributes[Attributes.Power].Current = power;
            var primary = context.Target(60);
            var secondary = context.Target(72);
            var saves = context.Storage.SaveAttempts;
            context.Cast(rank, primary.EntityId);
            Assert.AreEqual(11200L, context.Client.Player.NextLightningTime);
            var action = context.Map.PerformRecovery.Single();

            context.Advance(499);
            Assert.AreEqual(10000, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(power, context.Client.Player.Attributes[Attributes.Power].Current);
            context.Advance(1);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, action);

            Assert.AreEqual(10000 - primaryDamage, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000 - arcDamage, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(7u, context.Storage.Weapon.CurrentAmmo);
            Assert.AreEqual(7u, context.Storage.Read(context.Storage.Weapon).AmmoCount);
            Assert.AreEqual(saves, context.Storage.SaveAttempts);
            Assert.IsNull(context.Client.Player.PendingAbility);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            Assert.AreEqual(0, context.Map.QueuedMissiles.Count);
            var packets = context.Drain();
            Assert.AreEqual(1, packets.OfType<UpdatePowerPacket>().Count());
            Assert.AreEqual(1, packets.OfType<ActionReuseTimerRestartedPacket>().Count());
            Assert.AreEqual(0, packets.OfType<WeaponAttackRecovery>().Count());
            var hit = ReadPrimary(packets.OfType<LightningRecovery>().Single(), rank, primary.EntityId);
            Assert.AreEqual((long)primaryDamage, hit.FinalAmt);
            Assert.AreEqual(rank == 2 ? 1 : 0, hit.LightningArcs.Count);
            if (rank == 2)
            {
                var arc = hit.LightningArcs.Single();
                Assert.AreEqual(secondary.EntityId, arc.EntityId);
                Assert.AreEqual(DamageType.Electrical, arc.DamageType);
                Assert.AreEqual((long)arcDamage, arc.FinalAmt);
            }
        }

        [TestMethod]
        public void PrimaryOnlyRankTwoIsValidAndRetainsTheTwelveHundredMillisecondCooldown()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            context.Cast(2, primary.EntityId);
            context.Advance(500);
            Assert.AreEqual(950, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(), 2,
                primary.EntityId).LightningArcs.Count);
            context.Now += 699;
            context.Cast(2, primary.EntityId);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            context.Now++;
            context.Cast(2, primary.EntityId);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(500);
            Assert.AreEqual(900, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(9520, primary.Attributes[Attributes.Health].Current);
        }

        [TestMethod]
        public void RankTwoAdmissionRequiresFiftyPowerWithoutReservingCooldownOnRejection()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            context.Client.Player.Attributes[Attributes.Power].Current = 49;
            context.Cast(2, primary.EntityId);
            context.Advance(500);
            Assert.AreEqual(49, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(0L, context.Client.Player.NextLightningTime);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            Assert.AreEqual(10000, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(1, context.Drain().OfType<ActionFailedPacket>().Count());
        }

        [TestMethod]
        [DataRow(12f, 0f, 0f, true)]
        [DataRow(-12f, 0f, 0f, true)]
        [DataRow(0f, 12f, 0f, true)]
        [DataRow(0f, 0f, -12f, true)]
        [DataRow(12.001f, 0f, 0f, false)]
        [DataRow(0f, 12.001f, 0f, false)]
        [DataRow(8f, 0f, 9f, false)]
        [DataRow(float.NaN, 0f, 0f, false)]
        [DataRow(0f, float.PositiveInfinity, 0f, false)]
        [DataRow(0f, 0f, float.NegativeInfinity, false)]
        public void ArcRangeIsFiniteThreeDimensionalAndInclusiveAroundThePrimary(float x, float y, float z, bool hit)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            secondary.Position = primary.Position + new Vector3(x, y, z);
            context.Cast(2, primary.EntityId);
            context.Advance(500);
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(hit ? 9790 : 10000, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(hit ? 1 : 0, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(),
                2, primary.EntityId).LightningArcs.Count);
        }

        [TestMethod]
        [DataRow("friendly")]
        [DataRow("dead")]
        [DataRow("dying")]
        [DataRow("zero-health")]
        [DataRow("missing-health")]
        [DataRow("negative-armor")]
        [DataRow("missing-armor")]
        [DataRow("other-map")]
        [DataRow("removed")]
        [DataRow("unregistered")]
        [DataRow("untyped")]
        [DataRow("wrong-type")]
        [DataRow("replaced")]
        public void IneligibleSecondaryIsExcludedWithoutCancellingThePrimary(string invalid)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            context.Cast(2, primary.EntityId);
            if (invalid == "friendly") secondary.Faction = Factions.AFS;
            if (invalid == "dead") secondary.State = CharacterState.Dead;
            if (invalid == "dying") secondary.State = CharacterState.Dying;
            if (invalid == "zero-health") secondary.Attributes[Attributes.Health].Current = 0;
            if (invalid == "missing-health") secondary.Attributes.Remove(Attributes.Health);
            if (invalid == "negative-armor") secondary.Attributes[Attributes.Armor].Current = -1;
            if (invalid == "missing-armor") secondary.Attributes.Remove(Attributes.Armor);
            if (invalid == "other-map") secondary.MapContextId++;
            if (invalid == "removed")
                foreach (var cell in context.Map.MapCellInfo.Cells.Values) cell.CreatureList.Remove(secondary);
            if (invalid == "unregistered") EntityManager.Instance.UnregisterCreature(secondary.EntityId);
            if (invalid is "untyped" or "wrong-type") EntityManager.Instance.UnregisterEntity(secondary.EntityId);
            if (invalid == "wrong-type") EntityManager.Instance.RegisterEntity(secondary.EntityId, EntityType.Character);
            if (invalid == "replaced") EntityManager.Instance.Creatures[secondary.EntityId] = context.Target(100);
            var health = secondary.Attributes.GetValueOrDefault(Attributes.Health)?.Current;

            context.Advance(500);
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(health, secondary.Attributes.GetValueOrDefault(Attributes.Health)?.Current);
            Assert.AreEqual(950, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(0, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(),
                2, primary.EntityId).LightningArcs.Count);
        }

        [TestMethod]
        public void SecondaryComesFromCompletionTimeCellsNotAdmissionOrUnrelatedMaps()
        {
            using var context = new AbilityTestContext();
            using var other = new AbilityTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            var original = context.Target(11);
            var unrelated = other.Target(10);
            context.Cast(2, primary.EntityId);
            original.Position = new Vector3(100, 0, 0);
            var arriving = context.Target(12);
            context.Advance(500);
            Assert.AreEqual(10000, original.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000, unrelated.Attributes[Attributes.Health].Current);
            Assert.AreEqual(9790, arriving.Attributes[Attributes.Health].Current);
            Assert.AreEqual(arriving.EntityId, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(),
                2, primary.EntityId).LightningArcs.Single().EntityId);
        }

        [TestMethod]
        public void SelectionUsesNearestDistanceThenEntityIdentityRegardlessOfCellOrderOrDuplicates()
        {
            for (var iteration = 0; iteration < 2; iteration++)
            {
                using var context = new AbilityTestContext();
                context.Learn(49, 2);
                var primary = context.Target(24);
                var farther = context.Target(27);
                var first = context.Target(25);
                var second = context.Target(23);
                var lowerId = first.EntityId < second.EntityId ? first : second;
                var higherId = first.EntityId < second.EntityId ? second : first;
                var cells = context.Map.MapCellInfo.Cells.Values.ToArray();
                foreach (var cell in cells) cell.CreatureList.Reverse();
                cells.Last().CreatureList.Add(lowerId);
                context.Cast(2, primary.EntityId);
                context.Advance(500);
                Assert.AreEqual(9790, lowerId.Attributes[Attributes.Health].Current);
                Assert.AreEqual(10000, higherId.Attributes[Attributes.Health].Current);
                Assert.AreEqual(10000, farther.Attributes[Attributes.Health].Current);
                Assert.AreEqual(lowerId.EntityId, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(),
                    2, primary.EntityId).LightningArcs.Single().EntityId);
            }
        }

        [TestMethod]
        [DataRow("interrupt")]
        [DataRow("source-dead-revived")]
        [DataRow("source-depart-return")]
        [DataRow("source-replaced")]
        [DataRow("source-transferring")]
        [DataRow("target-dead-revived")]
        [DataRow("target-map-return")]
        [DataRow("target-replaced")]
        [DataRow("target-moved")]
        [DataRow("friendly")]
        [DataRow("rank-lost")]
        [DataRow("power")]
        [DataRow("ownership")]
        [DataRow("queue-removed")]
        [DataRow("action-tampered")]
        public void CancelledOrStaleRankTwoCannotDamageEitherTargetOrBeReplayed(string change)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 2);
            var player = context.Client.Player;
            var primary = context.Target();
            var secondary = context.Target(11);
            context.Cast(2, primary.EntityId);
            var action = context.Map.PerformRecovery.Single();
            if (change == "interrupt") ActorManager.Instance.RequestActionInterrupt(context.Client,
                new RequestActionInterruptPacket { ActionId = ActionId.AaRecruitLightning, ActionArgId = 2 });
            if (change == "source-dead-revived") { player.State = CharacterState.Dead; player.State = CharacterState.Normal; }
            if (change == "source-depart-return")
            {
                CellManager.Instance.RemoveFromWorld(context.Client);
                CellManager.Instance.AddToWorld(context.Client);
            }
            if (change == "source-transferring") context.Client.State = ClientState.Teleporting;
            if (change == "target-dead-revived") { primary.State = CharacterState.Dead; primary.State = CharacterState.Normal; }
            if (change == "target-map-return") { primary.MapContextId++; primary.MapContextId--; }
            if (change == "target-replaced") EntityManager.Instance.Creatures[primary.EntityId] = context.Target(100);
            if (change == "target-moved") primary.Position = new Vector3(60.001f, 0, 0);
            if (change == "friendly") player.GmFlagAlwaysFriendly = true;
            if (change == "rank-lost") player.Skills.Clear();
            if (change == "power") player.Attributes[Attributes.Power].Current = 49;
            if (change == "ownership") player.PendingAbility = null;
            if (change == "queue-removed") context.Map.PerformRecovery.Remove(action);
            if (change == "action-tampered") action.ActionArgId = 1;
            Manifestation replacement = null;
            if (change == "source-replaced") context.Client.Player = replacement = new Manifestation();
            context.Drain();
            context.Advance(500);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, action);
            context.Client.Player = player;
            if (replacement != null) EntityManager.Instance.FreeEntity(replacement.EntityId);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, action);
            Assert.AreEqual(10000, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(change == "power" ? 49 : 1000, player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(0, context.Drain().OfType<LightningRecovery>().Count());
        }

        [TestMethod]
        public void ConcurrentRankTwoRequestsAndPrematureOrWrongMapRecoveryStillYieldOneCast()
        {
            using var context = new AbilityTestContext();
            using var other = new WorldTestContext();
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            System.Threading.Tasks.Parallel.For(0, 12, _ => context.Cast(2, primary.EntityId));
            var action = context.Map.PerformRecovery.Single();
            var worker = new ActorActionManager(context.Manager);
            worker.PerformRecovery(other.Map, action);
            worker.PerformRecovery(context.Map, action);
            Assert.AreSame(action, context.Map.PerformRecovery.Single());
            context.Advance(500);
            System.Threading.Tasks.Parallel.For(0, 12, _ => worker.PerformRecovery(context.Map, action));
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(9790, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(950, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(1, context.Drain().OfType<LightningRecovery>().Count());
        }

        [TestMethod]
        public void RepeatedMissileDispatchCannotRepeatEitherHitOrRewriteAnAlreadyQueuedRecovery()
        {
            using var context = new AbilityTestContext();
            var primary = context.Target();
            var secondary = context.Target(11);
            var missile = new Missile
            {
                Source = context.Client.Player, SourceLifetime = context.Client.Player.ActionLifetime,
                TargetActor = primary, TargetEntityId = primary.EntityId, TargetLifetime = primary.ActionLifetime,
                ActionId = ActionId.AaRecruitLightning, ActionArgId = 2, DamageA = 240,
                Arc = new LightningArc(secondary, 210)
            };
            System.Threading.Tasks.Parallel.For(0, 12, _ => MissileManager.Instance.MissileTrigger(context.Map, missile));
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(9790, secondary.Attributes[Attributes.Health].Current);
            var packet = context.Drain().OfType<LightningRecovery>().Single();
            var expected = AbilityTestContext.Encode(packet);
            missile.Args.HitData.Single().LightningArcs.Single().FinalAmt = 9999;
            missile.Args.HitData.Clear();
            missile.DamageA = 9999;
            MissileManager.Instance.MissileTrigger(context.Map, missile);
            Assert.AreEqual(9760, primary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(9790, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, context.Drain().Count);
            CollectionAssert.AreEqual(expected, AbilityTestContext.Encode(packet));
        }

        [TestMethod]
        public void PrimaryAndArcUseArmorDeathExperienceAndLootPathsExactlyOnce()
        {
            using var context = new AbilityTestContext();
            using var rewards = new CreatureRewardsScope(context);
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            primary.Attributes[Attributes.Health].Current = 1;
            secondary.Attributes[Attributes.Health].Current = 10;
            secondary.Attributes[Attributes.Armor].Current = 200;
            context.Cast(2, primary.EntityId);
            var action = context.Map.PerformRecovery.Single();
            var saves = context.Storage.SaveAttempts;
            context.Advance(500);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, action);
            CreatureManager.Instance.HandleCreatureKill(context.Map, secondary, context.Client.Player);
            Assert.AreEqual(CharacterState.Dead, primary.State);
            Assert.AreEqual(CharacterState.Dead, secondary.State);
            Assert.AreEqual(0, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, secondary.Attributes[Attributes.Armor].Current);
            Assert.AreEqual(0, secondary.Attributes[Attributes.Health].RefreshAmount);
            Assert.AreEqual(saves + 2, context.Storage.SaveAttempts);
            Assert.IsTrue(context.Client.Player.Experience >= 180 && context.Client.Player.Experience <= 220);
            using var database = context.Storage.Open();
            Assert.AreEqual(context.Client.Player.Experience, database.CharacterEntries.Single().Experience);
            Assert.AreEqual(2, context.Map.LootDispensers.Count);
            CollectionAssert.AreEquivalent(new[] { primary.EntityId, secondary.EntityId },
                context.Map.LootDispensers.Values.Select(loot => loot.AttachedTo).ToArray());
            Assert.IsTrue(context.Map.LootDispensers.Values.All(loot => loot.Owner == context.Client.Player.EntityId));
            var packets = context.Drain();
            Assert.AreEqual(2, packets.OfType<ExperienceChangedPacket>().Count());
            Assert.AreEqual(2, packets.OfType<StateChangePacket>().Count());
            Assert.AreEqual(1, ReadPrimary(packets.OfType<LightningRecovery>().Single(), 2,
                primary.EntityId).LightningArcs.Count);
        }

        [TestMethod]
        [DataRow("secondary-revived")]
        [DataRow("secondary-map-return")]
        [DataRow("secondary-removed")]
        [DataRow("secondary-replaced")]
        [DataRow("secondary-friendly")]
        [DataRow("secondary-moved")]
        [DataRow("source-lifetime")]
        [DataRow("source-stunned")]
        [DataRow("source-zero-health")]
        public void ASelectedArcIsRevalidatedAfterPrimaryDeathBeforeReceivingDamage(string change)
        {
            using var context = new AbilityTestContext();
            using var rewards = new CreatureRewardsScope(context);
            context.Learn(49, 2);
            var primary = context.Target();
            var secondary = context.Target(11);
            var alternate = context.Target(12);
            primary.Attributes[Attributes.Health].Current = 1;
            context.Cast(2, primary.EntityId);
            context.Storage.BeforeSave = _ =>
            {
                if (change == "secondary-revived") { secondary.State = CharacterState.Dead; secondary.State = CharacterState.Normal; }
                if (change == "secondary-map-return") { secondary.MapContextId++; secondary.MapContextId--; }
                if (change == "secondary-removed")
                    foreach (var cell in context.Map.MapCellInfo.Cells.Values) cell.CreatureList.Remove(secondary);
                if (change == "secondary-replaced") EntityManager.Instance.Creatures[secondary.EntityId] = alternate;
                if (change == "secondary-friendly") secondary.Faction = Factions.AFS;
                if (change == "secondary-moved") secondary.Position = new Vector3(100, 0, 0);
                if (change == "source-lifetime") context.Client.Player.InvalidateActionLifetime();
                if (change == "source-stunned") context.Client.Player.State = CharacterState.Stunned;
                if (change == "source-zero-health") context.Client.Player.Attributes[Attributes.Health].Current = 0;
            };
            context.Advance(500);
            Assert.AreEqual(CharacterState.Dead, primary.State);
            Assert.AreEqual(10000, secondary.Attributes[Attributes.Health].Current);
            Assert.AreEqual(10000, alternate.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, ReadPrimary(context.Drain().OfType<LightningRecovery>().Single(), 2,
                primary.EntityId).LightningArcs.Count);
        }

        private static HitData ReadPrimary(LightningRecovery packet, int rank, ulong primaryId)
        {
            var decoded = LightningRecoveryTests.Decode(AbilityTestContext.Encode(packet));
            Assert.AreEqual(ActionId.AaRecruitLightning, decoded.ActionId);
            Assert.AreEqual((uint)rank, decoded.ActionArgId);
            CollectionAssert.AreEqual(new[] { primaryId }, decoded.Args.HitEntities);
            var primary = decoded.Args.HitData.Single();
            Assert.AreEqual(DamageType.Electrical, primary.DamageType);
            return primary;
        }

        private sealed class CreatureRewardsScope : IDisposable
        {
            private readonly FieldInfo _field = typeof(CreatureManager).GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object _previous;
            private readonly AbilityTestContext _context;

            internal CreatureRewardsScope(AbilityTestContext context)
            {
                _context = context;
                _previous = _field.GetValue(null);
                _field.SetValue(null, new CreatureManager(context.Storage, context.Manager));
            }

            public void Dispose()
            {
                _context.Storage.BeforeSave = null;
                foreach (var loot in _context.Map.LootDispensers.Values.ToArray())
                    CellManager.Instance.RemoveCreatureFromWorld(_context.Map, loot.Corpse);
                _field.SetValue(null, _previous);
            }
        }
    }
}
