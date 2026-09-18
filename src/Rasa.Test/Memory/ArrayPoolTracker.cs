using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Rasa.Test.Memory
{
    internal sealed class ArrayPoolTracker : EventListener
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly int _trackedLength;
        private readonly HashSet<int> _rented = new();
        private readonly HashSet<int> _returned = new();

        internal int LargestRent { get; private set; }

        internal ArrayPoolTracker(int trackedLength = 32768)
        {
            _trackedLength = trackedLength;
        }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Buffers.ArrayPoolEventSource")
                EnableEvents(source, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (_rented == null || Environment.CurrentManagedThreadId != _thread ||
                data.Payload == null || data.Payload.Count < 2)
                return;

            var length = (int)data.Payload[1];
            if (data.EventName == "BufferRented")
                LargestRent = Math.Max(LargestRent, length);
            if (length != _trackedLength)
                return;

            if (data.EventName == "BufferRented")
                _rented.Add((int)data.Payload[0]);
            if (data.EventName == "BufferReturned")
                _returned.Add((int)data.Payload[0]);
        }

        internal void AssertReturned()
        {
            Dispose();
            Assert.IsTrue(_rented.Count > 0, "Expected a large packet buffer to be rented.");
            CollectionAssert.IsSubsetOf(_rented.ToArray(), _returned.ToArray());
        }
    }
}
