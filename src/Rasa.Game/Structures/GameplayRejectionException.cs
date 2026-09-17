using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Rasa.Structures
{
    using Repositories;

    internal sealed class GameplayRejectionException : Exception
    {
        internal GameplayRejectionException(string message) : base(message) { }

        internal static bool IsExpected(Exception error) =>
            error is GameplayRejectionException or EntityNotFoundException or
                DbException or DbUpdateException or OverflowException or NotSupportedException;
    }
}
