using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(EGG9000.Site.Areas.Identity.IdentityHostingStartup))]
namespace EGG9000.Site.Areas.Identity {
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
            });
        }
    }
}