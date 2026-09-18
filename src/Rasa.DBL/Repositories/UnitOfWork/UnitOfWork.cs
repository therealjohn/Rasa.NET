using System;
using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        public IDbContextTransaction BeginTransaction()
        {
            return _dbContext.Database.BeginTransaction();
        }

        public void ExecuteTransaction(System.Action operation)
        {
            using var transaction = _dbContext.Database.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                try
                {
                    operation();
                }
                catch (InvalidOperationException error) when (IsTransientUpdateWrapper(error))
                {
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    throw;
                }
                catch (InvalidOperationException error) when (IsProviderConnectionLoss(error))
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
                catch (InvalidOperationException error) when (
                    transaction.GetDbTransaction().Connection?.State != ConnectionState.Open)
                {
                    throw new DbUpdateException("Transaction connection was lost before commit.", error);
                }
            }
            catch
            {
                try
                {
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

            bool IsProviderConnectionLoss(InvalidOperationException error)
            {
                var connection = _dbContext.Database.GetDbConnection();
                var declaringType = error.TargetSite?.DeclaringType;
                return connection.State != ConnectionState.Open &&
                    declaringType?.Assembly == connection.GetType().Assembly &&
                    (typeof(DbCommand).IsAssignableFrom(declaringType) ||
                        typeof(DbConnection).IsAssignableFrom(declaringType) ||
                        typeof(DbTransaction).IsAssignableFrom(declaringType));
            }

            bool IsTransientUpdateWrapper(InvalidOperationException error)
            {
                if (error.InnerException is not DbUpdateException { InnerException: DbException databaseError } ||
                    !databaseError.IsTransient)
                    return false;

                var providerConnectionType = _dbContext.Database.GetDbConnection().GetType();
                return databaseError.GetType().Assembly == providerConnectionType.Assembly;
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