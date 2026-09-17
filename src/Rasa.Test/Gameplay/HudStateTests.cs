using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Structures;
    using Rasa.Managers;

    [TestClass]
    public class HudStateTests
    {
        [TestMethod]
        public void TheMapWorkerDeliversAllElapsedTimeAndExpiresEveryDueLegacyEffect()
        {
            using var context = new AbilityTestContext();
            var player = context.Client.Player;
            player.ActiveEffects.Add(700, new GameEffect { EffectId = 700, TypeId = 999, Duration = 1000 });
            player.ActiveEffects.Add(701, new GameEffect { EffectId = 701, TypeId = 998, Duration = 1000 });
            var manager = new MapChannelManager(context.Storage);
            manager.MapChannelArray.Add(context.Map.MapInfo.MapContextId, context.Map);
            manager.MapChannelWorker(499);
            Assert.AreEqual(2, player.ActiveEffects.Count);
            manager.MapChannelWorker(501);
            Assert.AreEqual(0, player.ActiveEffects.Count);
            CollectionAssert.AreEquivalent(new[] { 700, 701 },
                context.Drain().OfType<GameEffectDetachedPacket>().Select(packet => packet.EffectId).ToArray());
        }

        [TestMethod]
        public void NewObserversReceiveTheActiveSprintRankAndAuthoritativeMovementModifier()
        {
            using var context = new AbilityTestContext();
            context.Learn(165, 5);
            context.Cast(3, action: ActionId.AaRecruitSprint);
            var packets = context.Manager.CreatePlayerEntityData(context.Client);
            var movement = packets.OfType<MovementModChangePacket>().Single();
            var effect = packets.OfType<GameEffectAttachedPacket>().Single();
            Assert.AreEqual(1.4, movement.MovementMod, 0.000001);
            Assert.AreEqual(3u, effect.EffectLevel);
            Assert.AreEqual(context.Client.Player.EntityId, effect.SourceId);
            Assert.IsNull(effect.Duration, "A toggle must not advertise an invented fixed duration.");
            Assert.IsFalse(effect.Announced);
        }

        [TestMethod]
        public void PowerAndAdrenalinePacketsSnapshotTheResourceValuesAtPublication()
        {
            var power = new ActorAttributes(Attributes.Power, 100, 100, 25, 2, 1000);
            var chi = new ActorAttributes(Attributes.Chi, 200, 200, 15, 0, 0);
            var powerPacket = new UpdatePowerPacket(power, 0);
            var chiPacket = new UpdateChiPacket(chi, 0);
            var expectedPower = AbilityTestContext.Encode(powerPacket);
            var expectedChi = AbilityTestContext.Encode(chiPacket);
            power.Current = 0;
            chi.Current = 0;
            power.CurrentMax = 50;
            chi.RefreshAmount = 7;
            CollectionAssert.AreEqual(expectedPower, AbilityTestContext.Encode(powerPacket));
            CollectionAssert.AreEqual(expectedChi, AbilityTestContext.Encode(chiPacket));
        }
    }
}
