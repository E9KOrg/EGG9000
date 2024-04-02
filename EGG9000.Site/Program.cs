using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;


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

        public static IHostBuilder CreateHostBuilder(string[] args) {
            return Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging => {
                logging.ClearProviders();
                logging.AddConsole();
            })
                .ConfigureWebHostDefaults(webBuilder => {
                    webBuilder.UseStartup<Startup>().UseUrls("http://0.0.0.0:5013");
                });
        }
    }
}
