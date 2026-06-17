using Bugsnag.AspNet.Core;

using Discord;
using Discord.WebSocket;

using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Mocks;
using EGG9000.Common.Services;
using EGG9000.Site.Data;
using EGG9000.Site.Services;

using MassTransit;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;


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



var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
NLog.GlobalDiagnosticsContext.Set("CustomMachineName", $"{Environment.MachineName}");
NLog.GlobalDiagnosticsContext.Set("CustomAppName", $"EGG9000.Site");
logger.Debug("init main");

var builder = WebApplication.CreateBuilder(args);
SecretsHelper.Initialize(builder.Configuration);
builder.WebHost.UseUrls("http://0.0.0.0:5013");
builder.Logging.ClearProviders();
builder.Host.UseNLog();
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Must run before any middleware that reads the request scheme/host (HTTPS redirect, auth,
// OAuth redirect_uri generation). Honors X-Forwarded-Proto from the TLS-terminating proxy so
// the Discord OAuth callback is built as https, not the proxy's internal http hop.
app.UseForwardedHeaders();

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
    services.AddDbContext<ApplicationDbContext>(
        options => options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"), x => x.CommandTimeout(30)),
        contextLifetime: ServiceLifetime.Scoped,
        optionsLifetime: ServiceLifetime.Singleton);


    services.AddDbContextFactory<ApplicationDbContext>(options => {
        options.UseNpgsql(Configuration.GetConnectionString("DefaultConnection"), x => {
            x.MigrationsAssembly("EGG9000.Common");
            x.CommandTimeout(30);
        });
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

    services
                .ConfigureApplicationCookie((options) => ConfigureAuthorizationCookie(options, "egg9000Cookie"))
                .ConfigureExternalCookie((options) => ConfigureAuthorizationCookie(options, "egg9000CookieExternal"));


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



#if RELEASE
        services.Configure<ActiveMonitorOptions>(options => {
            options.ServiceType = "site";
            options.ColorConfigKey = "SITE_COLOR";
            options.PollIntervalSeconds = 10;
        });
        // Register active monitor so every instance watches the shared deployment record
        // 1. Register the concrete class as a Singleton so other services can find it
        services.AddSingleton<ActiveMonitorHostedService>();
        // 2. Register it as a Hosted Service by resolving the existing Singleton instance
        services.AddHostedService(provider => provider.GetRequiredService<ActiveMonitorHostedService>());
#endif

    services.AddResponseCaching();
    services.AddAuthorization(options => {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    services.AddControllersWithViews().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
    services.AddRazorPages();
    services.AddTransient<IEmailSender, EmailSenderBlank>();
    services.AddHostedService<NewCoopChecker>();
    services.AddSingleton<DatabaseCache>();
    services.AddHostedService<UserCacheRefreshService>();
    services.AddHostedService<ActiveCoopsCacheRefreshService>();

    services.Configure<ForwardedHeadersOptions>(options => {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Trusted proxy subnet(s) come from TRUSTED_PROXY_NETWORKS (comma-separated CIDR). Forwarded
        // headers from any other source IP are ignored. Falls back to the prior hardcoded value when
        // unset so an un-updated deploy keeps its old behavior instead of trusting nothing.
        string trustedNetworks = Configuration["TRUSTED_PROXY_NETWORKS"] ?? "192.168.0.0/24";
        foreach (string cidr in trustedNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        }
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


    var bugsnagConfig = new Bugsnag.Configuration(Configuration.GetConnectionString("BugSnagApiKey"));
    var bs = new Bugsnag.Client(bugsnagConfig);
    services.AddSingleton<Bugsnag.IClient>(bs);
    // Test Bugsnag is working
    if(bs != null) {
        try {
            bs.Notify(new Exception("Bugsnag test - startup successful"));
            logger.Log(NLog.LogLevel.Info, "Bugsnag test notification sent");
        } catch(Exception ex) {
            logger.Log(NLog.LogLevel.Error, ex, "Failed to send Bugsnag test notification");
        }
    }

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
        // Per-instance temporary queue so a version update fans out to every running process
        // instead of being load-balanced across a shared queue.
        x.AddConsumer<UpdateApiVersionsConsumer>().Endpoint(e => { e.InstanceId = Guid.NewGuid().ToString("N"); e.Temporary = true; });
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