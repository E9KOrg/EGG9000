using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.EntityFrameworkCore;
//using EGG9000.Site.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EGG9000.Common.Database;
using EGG9000.Site.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Cors.Infrastructure;
using EGG9000.Site.Data;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.AspNetCore.Http;
using EGG9000.Common.Services;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Discord;
using Bugsnag.AspNet.Core;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication;
using EGG9000.Common.Factories;
using Microsoft.Extensions.Logging;

namespace EGG9000.Site {
    public class Startup {
        public Startup(IConfiguration configuration) => Configuration = configuration;

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {
            Console.WriteLine(Configuration.GetConnectionString("DefaultConnection"));
            Console.WriteLine(Configuration.GetChildren().Count());
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(
                    Configuration.GetConnectionString("DefaultConnection")));



            services.AddIdentity<IdentityUser, IdentityRole>(options => {
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = false;
            })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders().AddClaimsPrincipalFactory<CustomClaimsPrincipleFactory>();

            services.ConfigureExternalCookie(options => {
                options.ExpireTimeSpan = TimeSpan.FromDays(15);
            });

            services.ConfigureApplicationCookie(options => {
                options.LoginPath = $"/Identity/Account/Login";
                options.LogoutPath = $"/Identity/Account/Logout";
                options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(15);
            });

            services
                .ConfigureApplicationCookie((options) => ConfigureAuthorizationCookie(options, "egg9000Cookie"))
                .ConfigureExternalCookie((options) => ConfigureAuthorizationCookie(options, "egg9000CookieExternal"));


            Console.WriteLine(Configuration.GetConnectionString("ClientId"));
            Console.WriteLine(Configuration.GetConnectionString("ClientSecret"));

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
            });






            services.AddResponseCaching();
            services.AddControllersWithViews().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
            services.AddRazorPages();
            services.AddTransient<IEmailSender, EmailSenderBlank>();
            services.Configure<APILinkOptions>(x => x.AsyncLoadCache = true);
            services.AddSingleton<APILink>();
            services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

            services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("192.168.0.0"), 24));
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
#else
            services.AddBugsnag();
#endif

            services.AddDatabaseDeveloperPageExceptionFilter();
        }

        private static void ConfigureAuthorizationCookie(CookieAuthenticationOptions options, string cookieName) {
            options.Cookie.Name = cookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(150);
            options.SlidingExpiration = true;
            options.Cookie.MaxAge = TimeSpan.FromDays(150);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            app.UseForwardedHeaders();

            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            } else {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
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
            app.UseResponseCompression();

            app.UseEndpoints(endpoints => {
                endpoints.MapControllerRoute(
                    name: "invite", 
                    pattern: "invite", 
                    defaults: new { controller = "Home", action = "Invite" });
                endpoints.MapControllerRoute(
                    name: "coop", 
                    pattern: "coop/{ContractId}/{CoopId}", 
                    defaults: new { controller = "Home", action = "Coop" });
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();
            });
        }
    }
}
