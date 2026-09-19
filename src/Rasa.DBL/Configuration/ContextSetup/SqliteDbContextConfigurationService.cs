using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rasa.Configuration.ContextSetup
{
    using ConnectionStrings;

    public class SqliteDbContextConfigurationService : IDbContextConfigurationService
    {
        private readonly IConnectionStringFactory _connectionStringFactory;

        public SqliteDbContextConfigurationService(IConnectionStringFactory connectionStringFactory)
        {
            _connectionStringFactory = connectionStringFactory;
        }

        public void Configure(DbContextOptionsBuilder dbContextOptionsBuilder, DatabaseConnectionConfiguration configuration)
        {
            var connectionString = _connectionStringFactory.Create(configuration);
            dbContextOptionsBuilder.UseSqlite(connectionString);
            dbContextOptionsBuilder.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }
    }
}