using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public abstract class E9KModuleBase : InteractionModuleBase<SocketInteractionContext> {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        protected ApplicationDbContext Db { get; private set; }

        protected E9KModuleBase(IDbContextFactory<ApplicationDbContext> dbFactory) {
            _dbFactory = dbFactory;
        }

        public override async Task BeforeExecuteAsync(ICommandInfo command) {
            Db = await _dbFactory.CreateDbContextAsync();
        }

        public override async Task AfterExecuteAsync(ICommandInfo command) {
            if(Db is not null) await Db.DisposeAsync();
        }
    }
}
