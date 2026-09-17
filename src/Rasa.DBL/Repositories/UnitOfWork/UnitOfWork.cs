using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

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
                operation();
                RequireOpenTransaction();
                Complete();
                RequireOpenTransaction();
                transaction.Commit();
            }
            catch (System.InvalidOperationException error) when (
                transaction.GetDbTransaction().Connection?.State != ConnectionState.Open &&
                error.Message.Contains("transaction object is not associated with the same connection object",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                _dbContext.ChangeTracker.Clear();
                throw new DbUpdateException("Transaction connection was lost before commit.", error);
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