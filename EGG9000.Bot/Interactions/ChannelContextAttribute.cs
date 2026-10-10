using Discord;
using Discord.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public static class ChannelContextResolution {
        public static bool ShouldReject(bool coopRequested, bool contractRequested, bool coopResolved, bool contractResolved) {
            if(coopRequested && contractRequested)
                return !coopResolved && !contractResolved;
            bool coopSatisfied = !coopRequested || coopResolved;
            bool contractSatisfied = !contractRequested || contractResolved;
            return !(coopSatisfied && contractSatisfied);
        }

        public static string RejectMessage(bool coopRequested, bool contractRequested) {
            if(coopRequested && contractRequested)
                return "This command can only be used in a co-op or contract channel.";
            if(contractRequested)
                return "This command can only be used in a contract channel.";
            return "This command can only be used in a co-op channel.";
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ChannelContextAttribute : PreconditionAttribute {
        public bool Coop { get; set; }
        public bool CoopWithContract { get; set; }
        public bool CoopWithUsers { get; set; }
        public bool Contract { get; set; }
        public bool ContractWithContract { get; set; }
        public bool ContractGuildScoped { get; set; }

        public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services) {
            if(context is not E9KInteractionContext ctx)
                return PreconditionResult.FromError("ChannelContext requires E9KInteractionContext.");

            bool coopRequested = Coop || CoopWithContract || CoopWithUsers;
            bool contractRequested = Contract || ContractWithContract || ContractGuildScoped;
            if(!coopRequested && !contractRequested)
                coopRequested = true;

            var factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            await using var db = await factory.CreateDbContextAsync();

            ulong channelId = context.Channel.Id;

            if(coopRequested) {
                IQueryable<Coop> q = db.Coops;
                if(CoopWithContract) q = q.Include(x => x.Contract);
                if(CoopWithUsers) q = q.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User);
                ctx.CoopChannel = await q.FirstOrDefaultAsync(x => x.ThreadID == channelId);
            }

            if(contractRequested) {
                IQueryable<GuildContract> q = db.GuildContracts;
                if(ContractWithContract) q = q.Include(x => x.Contract);
                if(ContractGuildScoped && context.Guild is not null)
                    q = q.Where(x => x.GuildID == context.Guild.Id);
                ctx.ContractChannel = await q.FirstOrDefaultAsync(x => x.DiscordChannelId == channelId);
            }

            if(ChannelContextResolution.ShouldReject(coopRequested, contractRequested, ctx.CoopChannel is not null, ctx.ContractChannel is not null))
                return PreconditionResult.FromError(ChannelContextResolution.RejectMessage(coopRequested, contractRequested));

            return PreconditionResult.FromSuccess();
        }
    }
}
