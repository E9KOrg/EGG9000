using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace EGG9000.Site {
    public class Program {
        public static void Main(string[] args) {
#if DEBUG
            Console.WriteLine(Process.GetCurrentProcess().Id.ToString());
#endif

            CreateHostBuilder(args)
                .UseDefaultServiceProvider(options => options.ValidateScopes = false)
                .Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)

                .ConfigureWebHostDefaults(webBuilder => {
                    webBuilder.UseStartup<Startup>().UseUrls("http://0.0.0.0:5013");
                });
    }
}
