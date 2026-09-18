using System;

namespace Rasa.Structures
{
    internal sealed class GameplayRejectionException : InvalidOperationException
    {
        internal GameplayRejectionException(string message) : base(message)
        {
        }
    }
}
