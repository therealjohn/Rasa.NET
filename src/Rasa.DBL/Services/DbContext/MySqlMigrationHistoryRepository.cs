using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Migrations.Internal;

namespace Rasa.Services.DbContext
{
    internal sealed class MySqlMigrationHistoryRepository : MySqlHistoryRepository
    {
        public MySqlMigrationHistoryRepository(HistoryRepositoryDependencies dependencies)
            : base(dependencies)
        {
        }

        protected override string GetDatabaseLockName(string databaseName)
        {
            var name = base.GetDatabaseLockName(databaseName);
            if (name.Length <= 64)
                return name;

            // Pomelo uses this hook for both acquire and release. MySQL lock names
            // are case-insensitive; retain that coordination when bounding long names.
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant())));
        }
    }
}
