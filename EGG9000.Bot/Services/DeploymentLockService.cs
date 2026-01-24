//using System;
//using System.Data;
//using System.Data.Common;
//using System.Threading;
//using System.Threading.Tasks;
//using EGG9000.Common.Database;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Configuration;

//namespace EGG9000.Bot.Services
//{
//    /// <summary>
//    /// Helpers to perform atomic check+update operations on the deployment row.
//    /// Implements two approaches:
//    ///  - TrySetActiveColorWithAppLockAsync: uses sp_getapplock (application-level lock)
//    ///  - TrySetActiveColorWithPessimisticLockAsync: uses SELECT ... WITH (UPDLOCK, ROWLOCK)
//    /// </summary>
//    public class DeploymentLockService
//    {
//        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
//        private readonly ILogger<DeploymentLockService> _logger;
//        private readonly string _resourceName;

//        public DeploymentLockService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<DeploymentLockService> logger, IConfiguration config = null)
//        {
//            _dbFactory = dbFactory;
//            _logger = logger;
//            // Optional: resource name can be configured
//            _resourceName = config?.GetValue<string>("DeploymentLockResource") ?? "EGG9000_DeploymentLock";
//        }

//        /// <summary>
//        /// Acquire an application-level lock (sp_getapplock) then check and update the ActiveColor row atomically.
//        /// Uses LockOwner = 'Transaction' so the lock is released on transaction commit/rollback.
//        /// Returns true if update succeeded, false if the expected value didn't match or lock failed.
//        /// </summary>
//        public async Task<bool> TrySetActiveColorWithAppLockAsync(string expectedCurrent, string newColor, CancellationToken ct = default)
//        {
//            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
//            var conn = ctx.Database.GetDbConnection();
//            await conn.OpenAsync(ct);

//            // Begin a transaction so we can obtain a transaction-scoped applock
//            await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

//            try
//            {
//                // Acquire applock. Use a return parameter to read the result code.
//                await using (var cmd = conn.CreateCommand())
//                {
//                    cmd.Transaction = tx.GetDbTransaction();
//                    cmd.CommandText = "EXEC @rc = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;";
//                    var pRes = cmd.CreateParameter(); pRes.ParameterName = "@res"; pRes.Value = _resourceName; cmd.Parameters.Add(pRes);
//                    var pTimeout = cmd.CreateParameter(); pTimeout.ParameterName = "@timeout"; pTimeout.Value = 10000; cmd.Parameters.Add(pTimeout);

//                    var pRet = cmd.CreateParameter(); pRet.ParameterName = "@rc"; pRet.DbType = DbType.Int32; pRet.Direction = ParameterDirection.ReturnValue;
//                    cmd.Parameters.Add(pRet);

//                    await cmd.ExecuteNonQueryAsync(ct);
//                    var rc = Convert.ToInt32(pRet.Value);
//                    // rc < 0 => lock failed
//                    if (rc < 0)
//                    {
//                        _logger.LogWarning("Failed to acquire applock (rc={rc})", rc);
//                        await tx.RollbackAsync(ct);
//                        return false;
//                    }

//                    // Read current value
//                    cmd.Parameters.Clear();
//                    cmd.CommandText = "SELECT ActiveColor FROM dbo.EGG9000_DeploymentStatus WHERE Id = 1";
//                    var obj = await cmd.ExecuteScalarAsync(ct);
//                    var current = obj?.ToString();

//                    if (!string.Equals(current, expectedCurrent, StringComparison.OrdinalIgnoreCase))
//                    {
//                        _logger.LogInformation("ActiveColor mismatch (db={db}, expected={exp})", current, expectedCurrent);
//                        await tx.RollbackAsync(ct);
//                        return false;
//                    }

//                    // Update to newColor
//                    cmd.CommandText = "UPDATE dbo.EGG9000_DeploymentStatus SET ActiveColor = @newColor, UpdatedAt = SYSUTCDATETIME() WHERE Id = 1";
//                    var pNew = cmd.CreateParameter(); pNew.ParameterName = "@newColor"; pNew.Value = newColor; cmd.Parameters.Add(pNew);
//                    var rows = await cmd.ExecuteNonQueryAsync(ct);

//                    await tx.CommitAsync(ct);
//                    _logger.LogInformation("ActiveColor changed from {old} to {new} (rows={rows})", current, newColor, rows);
//                    return rows > 0;
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error in TrySetActiveColorWithAppLockAsync");
//                try { await tx.RollbackAsync(ct); } catch { }
//                throw;
//            }
//            finally
//            {
//                await conn.CloseAsync();
//            }
//        }

//        /// <summary>
//        /// Pessimistic row lock using UPDLOCK+ROWLOCK in the SELECT. Other writers will block until commit.
//        /// Runs in a transaction.
//        /// Returns true if the update was applied; false if expectedCurrent didn't match.
//        /// </summary>
//        public async Task<bool> TrySetActiveColorWithPessimisticLockAsync(string expectedCurrent, string newColor, CancellationToken ct = default)
//        {
//            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
//            var conn = ctx.Database.GetDbConnection();
//            await conn.OpenAsync(ct);

//            await using var tx = await ctx.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
//            try
//            {
//                await using (var cmd = conn.CreateCommand())
//                {
//                    cmd.Transaction = tx.GetDbTransaction();
//                    // Acquire update lock on the row
//                    cmd.CommandText = "SELECT ActiveColor FROM dbo.EGG9000_DeploymentStatus WITH (ROWLOCK, UPDLOCK) WHERE Id = 1";
//                    var obj = await cmd.ExecuteScalarAsync(ct);
//                    var current = obj?.ToString();

//                    if (!string.Equals(current, expectedCurrent, StringComparison.OrdinalIgnoreCase))
//                    {
//                        await tx.RollbackAsync(ct);
//                        _logger.LogInformation("ActiveColor mismatch (db={db}, expected={exp})", current, expectedCurrent);
//                        return false;
//                    }

//                    cmd.CommandText = "UPDATE dbo.EGG9000_DeploymentStatus SET ActiveColor = @newColor, UpdatedAt = SYSUTCDATETIME() WHERE Id = 1";
//                    var p = cmd.CreateParameter(); p.ParameterName = "@newColor"; p.Value = newColor; cmd.Parameters.Add(p);
//                    var rows = await cmd.ExecuteNonQueryAsync(ct);

//                    await tx.CommitAsync(ct);
//                    _logger.LogInformation("ActiveColor changed from {old} to {new} (rows={rows})", current, newColor, rows);
//                    return rows > 0;
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error in TrySetActiveColorWithPessimisticLockAsync");
//                try { await tx.RollbackAsync(ct); } catch { }
//                throw;
//            }
//            finally
//            {
//                await conn.CloseAsync();
//            }
//        }
//    }
//}