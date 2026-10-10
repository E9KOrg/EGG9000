using Discord.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace EGG9000.Bot.Interactions {
    public abstract class E9KModuleBase(IDbContextFactory<ApplicationDbContext> dbFactory) : InteractionModuleBase<E9KInteractionContext> {
        protected ApplicationDbContext Db { get; private set; }

        protected Coop CoopChannel => Context.CoopChannel;
        protected GuildContract ContractChannel => Context.ContractChannel;

        public async override Task BeforeExecuteAsync(ICommandInfo command) {
            Db = await dbFactory.CreateDbContextAsync();
        }

        public async override Task AfterExecuteAsync(ICommandInfo command) {
            if(Db is not null) await Db.DisposeAsync();
        }
    }
}
