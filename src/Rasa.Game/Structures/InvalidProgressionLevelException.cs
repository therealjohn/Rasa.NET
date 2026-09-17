using System;

namespace Rasa.Structures
{
    public sealed class InvalidProgressionLevelException : InvalidOperationException
    {
        public InvalidProgressionLevelException(uint characterId, byte level, int maximumLevel)
            : base($"Character {characterId} has invalid persisted level {level}; expected 1..{maximumLevel}.")
        {
        }
    }
}
