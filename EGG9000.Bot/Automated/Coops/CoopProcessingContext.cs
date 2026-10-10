using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using Ei;
using System;
using System.Collections.Generic;
using System.Threading;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        private sealed class CoopProcessingContext {
            public Guid CoopId { get; }
            public SocketGuild Guild { get; }
            public SocketGuild ParentGuild { get; }
            public List<UserWithBackup> Users { get; }
            public Guild DbGuild { get; }
            public TimingsFactory Timings { get; }
            public CancellationToken Cancellation { get; }
            public ApplicationDbContext Db { get; set; }

            public Coop Coop { get; set; }
            public string CoopName { get; set; }
            public IReadOnlyCollection<SocketApplicationCommand> SlashCommands { get; set; }

            public IThreadChannel CoopThread { get; set; }
            public List<ulong> CoopDiscordUsers { get; set; }

            public StatusResponse StatusResponse { get; set; }
            public ContractCoopStatusResponse Status { get; set; }

            public List<DBCustomEgg> CustomEggs { get; set; }
            public CoopDetails CoopDetails { get; set; }

            public List<UserWithStatus> UsersWithStatus { get; set; }
            public List<UserFarmDetails> UsersNotJoined { get; set; }
            public List<ulong> UsersNeedingChannelPermissions { get; set; } = [];
            public IEnumerable<ulong> CurrentUserDiscordIds { get; set; }

            public int League { get; set; }
            public double TargetAmount { get; set; }
            public double TotalRate { get; set; }
            public double AmountWithOffline { get; set; }
            public double RemainingAmount { get; set; }
            public TimeSpan TimeRemaining { get; set; }
            public IEnumerable<UserWithStatus> WaitingOn { get; set; }

            public bool FinalChannelUpdate { get; set; }
            public bool MissingFromServer { get; set; }
            public int MissingCount { get; set; }

            public string LastMessage { get; set; } = "";
            public List<string> Msgs { get; set; }

            public string Emojis { get; set; } = "";
            public Color EmbedColor { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }

            public CoopProcessingContext(Guid coopId, SocketGuild guild, SocketGuild parentGuild, List<UserWithBackup> users, Guild dbGuild, TimingsFactory timings, CancellationToken cancellationToken) {
                CoopId = coopId;
                Guild = guild;
                ParentGuild = parentGuild;
                Users = users;
                DbGuild = dbGuild;
                Timings = timings;
                Cancellation = cancellationToken;
            }
        }
    }
}
