using Bugsnag.AspNet.Core;

using Discord;
using Discord.WebSocket;

using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Mocks;
using EGG9000.Common.Services;
using EGG9000.Site.Data;
using EGG9000.Site.Services;

using MassTransit;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;


//using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog;
using NLog.Web;

using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;


//namespace EGG9000.Site {
//    public class Program {
//        public static void Main(string[] args) {
//#if DEBUG
//            Console.WriteLine(Process.GetCurrentProcess().Id.ToString());
//#endif

//            CreateHostBuilder(args)
//                .UseDefaultServiceProvider(options => options.ValidateScopes = false)
//                .Build().Run();
//        }

//        public static IHostBuilder CreateHostBuilder(string[] args) {
//            return Host.CreateDefaultBuilder(args)
//            .ConfigureLogging(logging => {
//                logging.ClearProviders();
//                logging.AddConsole();
//            })
//                .ConfigureWebHostDefaults(webBuilder => {
//                    webBuilder.UseStartup<Startup>().UseUrls("http://0.0.0.0:5013");
//                });
//        }
//    }
//}


var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
logger.Debug("init main");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5013");
builder.Logging.ClearProviders();
builder.Host.UseNLog();
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();
if(app.Environment.IsDevelopment()) {
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
} else {
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions {
    ServeUnknownFileTypes = true
});

app.UseRouting();
app.UseCors("SiteCorsPolicy");
app.UseResponseCaching();
app.UseAuthentication(); 
app.UseAuthorization();

app.MapStaticAssets();

//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}")
//    .WithStaticAssets();


app.MapControllerRoute(name: "invite",
        pattern: "invite",
        defaults: new { controller = "Home", action = "Invite" });
app.MapControllerRoute(
    name: "coop",
    pattern: "coop/{ContractId}/{CoopId}",
    defaults: new { controller = "Home", action = "Coop" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.Run();


void ConfigureServices(IServiceCollection services, IConfiguration Configuration) {
    services.AddDataProtection().PersistKeysToDbContext<ApplicationDbContext>().SetApplicationName("EGG9000");
    //_logger.LogInformation(Configuration.GetConnectionString("DefaultConnection"));
    //_logger.LogInformation(Configuration.GetChildren().Count().ToString());
    services.AddDbContext<ApplicationDbContext>(
        options => options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")),
        contextLifetime: ServiceLifetime.Scoped,
        optionsLifetime: ServiceLifetime.Singleton);


    services.AddDbContextFactory<ApplicationDbContext>(options => {
        options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"), x => x.MigrationsAssembly("EGG9000.Common"));
        options.EnableSensitiveDataLogging(true);
    });

    services.AddIdentity<IdentityUser, IdentityRole>(options => {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = false;
    })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders().AddClaimsPrincipalFactory<CustomClaimsPrincipleFactory>();

    services.ConfigureExternalCookie(options => {
        options.ExpireTimeSpan = TimeSpan.FromDays(15);
    });

    //services.ConfigureApplicationCookie(options => {
    //    options.LoginPath = $"/Identity/Account/Login";
    //    options.LogoutPath = $"/Identity/Account/Logout";
    //    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
    //    options.SlidingExpiration = true;
    //    options.ExpireTimeSpan = TimeSpan.FromDays(15);
    //    options.se
    //});

    //services.ConfigureExternalCookie((options) => ConfigureAuthorizationCookie(options, "egg9000CookieExternal"));

    services
                .ConfigureApplicationCookie((options) => ConfigureAuthorizationCookie(options, "egg9000Cookie"))
                .ConfigureExternalCookie((options) => ConfigureAuthorizationCookie(options, "egg9000CookieExternal"));


    //_logger.LogInformation(Configuration.GetConnectionString("ClientId"));
    //_logger.LogInformation(Configuration.GetConnectionString("ClientSecret"));

    services.AddAuthentication(options => {
    }).AddDiscord(options => {
        options.ClientId = Configuration.GetConnectionString("ClientId");
        options.ClientSecret = Configuration.GetConnectionString("ClientSecret");
        options.Events = new Microsoft.AspNetCore.Authentication.OAuth.OAuthEvents {
            OnTicketReceived = context => {
                Console.WriteLine("est");
                return Task.FromResult(0);
            }
        };
        options.SaveTokens = true;
    }).AddCookie(options => {
        options.ExpireTimeSpan = TimeSpan.FromDays(45);
        options.Cookie.Name = "egg9000Cookie";
        options.Cookie.Expiration = TimeSpan.FromDays(45);
        options.SlidingExpiration = true;
        options.LoginPath = $"/Identity/Account/Login";
        options.LogoutPath = $"/Identity/Account/Logout";
        options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
    });





    services.AddResponseCaching();
    services.AddControllersWithViews().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
    services.AddRazorPages();
    services.AddTransient<IEmailSender, EmailSenderBlank>();
    services.Configure<APILinkOptions>(x => x.AsyncLoadCache = true);
    services.AddSingleton<APILink>();
    services.AddHostedService<APILink>(provider => provider.GetService<APILink>());
    services.AddHostedService<NewCoopChecker>();
    services.AddSingleton<DatabaseCache>();

    services.Configure<ForwardedHeadersOptions>(options => {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 24));
    });


    var config = new DiscordSocketConfig() {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
    };
    var client = new DiscordSocketClient(config);
    client.LoginAsync(Discord.TokenType.Bot, Configuration.GetConnectionString("Token")).Wait();
    client.StartAsync().Wait();
    services.AddSingleton(client);


#if RELEASE
    services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
    services.AddResponseCompression(options => {
        options.Providers.Add<GzipCompressionProvider>();
        options.EnableForHttps = true;
    });
    services.AddBugsnag(configuration => {
        configuration.ApiKey = Configuration.GetConnectionString("BugSnagApiKey");
    });
    services.AddOptions<RabbitMqTransportOptions>().Configure(options => {
        var host = Configuration.GetConnectionString("RabbitMQServer")?.Split("|");
        if(host.Length > 1) {
            options.Host = host[0];
            options.User = host[1];
            options.Pass = host[2];
        }
    });

    services.AddMassTransit(x => {
        x.AddConsumer<ExpireCacheConsumer>();
        var host = Configuration.GetConnectionString("RabbitMQServer");
        if(string.IsNullOrEmpty(host)) {
            x.UsingInMemory((context, cfg) => {
                cfg.ConfigureEndpoints(context);
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            });
        } else {
            x.UsingRabbitMq((context, cfg) => {
                cfg.ConfigureEndpoints(context);
            });
        }
    });
#else
            services.AddSingleton<IPublishEndpoint>(new PublishEndpointMock());
            services.AddBugsnag();
#endif

    services.AddDatabaseDeveloperPageExceptionFilter();
}

void ConfigureAuthorizationCookie(CookieAuthenticationOptions options, string cookieName) {
    options.Cookie.Name = cookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(15);
    options.SlidingExpiration = true;
    options.Cookie.MaxAge = TimeSpan.FromDays(15);
    options.LoginPath = $"/Identity/Account/Login";
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
}