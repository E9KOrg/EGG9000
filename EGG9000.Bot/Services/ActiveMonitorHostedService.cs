using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Bot.Services
{
    /// <summary>
    /// Polls a small deployment table in the application's SQL Server database.
    /// When the stored ActiveColor does not match this instance's BOT_COLOR the host will be stopped.
    /// </summary>
    internal class ActiveMonitorHostedService : BackgroundService
    {
        private readonly IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> _dbFactory;
        private readonly ILogger<ActiveMonitorHostedService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly string _instanceColor;
        private readonly TimeSpan _pollInterval;

        public ActiveMonitorHostedService(
            IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> dbFactory,
            IConfiguration configuration,
            IHostApplicationLifetime lifetime,
            ILogger<ActiveMonitorHostedService> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _lifetime = lifetime;
            _instanceColor = configuration.GetValue<string>("BOT_COLOR")?.ToLowerInvariant() ?? "blue";
            _pollInterval = configuration.GetValue("ACTIVE_MONITOR_POLL_SECONDS", 10) is int s ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(10);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ActiveMonitorHostedService starting. Instance color: {color}", _instanceColor);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var ctx = await _dbFactory.CreateDbContextAsync(stoppingToken);
                    var conn = ctx.Database.GetDbConnection();
                    await conn.OpenAsync(stoppingToken);

                    await EnsureTableExistsAsync(conn, stoppingToken);

                    var activeColor = await ReadActiveColorAsync(conn, stoppingToken);

                    if (activeColor is null)
                    {
                        // No value present yet — insert our color as the initial value (optional policy).
                        await UpsertActiveColorAsync(conn, _instanceColor, stoppingToken);
                        _logger.LogInformation("No active color found in DB. Claimed active color = {color}", _instanceColor);
                    }
                    else
                    {
                        if (!string.Equals(activeColor, _instanceColor, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Instance color {instance} does not match ActiveColor {active} in DB. Stopping host.", _instanceColor, activeColor);
                            // Graceful shutdown
                            try { _lifetime.StopApplication(); } catch { /* swallow */ }
                            return;
                        }
                        else
                        {
                            // We're the active color — update heartbeat timestamp
                            await UpdateHeartbeatAsync(conn, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // exiting
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ActiveMonitor encountered an error; will retry.");
                }

                try { await Task.Delay(_pollInterval, stoppingToken); } catch (TaskCanceledException) { break; }
            }

            _logger.LogInformation("ActiveMonitorHostedService stopping.");
        }

        private static async Task EnsureTableExistsAsync(DbConnection conn, CancellationToken ct)
        {
            // Creates a tiny table if not present. Uses SQL Server conditional.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.EGG9000_DeploymentStatus','U') IS NULL
BEGIN
    CREATE TABLE dbo.EGG9000_DeploymentStatus (
        Id INT PRIMARY KEY,
        ActiveColor NVARCHAR(50) NOT NULL,
        UpdatedAt DATETIME2 NOT NULL
    );

    INSERT INTO dbo.EGG9000_DeploymentStatus (Id, ActiveColor, UpdatedAt) VALUES (1, 'blue', SYSUTCDATETIME());
END";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<string?> ReadActiveColorAsync(DbConnection conn, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ActiveColor FROM dbo.EGG9000_DeploymentStatus WHERE Id = 1";
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is DBNull ? null : result?.ToString();
        }

        private static async Task UpsertActiveColorAsync(DbConnection conn, string color, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
MERGE dbo.EGG9000_DeploymentStatus AS target
USING (VALUES (1, @color, SYSUTCDATETIME())) AS source(Id, ActiveColor, UpdatedAt)
ON target.Id = source.Id
WHEN MATCHED THEN
    UPDATE SET ActiveColor = source.ActiveColor, UpdatedAt = source.UpdatedAt
WHEN NOT MATCHED THEN
    INSERT (Id, ActiveColor, UpdatedAt) VALUES (source.Id, source.ActiveColor, source.UpdatedAt);";
            var p = cmd.CreateParameter();
            p.ParameterName = "@color";
            p.Value = color;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task UpdateHeartbeatAsync(DbConnection conn, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.EGG9000_DeploymentStatus SET UpdatedAt = SYSUTCDATETIME() WHERE Id = 1";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}