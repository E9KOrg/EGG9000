using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe {
    public sealed class ProbeOptions {
        public const string DefaultOutDir = "dist/convertprobe";
        private const string ReadOnlyStartup = "-c default_transaction_read_only=on";

        public string Verb { get; private set; }
        public string Conn { get; private set; }
        public string OutDir { get; set; } = DefaultOutDir;
        public int? Limit { get; set; }
        public string Csv { get; set; }
        public string Volatile { get; private set; }
        public bool Help { get; private set; }
        public bool All { get; private set; }
        public List<string> Positional { get; } = [];

        public static ProbeOptions Parse(string[] args, out string error) {
            error = null;
            var options = new ProbeOptions();
            for(var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if(arg is "--help" or "-h" or "help") {
                    options.Help = true;
                    continue;
                }
                if(arg == "--all") {
                    options.All = true;
                    continue;
                }
                if(!arg.StartsWith("--", StringComparison.Ordinal)) {
                    if(options.Verb is null) options.Verb = arg;
                    else options.Positional.Add(arg);
                    continue;
                }
                if(i + 1 >= args.Length) {
                    error = $"Option {arg} requires a value.";
                    return options;
                }
                var value = args[++i];
                switch(arg) {
                    case "--conn":
                        options.Conn = value;
                        break;
                    case "--out":
                        options.OutDir = value;
                        break;
                    case "--limit":
                        if(!int.TryParse(value, out var limit) || limit <= 0) {
                            error = "--limit must be a positive integer.";
                            return options;
                        }
                        options.Limit = limit;
                        break;
                    case "--csv":
                        options.Csv = value;
                        break;
                    case "--volatile":
                        options.Volatile = value;
                        break;
                    default:
                        error = $"Unknown option {arg}.";
                        return options;
                }
            }
            return options;
        }

        public string EnsureOutDir() {
            Directory.CreateDirectory(OutDir);
            return Path.GetFullPath(OutDir);
        }

        public string ReadOnlyConnectionString() {
            var builder = new NpgsqlConnectionStringBuilder(Conn);
            builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? ReadOnlyStartup : builder.Options + " " + ReadOnlyStartup;
            return builder.ConnectionString;
        }

        public async Task<NpgsqlConnection> OpenConnectionAsync() {
            var connection = new NpgsqlConnection(ReadOnlyConnectionString());
            await connection.OpenAsync();
            return connection;
        }

        public ApplicationDbContext CreateContext() {
            var contextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ReadOnlyConnectionString(), x => x.CommandTimeout(600))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;
            return new ApplicationDbContext(contextOptions);
        }
    }
}
