using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Common.Services
{
    /// <summary>
    /// Configuration options for ActiveMonitorHostedService
    /// </summary>
    public class ActiveMonitorOptions
    {
        /// <summary>
        /// Service type identifier (bot or site)
        /// </summary>
        public string ServiceType { get; set; } = "bot";
        
        /// <summary>
        /// Configuration key to read the color from (e.g., BOT_COLOR or SITE_COLOR)
        /// </summary>
        public string ColorConfigKey { get; set; } = "BOT_COLOR";
        
        /// <summary>
        /// Poll interval in seconds
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 10;
    }

    /// <summary>
    /// Polls a deployment table in the application's SQL Server database.
    /// When the stored ActiveColor does not match this instance's color the host will be stopped.
    /// Supports separate tracking for bot and site services.
    /// </summary>
    public class ActiveMonitorHostedService : BackgroundService
    {
        private readonly IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> _dbFactory;
        private readonly ILogger<ActiveMonitorHostedService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IConfiguration _configuration;
        private readonly string _instanceColor;
        private readonly string _serviceType;
        private readonly TimeSpan _pollInterval;
        public bool Ready { get; set; } = false;

        public ActiveMonitorHostedService(
            IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> dbFactory,
            IConfiguration configuration,
            IHostApplicationLifetime lifetime,
            ILogger<ActiveMonitorHostedService> logger,
            Microsoft.Extensions.Options.IOptions<ActiveMonitorOptions> options)
        {
            _dbFactory = dbFactory;
            _logger = logger;
            _lifetime = lifetime;
            _configuration = configuration;
            
            var opts = options.Value;
            _serviceType = opts.ServiceType.ToLowerInvariant();
            _instanceColor = configuration.GetValue<string>(opts.ColorConfigKey)?.ToLowerInvariant() ?? "blue";
            _pollInterval = TimeSpan.FromSeconds(opts.PollIntervalSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ActiveMonitorHostedService starting. Service: {service}, Color: {color}", _serviceType, _instanceColor);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if(Ready) {

                        await using var ctx = await _dbFactory.CreateDbContextAsync(stoppingToken);
                        var conn = ctx.Database.GetDbConnection();
                        await conn.OpenAsync(stoppingToken);

                        await EnsureTableExistsAsync(conn, stoppingToken);

                        var activeColor = await ReadActiveColorAsync(conn, _serviceType, stoppingToken);

                        if(activeColor is null) {
                            // No value present yet — insert our color as the initial value (optional policy).
                            await UpsertActiveColorAsync(conn, _serviceType, _instanceColor, stoppingToken);
                            _logger.LogInformation("No active color found in DB for {service}. Claimed active color = {color}", _serviceType, _instanceColor);
                        } else {
                            if(!string.Equals(activeColor, _instanceColor, StringComparison.OrdinalIgnoreCase)) {
                                _logger.LogInformation("Instance color {instance} does not match ActiveColor {active} in DB for {service}. Stopping host.", _instanceColor, activeColor, _serviceType);
                                // Graceful shutdown
                                try { _lifetime.StopApplication(); } catch { /* swallow */ }
                                return;
                            } else {
                                // We're the active color — update heartbeat timestamp
                                await UpdateHeartbeatAsync(conn, _serviceType, stoppingToken);
                            }
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

        public async Task SetActiveColorAsync() 
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var conn = ctx.Database.GetDbConnection();
            await conn.OpenAsync();
            await EnsureTableExistsAsync(conn, CancellationToken.None);
            await UpsertActiveColorAsync(conn, _serviceType, _instanceColor, CancellationToken.None);
            _logger.LogInformation("Set active color in DB to {color} for {service}", _instanceColor, _serviceType);

            Ready = true;
        }

        private static async Task EnsureTableExistsAsync(DbConnection conn, CancellationToken ct)
        {
            // Creates a tiny table if not present. Uses SQL Server conditional.
            // ServiceType column allows tracking bot and site separately
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.EGG9000_DeploymentStatus','U') IS NULL
BEGIN
    CREATE TABLE dbo.EGG9000_DeploymentStatus (
        ServiceType NVARCHAR(50) NOT NULL PRIMARY KEY,
        ActiveColor NVARCHAR(50) NOT NULL,
        UpdatedAt DATETIME2 NOT NULL
    );

    -- Insert default values for bot and site
    INSERT INTO dbo.EGG9000_DeploymentStatus (ServiceType, ActiveColor, UpdatedAt) VALUES ('bot', 'blue', SYSUTCDATETIME());
    INSERT INTO dbo.EGG9000_DeploymentStatus (ServiceType, ActiveColor, UpdatedAt) VALUES ('site', 'blue', SYSUTCDATETIME());
END";
            await cmd.ExecuteNonQueryAsync(ct);
        }

#nullable enable
        public static async Task<string?> ReadActiveColorAsync(DbConnection conn, string serviceType, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ActiveColor FROM dbo.EGG9000_DeploymentStatus WHERE ServiceType = @serviceType";
            var p = cmd.CreateParameter();
            p.ParameterName = "@serviceType";
            p.Value = serviceType;
            cmd.Parameters.Add(p);
            
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is DBNull ? null : result?.ToString();
        }
#nullable disable

        public static async Task UpsertActiveColorAsync(DbConnection conn, string serviceType, string color, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
MERGE dbo.EGG9000_DeploymentStatus AS target
USING (VALUES (@serviceType, @color, SYSUTCDATETIME())) AS source(ServiceType, ActiveColor, UpdatedAt)
ON target.ServiceType = source.ServiceType
WHEN MATCHED THEN
    UPDATE SET ActiveColor = source.ActiveColor, UpdatedAt = source.UpdatedAt
WHEN NOT MATCHED THEN
    INSERT (ServiceType, ActiveColor, UpdatedAt) VALUES (source.ServiceType, source.ActiveColor, source.UpdatedAt);";
            
            var pService = cmd.CreateParameter();
            pService.ParameterName = "@serviceType";
            pService.Value = serviceType;
            cmd.Parameters.Add(pService);
            
            var pColor = cmd.CreateParameter();
            pColor.ParameterName = "@color";
            pColor.Value = color;
            cmd.Parameters.Add(pColor);
            
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task UpdateHeartbeatAsync(DbConnection conn, string serviceType, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.EGG9000_DeploymentStatus SET UpdatedAt = SYSUTCDATETIME() WHERE ServiceType = @serviceType";
            var p = cmd.CreateParameter();
            p.ParameterName = "@serviceType";
            p.Value = serviceType;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}