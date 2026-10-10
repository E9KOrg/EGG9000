using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public abstract class E9KModuleBase(IDbContextFactory<ApplicationDbContext> dbFactory) : InteractionModuleBase<E9KInteractionContext> {
        protected ApplicationDbContext Db { get; private set; }

        protected Coop CoopChannel => Context.CoopChannel;
        protected GuildContract ContractChannel => Context.ContractChannel;

        public async override Task BeforeExecuteAsync(ICommandInfo command) {
            if(Context.Interaction is SocketMessageComponent component && !HasNoAutoAck(command))
                await component.DeferDisablingAsync();
            Db = await dbFactory.CreateDbContextAsync();
        }

        public async override Task AfterExecuteAsync(ICommandInfo command) {
            if(Db is not null) await Db.DisposeAsync();
        }

        private static bool HasNoAutoAck(ICommandInfo command) =>
            command.Attributes.OfType<NoAutoAckAttribute>().Any() || command.Module.Attributes.OfType<NoAutoAckAttribute>().Any();
    }
}
