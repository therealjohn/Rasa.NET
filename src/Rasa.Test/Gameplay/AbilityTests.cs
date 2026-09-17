extern alias RasaGame;

using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.MapChannel.Server.PerformRecovery;
    using Rasa.Packets.MapChannel.Client;
    using Rasa.Structures;
    using Rasa.Test.World;
    using ClientState = RasaGame::Rasa.Data.ClientState;

    [TestClass]
    [DoNotParallelize]
    public class AbilityTests
    {
        [TestMethod]
        public void ConcurrentLightningRequestsReserveOneActionAndSpendPowerOnce()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            System.Threading.Tasks.Parallel.For(0, 12, _ => context.Cast(target: target.EntityId));
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(500);
            Assert.AreEqual(975, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(9820, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(1, context.Drain().OfType<LightningRecovery>().Count());
        }

        [TestMethod]
        public void DirectEffectDetachmentAllowsTheNextSprintToggleToStartANewEffect()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Cast(action: ActionId.AaRecruitSprint);
            var effect = context.Client.Player.ActiveEffects.Values.Single();
            context.Now += 1000;
            GameEffectManager.Instance.DettachEffect(context.Map, context.Client.Player, effect);
            Assert.AreEqual(985, context.Client.Player.Attributes[Attributes.Chi].Current);
            context.Cast(action: ActionId.AaRecruitSprint);
            Assert.AreEqual(1, context.Client.Player.ActiveEffects.Count);
            Assert.AreNotEqual(effect.EffectId, context.Client.Player.ActiveEffects.Values.Single().EffectId);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DeathAndRevivalBeforeRecoveryStillInvalidatesTheOriginalActorLifetime(bool targetDies)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            context.Cast(target: target.EntityId);
            Actor actor = targetDies ? target : context.Client.Player;
            actor.State = CharacterState.Dead;
            actor.State = CharacterState.Normal;
            context.Advance(500);
            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(1000, context.Client.Player.Attributes[Attributes.Power].Current);
        }

        [TestMethod]
        public void AbilityDecodingPreservesTheEntireTargetIdentity()
        {
            const ulong identity = (1UL << 40) + 7;
            var packet = AbilityTestContext.Decode<RequestPerformAbilityPacket>(writer =>
            {
                writer.WriteTuple(4);
                writer.WriteInt(194);
                writer.WriteInt(1);
                writer.WriteULong(identity);
                writer.WriteNoneStruct();
            });
            Assert.AreEqual(identity, packet.Target);
        }

        [TestMethod]
        [DataRow("dead")]
        [DataRow("revived-before-tick")]
        [DataRow("depart-return")]
        [DataRow("transferring")]
        [DataRow("rank-lost")]
        [DataRow("source-replaced")]
        public void SprintStopsAtLifecycleBoundariesAndCannotAffectAReplacementCharacter(string change)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            var player = context.Client.Player;
            context.Cast(action: ActionId.AaRecruitSprint);
            context.Drain();
            if (change == "dead") player.State = CharacterState.Dead;
            if (change == "revived-before-tick")
            {
                player.State = CharacterState.Dead;
                player.State = CharacterState.Normal;
            }
            if (change == "depart-return")
            {
                CellManager.Instance.RemoveFromWorld(context.Client);
                CellManager.Instance.AddToWorld(context.Client);
            }
            if (change == "transferring") context.Client.State = ClientState.Teleporting;
            if (change == "rank-lost") player.Skills.Clear();
            Manifestation replacement = null;
            if (change == "source-replaced")
            {
                replacement = new Manifestation { MapChannel = context.Map, Attributes = new()
                {
                    [Attributes.Chi] = new ActorAttributes(Attributes.Chi, 100, 100, 73, 0, 0)
                }};
                context.Client.Player = replacement;
            }
            context.Now += 1000;
            GameEffectManager.Instance.DoWork(context.Map, 1000);
            Assert.AreEqual(0, player.ActiveEffects.Count);
            Assert.AreEqual(1d, player.MovementSpeed);
            if (replacement != null)
            {
                Assert.AreEqual(73, replacement.Attributes[Attributes.Chi].Current);
                context.Client.Player = player;
                EntityManager.Instance.FreeEntity(replacement.EntityId);
            }
            Assert.AreEqual(1, context.Drain().OfType<GameEffectDetachedPacket>().Count());
        }

        [TestMethod]
        public void SprintUsesActualElapsedTimeAcrossIrregularTicksAndAnUnchangedPosition()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Client.Player.InCombatMode = true;
            context.Cast(action: ActionId.AaRecruitSprint);
            foreach (var elapsed in new[] { 17, 483, 1, 1499 })
            {
                context.Now += elapsed;
                GameEffectManager.Instance.DoWork(context.Map, 50);
            }
            Assert.AreEqual(970, context.Client.Player.Attributes[Attributes.Chi].Current);
            GameEffectManager.Instance.DoWork(context.Map, 999999);
            Assert.AreEqual(970, context.Client.Player.Attributes[Attributes.Chi].Current);
            context.Now -= 1000;
            GameEffectManager.Instance.DoWork(context.Map, 1000);
            context.Now += 1000;
            GameEffectManager.Instance.DoWork(context.Map, 1000);
            Assert.AreEqual(970, context.Client.Player.Attributes[Attributes.Chi].Current);
        }

        [TestMethod]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        public void LightningRanksWithMissingWireContractsFailExplicitlyWithoutPartialExecution(int rank)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 5);
            var target = context.Target();
            context.Cast(rank, target.EntityId);
            context.Advance(5000);
            Assert.AreEqual(0L, context.Client.Player.NextLightningTime);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(1000, context.Client.Player.Attributes[Attributes.Power].Current);
            var failure = context.Drain().OfType<ActionFailedPacket>().Single();
            Assert.AreEqual(rank, failure.ActionArgId);
            context.Cast(1, target.EntityId);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count, "Unsupported ranks must not reserve cooldown.");
        }

        [TestMethod]
        [DataRow("unlearned")]
        [DataRow("mismatched-skill")]
        [DataRow("mismatched-id")]
        [DataRow("rank-zero")]
        [DataRow("rank-six")]
        [DataRow("rank-negative")]
        [DataRow("rank-above-learned")]
        [DataRow("unsupported")]
        [DataRow("power")]
        [DataRow("dead")]
        [DataRow("zero-health")]
        [DataRow("loading")]
        [DataRow("removed")]
        [DataRow("no-cell")]
        [DataRow("no-client")]
        [DataRow("unregistered")]
        [DataRow("busy")]
        [DataRow("stunned")]
        public void InvalidAbilityAdmissionCannotQueueOrSpendResources(string invalid)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            var player = context.Client.Player;
            var rank = 1;
            var action = ActionId.AaRecruitLightning;
            if (invalid == "unlearned") player.Skills.Clear();
            if (invalid == "mismatched-skill") player.Skills[(SkillId)49].SkillId = (SkillId)165;
            if (invalid == "mismatched-id") player.Skills[(SkillId)49].AbilityId = 401;
            if (invalid == "rank-zero") rank = 0;
            if (invalid == "rank-six") rank = 6;
            if (invalid == "rank-negative") rank = -1;
            if (invalid == "rank-above-learned") rank = 2;
            if (invalid == "unsupported") action = (ActionId)999;
            if (invalid == "power") player.Attributes[Attributes.Power].Current = 24;
            if (invalid == "dead") player.State = CharacterState.Dead;
            if (invalid == "zero-health") player.Attributes[Attributes.Health].Current = 0;
            if (invalid == "loading") context.Client.State = ClientState.Loading;
            if (invalid == "removed") player.RemoveFromMap = true;
            if (invalid == "no-cell") CellManager.Instance.RemoveFromWorld(context.Client);
            if (invalid == "no-client") context.Map.ClientList.Remove(context.Client);
            if (invalid == "unregistered") EntityManager.Instance.UnregisterPlayer(player.EntityId);
            if (invalid == "busy") player.CurrentAction = 1;
            if (invalid == "stunned") player.State = CharacterState.Stunned;
            var power = player.Attributes[Attributes.Power].Current;
            context.Drain();
            context.Cast(rank, target.EntityId, action);
            context.Advance(500);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            Assert.AreEqual(power, player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, context.Drain().OfType<LightningRecovery>().Count());
        }

        [TestMethod]
        [DataRow("missing")]
        [DataRow("self")]
        [DataRow("friendly")]
        [DataRow("dead")]
        [DataRow("zero-health")]
        [DataRow("out-of-range")]
        [DataRow("source-range")]
        [DataRow("vertical-range")]
        [DataRow("nonfinite")]
        [DataRow("other-map")]
        [DataRow("removed")]
        public void LightningRejectsInvalidHostilityIdentityAndThreeDimensionalRange(string invalid)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            var id = target.EntityId;
            if (invalid == "missing") id = 0;
            if (invalid == "self") id = context.Client.Player.EntityId;
            if (invalid == "friendly") target.Faction = Factions.AFS;
            if (invalid == "dead") target.State = CharacterState.Dead;
            if (invalid == "zero-health") target.Attributes[Attributes.Health].Current = 0;
            if (invalid == "out-of-range") target.Position = new Vector3(60.001f, 0, 0);
            if (invalid == "source-range") context.Client.Player.Position = new Vector3(-50.001f, 0, 0);
            if (invalid == "vertical-range") target.Position = new Vector3(0, 60.001f, 0);
            if (invalid == "nonfinite") target.Position = new Vector3(float.NaN, 0, 0);
            if (invalid == "other-map") target.MapContextId++;
            if (invalid == "removed") foreach (var cell in context.Map.MapCellInfo.Cells.Values) cell.CreatureList.Remove(target);
            var health = target.Attributes[Attributes.Health].Current;
            context.Cast(target: id);
            context.Advance(500);
            Assert.AreEqual(health, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(1000, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(1, context.Drain().OfType<ActionFailedPacket>().Count());
        }

        [TestMethod]
        [DataRow("interrupt")]
        [DataRow("death")]
        [DataRow("depart-return")]
        [DataRow("target-moved")]
        [DataRow("target-dead")]
        [DataRow("target-replaced")]
        [DataRow("target-other-map")]
        [DataRow("power-spent")]
        [DataRow("rank-lost")]
        [DataRow("source-replaced")]
        [DataRow("busy")]
        public void RecoveryRevalidatesLifetimesAndCurrentResourcesAndIsNeverReplayable(string change)
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var player = context.Client.Player;
            var target = context.Target();
            context.Cast(target: target.EntityId);
            var pending = context.Map.PerformRecovery.Single();
            if (change == "interrupt")
                ActorManager.Instance.RequestActionInterrupt(context.Client,
                    new RequestActionInterruptPacket { ActionId = ActionId.AaRecruitLightning, ActionArgId = 1 });
            if (change == "death") player.State = CharacterState.Dead;
            if (change == "depart-return")
            {
                CellManager.Instance.RemoveFromWorld(context.Client);
                CellManager.Instance.AddToWorld(context.Client);
            }
            if (change == "target-moved") target.Position = new Vector3(61, 0, 0);
            if (change == "target-dead") target.State = CharacterState.Dead;
            if (change == "target-replaced") EntityManager.Instance.Creatures[target.EntityId] = context.Target();
            if (change == "target-other-map") target.MapContextId++;
            if (change == "power-spent") player.Attributes[Attributes.Power].Current = 24;
            if (change == "rank-lost") player.Skills.Clear();
            if (change == "busy") player.CurrentAction = 1;
            Manifestation replacement = null;
            if (change == "source-replaced")
            {
                replacement = new Manifestation();
                context.Client.Player = replacement;
            }
            context.Drain();
            context.Advance(500);
            context.Client.Player = player;
            if (replacement != null) EntityManager.Instance.FreeEntity(replacement.EntityId);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, pending);
            Assert.AreEqual(change == "power-spent" ? 24 : 1000, player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(0, context.Drain().OfType<LightningRecovery>().Count());
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
        }

        [TestMethod]
        public void CooldownStartsAtAdmissionAndOnlyAllowsTheNextCastAtTwelveHundredMilliseconds()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            context.Cast(target: target.EntityId);
            context.Cast(target: target.EntityId);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(500);
            context.Now += 699;
            context.Cast(target: target.EntityId);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            context.Now++;
            context.Cast(target: target.EntityId);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(500);
            Assert.AreEqual(950, context.Client.Player.Attributes[Attributes.Power].Current);
            Assert.AreEqual(9640, target.Attributes[Attributes.Health].Current);
        }

        [TestMethod]
        [DataRow(false, 9820)]
        [DataRow(true, 9760)]
        public void LightningUsesInclusiveHistoricalDamageBounds(bool maximum, int health)
        {
            using var context = new AbilityTestContext();
            context.Roll = (low, high) => maximum ? high - 1 : low;
            context.Learn(49, 1);
            var target = context.Target();
            context.Cast(target: target.EntityId);
            context.Advance(500);
            Assert.AreEqual(health, target.Attributes[Attributes.Health].Current);
        }

        [TestMethod]
        public void LightningAppliesTheExistingMindBonusWithoutAddingLevelScaling()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            context.Client.Player.Attributes[Attributes.Mind].CurrentMax = 78;
            var target = context.Target();
            context.Cast(target: target.EntityId);
            context.Advance(500);
            Assert.AreEqual(9793, target.Attributes[Attributes.Health].Current); // 180 + 15%, no extra level factor.
        }

        [TestMethod]
        public void WrongMapOrPrematureRecoveryCannotConsumeTheScheduledAction()
        {
            using var context = new AbilityTestContext();
            using var other = new WorldTestContext();
            context.Learn(49, 1);
            var target = context.Target();
            context.Cast(target: target.EntityId);
            var action = context.Map.PerformRecovery.Single();
            var manager = new ActorActionManager(context.Manager);
            manager.PerformRecovery(other.Map, action);
            manager.PerformRecovery(context.Map, action);
            Assert.AreSame(action, context.Map.PerformRecovery.Single());
            Assert.AreEqual(1000, context.Client.Player.Attributes[Attributes.Power].Current);
            context.Advance(500);
            manager.PerformRecovery(context.Map, action);
            Assert.AreEqual(9820, target.Attributes[Attributes.Health].Current);
        }

        [TestMethod]
        public void WeaponsAndLightningSerializePendingActionsWhileSprintRemainsActive()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 1);
            context.Learn(165, 1);
            context.Storage.AddAmmo(30);
            context.Manager.RequestWeaponReload(context.Client, true);
            context.Cast(target: context.Target().EntityId);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(1500);
            context.Cast(action: ActionId.AaRecruitSprint);
            context.Cast(target: context.Target().EntityId);
            Assert.IsFalse(context.Manager.PlayerTryFireWeapon(context.Client));
            context.Manager.RequestWeaponStow(context.Client);
            Assert.AreEqual(1, context.Map.PerformRecovery.Count);
            context.Advance(500);
            Assert.IsTrue(context.Manager.PlayerTryFireWeapon(context.Client));
            context.Storage.AddWeapon(5, 1);
            context.Manager.RequestArmWeapon(context.Client, 1);
            Assert.AreEqual(1.2, context.Client.Player.MovementSpeed, 0.000001);
            Assert.AreEqual(1, context.Client.Player.ActiveEffects.Count);
        }

        [TestMethod]
        [DataRow(1, 1.2, 970, 66667)]
        [DataRow(2, 1.3, 973, 74075)]
        [DataRow(3, 1.4, 975, 80000)]
        [DataRow(4, 1.5, 980, 100000)]
        [DataRow(5, 1.6, 982, 111112)]
        public void EverySprintRankUsesTheHistoricalSpeedAndDepletesAtItsFractionalBoundary(
            int rank, double speed, int remainingAtTwoSeconds, int depletionMilliseconds)
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 5);
            context.Cast(rank, action: ActionId.AaRecruitSprint);
            Assert.AreEqual(speed, context.Client.Player.MovementSpeed, 0.000001);
            var start = context.Now;
            context.Now = start + 2000;
            GameEffectManager.Instance.DoWork(context.Map, 2000);
            Assert.AreEqual(remainingAtTwoSeconds, context.Client.Player.Attributes[Attributes.Chi].Current);
            context.Now = start + depletionMilliseconds - 1;
            GameEffectManager.Instance.DoWork(context.Map, depletionMilliseconds - 2001);
            Assert.AreEqual(1, context.Client.Player.ActiveEffects.Count);
            context.Now++;
            GameEffectManager.Instance.DoWork(context.Map, 1);
            Assert.AreEqual(0, context.Client.Player.Attributes[Attributes.Chi].Current);
            Assert.AreEqual(0, context.Client.Player.ActiveEffects.Count);
            Assert.AreEqual(1d, context.Client.Player.MovementSpeed);
            Assert.AreEqual(1, context.Drain().OfType<GameEffectDetachedPacket>().Count());
        }

        [TestMethod]
        public void RapidSprintTogglesDoNotDiscardFractionalAdrenalineDebt()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            for (var i = 0; i < 100; i++)
            {
                context.Cast(action: ActionId.AaRecruitSprint);
                context.Now += 10;
                context.Cast(action: ActionId.AaRecruitSprint);
            }
            Assert.AreEqual(985, context.Client.Player.Attributes[Attributes.Chi].Current);
            Assert.AreEqual(0, context.Client.Player.ActiveEffects.Count);
            context.Manager.RemovePlayerCharacter(context.Client);
        }

        [TestMethod]
        public void RemovingTheCharacterStopsSprintImmediatelyWithoutWaitingForATick()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Cast(action: ActionId.AaRecruitSprint);
            context.Manager.RemovePlayerCharacter(context.Client);
            Assert.AreEqual(0, context.Client.Player.ActiveEffects.Count);
            Assert.AreEqual(1d, context.Client.Player.MovementSpeed);
        }

        [TestMethod]
        public void SprintIsAToggleWithFractionalElapsedDrainEvenWhileStandingStill()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 1);
            context.Cast(action: ActionId.AaRecruitSprint);
            Assert.AreEqual(1.2, context.Client.Player.MovementSpeed, 0.000001);
            Assert.AreEqual(1, context.Client.Player.ActiveEffects.Count);
            Assert.AreEqual(0, context.Map.PerformRecovery.Count);
            var attached = context.Drain().OfType<GameEffectAttachedPacket>().Single();
            Assert.AreEqual(247, attached.EffectTypeId);
            context.Now += 500;
            GameEffectManager.Instance.DoWork(context.Map, 500);
            Assert.AreEqual(993, context.Client.Player.Attributes[Attributes.Chi].Current);
            context.Now += 500;
            GameEffectManager.Instance.DoWork(context.Map, 500);
            Assert.AreEqual(985, context.Client.Player.Attributes[Attributes.Chi].Current);
            context.Cast(action: ActionId.AaRecruitSprint);
            Assert.AreEqual(1.0, context.Client.Player.MovementSpeed);
            Assert.AreEqual(0, context.Client.Player.ActiveEffects.Count);
            var detached = context.Drain().OfType<GameEffectDetachedPacket>().Single();
            Assert.AreEqual(attached.EffectId, detached.EffectId);
        }

        [TestMethod]
        public void LightningWaitsHalfASecondSpendsPowerOnceAndNeverSpendsAmmunition()
        {
            using var context = new AbilityTestContext();
            context.Learn(49, 5);
            var target = context.Target(60);
            context.Client.Player.Attributes[Attributes.Power].Current = 25;

            context.Cast(target: target.EntityId);
            var action = context.Map.PerformRecovery.Single();
            context.Advance(499);
            Assert.AreEqual(10000, target.Attributes[Attributes.Health].Current);
            Assert.AreEqual(25, context.Client.Player.Attributes[Attributes.Power].Current);
            context.Advance(1);
            Assert.IsTrue(target.Attributes[Attributes.Health].Current >= 9760 && target.Attributes[Attributes.Health].Current <= 9820);
            Assert.AreEqual(0, context.Client.Player.Attributes[Attributes.Power].Current);
            new ActorActionManager(context.Manager).PerformRecovery(context.Map, action);
            Assert.AreEqual(7u, context.Storage.Weapon.CurrentAmmo);
            var packets = context.Drain();
            Assert.AreEqual(1, packets.OfType<LightningRecovery>().Count());
            Assert.AreEqual(1, packets.OfType<UpdatePowerPacket>().Count());
        }
    }
}
