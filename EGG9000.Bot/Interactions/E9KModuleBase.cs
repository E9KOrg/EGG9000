using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public abstract class E9KModuleBase(IDbContextFactory<ApplicationDbContext> dbFactory) : InteractionModuleBase<SocketInteractionContext> {
        protected ApplicationDbContext Db { get; private set; }

        public override async Task BeforeExecuteAsync(ICommandInfo command) {
            Db = await dbFactory.CreateDbContextAsync();
        }

        public override async Task AfterExecuteAsync(ICommandInfo command) {
            if(Db is not null) await Db.DisposeAsync();
        }
    }
}
