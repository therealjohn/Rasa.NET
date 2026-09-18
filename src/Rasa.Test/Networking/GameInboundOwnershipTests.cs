using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Networking
{
    using Rasa.Memory;
    using Rasa.Test.Memory;

    [TestClass]
    [DoNotParallelize]
    public class GameInboundOwnershipTests
    {
        [ClassInitialize]
        public static void Initialize(TestContext context)
        {
            if (Logger.Config == null)
                Logger.UpdateConfig(new Logger.LoggerConfig());

            BufferManager.Initialize(8192, 8, 8);
        }

        [TestMethod]
        public void ReceiveRacingDisconnectReturnsItsRentedChunk()
        {
            var client = new Rasa.Game.Client(null, new Rasa.Game.Handlers.ClientPacketHandler());
            var state = typeof(Rasa.Game.Client).GetProperty("State");
            state.SetValue(client, Enum.Parse(state.PropertyType, "Connected"));
            var data = BufferManager.RequestBuffer();
            data.Length = 100;
            var sync = typeof(Rasa.Game.Client).GetField("_pendingChunksLock",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client);
            var receive = typeof(Rasa.Game.Client).GetMethod("OnReceive",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(sync);
            Assert.IsNotNull(receive);

            Monitor.Enter(sync);
            try
            {
                using var started = new ManualResetEventSlim();
                var worker = Task.Run(() =>
                {
                    using var buffers = new ArrayPoolTracker(128);
                    started.Set();
                    receive.Invoke(client, new object[] { data });
                    buffers.AssertReturned();
                });

                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
                Thread.Sleep(25);
                state.SetValue(client, Enum.Parse(state.PropertyType, "Disconnected"));
                Monitor.Exit(sync);
                sync = null;

                Assert.IsTrue(worker.Wait(TimeSpan.FromSeconds(5)));
                worker.GetAwaiter().GetResult();
            }
            finally
            {
                if (sync != null)
                    Monitor.Exit(sync);

                BufferManager.FreeBuffer(data);
            }
        }
    }
}
