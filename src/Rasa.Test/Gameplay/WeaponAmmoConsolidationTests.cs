using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Gameplay
{
    using Rasa.Data;
    using Rasa.Managers;
    using Rasa.Packets.MapChannel.Server;
    using Rasa.Packets.Protocol;
    using Rasa.Test.World;

    [TestClass]
    [DoNotParallelize]
    public class WeaponAmmoConsolidationTests
    {
        [TestMethod]
        public void ShotPersistsBeforePublishingAndFailureLeavesClipUnchanged()
        {
            using var context = new WeaponAmmoContext();
            var manager = new ManifestationManager(context);
            context.BeforeSave = _ =>
            {
                Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
                Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
                throw new DbUpdateException("Injected shot failure.");
            };

            Assert.IsFalse(manager.PlayerTryFireWeapon(context.Client));

            Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(7u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0, context.World.Map.QueuedMissiles.Count);
            context.BeforeSave = null;

            Assert.IsTrue(manager.PlayerTryFireWeapon(context.Client));
            Assert.AreEqual(6u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(6u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(1, context.World.Map.QueuedMissiles.Count);
            Assert.AreEqual(6u, WorldTestContext.Drain(context.Client)
                .Select(packet => packet.Message).OfType<CallMethodMessage>()
                .Select(message => message.Packet).OfType<WeaponAmmoInfoPacket>()
                .Single().AmmoInfo);
        }

        [TestMethod]
        [DataRow(20u, 30u)]
        [DataRow(7u, 0u)]
        [DataRow(0u, 0u)]
        public void FullClipAndNoReserveDoNotScheduleOrChangeAmmo(uint clip, uint reserve)
        {
            using var context = new WeaponAmmoContext(clip);
            if (reserve > 0)
                context.AddAmmo(reserve);
            var manager = new ManifestationManager(context);

            manager.RequestWeaponReload(context.Client, true);
            manager.RequestWeaponReload(context.Client, true);

            Assert.AreEqual(0, context.World.Map.PerformRecovery.Count);
            Assert.AreEqual(0, context.SaveAttempts);
            Assert.AreEqual(clip, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
        }

        [TestMethod]
        public void ReloadCommitsClipAndReserveStacksBeforePublishing()
        {
            using var context = new WeaponAmmoContext();
            var first = context.AddAmmo(3);
            var second = context.AddAmmo(4, 51);
            var manager = new ManifestationManager(context);
            manager.RequestWeaponReload(context.Client, true);
            var action = context.World.Map.PerformRecovery.Single();
            WorldTestContext.Drain(context.Client);
            context.BeforeSave = database =>
            {
                Assert.IsNotNull(database.Database.CurrentTransaction);
                Assert.AreEqual(7u, context.Weapon.CurrentAmmo);
                Assert.AreEqual(3u, first.StackSize);
                Assert.AreEqual(4u, second.StackSize);
                Assert.AreEqual(0, WorldTestContext.Drain(context.Client).Count);
            };

            manager.WeaponReload(action);

            Assert.AreEqual(14u, context.Weapon.CurrentAmmo);
            Assert.AreEqual(14u, context.Read(context.Weapon).AmmoCount);
            Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[50]);
            Assert.AreEqual(0UL, context.Client.Player.Inventory.PersonalInventory[51]);
            var packets = WorldTestContext.Drain(context.Client)
                .Select(packet => packet.Message).OfType<CallMethodMessage>()
                .Select(message => message.Packet).ToList();
            Assert.AreEqual(14u, packets.OfType<WeaponAmmoInfoPacket>().Single().AmmoInfo);
            Assert.AreEqual(14u, packets.OfType<PerformRecoveryPacket>().Single().Arg);
        }
    }
}
