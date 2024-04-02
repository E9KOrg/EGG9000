using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EGG9000.Common.Database {
    public class ApplicationDbFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext(string[] args) {
            var Configuration = new ConfigurationBuilder()
                .AddUserSecrets<ApplicationDbFactory>()
                .Build();

            return new ApplicationDbContext(Configuration);
        }
    }
}
