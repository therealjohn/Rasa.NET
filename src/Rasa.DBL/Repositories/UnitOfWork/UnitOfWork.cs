using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Runtime.ExceptionServices;

namespace Rasa.Repositories.UnitOfWork
{
    public abstract class UnitOfWork
    {
        private readonly DbContext _dbContext;

        protected UnitOfWork(DbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public void Complete()
        {
            if (_dbContext.ChangeTracker.HasChanges())
            {
                _dbContext.SaveChanges();
            }
        }

        public void ExecuteTransaction(System.Action operation)
        {
            using var transaction = _dbContext.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                try
                {
                    operation();
                }
                catch (System.InvalidOperationException error) when (IsTransientUpdateWrapper(error))
                {
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    throw;
                }
                catch (System.InvalidOperationException error) when (IsProviderConnectionLoss(error))
                {
                    throw new DbUpdateException("Transaction connection was lost before commit.", error);
                }
                RequireOpenTransaction();
                Complete();
                RequireOpenTransaction();
                try
                {
                    transaction.Commit();
                }
                catch (System.InvalidOperationException error) when (
                    transaction.GetDbTransaction().Connection?.State != ConnectionState.Open)
                {
                    throw new DbUpdateException("Transaction connection was lost before commit.", error);
                }
            }
            catch
            {
                try
                {
                    // A closed connection has already ended the transaction.
                    if (transaction.GetDbTransaction().Connection?.State == ConnectionState.Open)
                        transaction.Rollback();
                }
                catch (System.Exception rollbackError)
                {
                    Logger.WriteLog(LogType.Error, $"Transaction rollback failed: {rollbackError}");
                }
                finally
                {
                    _dbContext.ChangeTracker.Clear();
                }
                throw;
            }

            void RequireOpenTransaction()
            {
                if (transaction.GetDbTransaction().Connection?.State != ConnectionState.Open)
                    throw new DbUpdateException("Transaction connection was lost before commit.");
            }

            bool IsProviderConnectionLoss(System.InvalidOperationException error)
            {
                var connection = _dbContext.Database.GetDbConnection();
                var declaringType = error.TargetSite?.DeclaringType;
                return connection.State != ConnectionState.Open &&
                    declaringType?.Assembly == connection.GetType().Assembly &&
                    (typeof(System.Data.Common.DbCommand).IsAssignableFrom(declaringType) ||
                        typeof(System.Data.Common.DbConnection).IsAssignableFrom(declaringType) ||
                        typeof(System.Data.Common.DbTransaction).IsAssignableFrom(declaringType));
            }

            static bool IsTransientUpdateWrapper(System.InvalidOperationException error) =>
                error.InnerException is DbUpdateException { InnerException: DbException databaseError } &&
                databaseError.IsTransient;
        }

        public void Reject()
        {
            if (_dbContext.ChangeTracker.HasChanges())
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        public void Dispose()
        {
            Reject();
            _dbContext.Dispose();
        }
    }
}