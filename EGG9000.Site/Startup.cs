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
using EGG9000.Bot.Services;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Discord;
using Bugsnag.AspNet.Core;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication;

namespace EGG9000.Site {
    public class Startup {
        public Startup(IConfiguration configuration) => Configuration = configuration;

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {
            Console.WriteLine(Configuration.GetConnectionString("DefaultConnection"));
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


            services.AddAuthentication(options => {
            }).AddDiscord(options => {
                options.ClientId = Configuration["ConnectionStrings:ClientId"];
                options.ClientSecret = Configuration["ConnectionStrings:ClientSecret"];
                options.Events = new Microsoft.AspNetCore.Authentication.OAuth.OAuthEvents {
                    OnTicketReceived = context => {
                        Console.WriteLine("est");
                        return Task.FromResult(0);
                    }
                };
                //options.Events = new Microsoft.AspNetCore.Authentication.OAuth.OAuthEvents {
                //    OnTicketReceived = context => {
                //        context.Properties.IsPersistent = true;
                //        context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
                //        context.Properties.AllowRefresh = true;
                //        return Task.FromResult(0);
                //    }
                //};
            });





            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("192.168.0.0"), 24));
            });

            services.AddResponseCaching();
            services.AddControllersWithViews().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
            services.AddRazorPages();
            services.AddTransient<IEmailSender, EmailSenderBlank>();
            services.AddSingleton<APILink>();
            services.AddHostedService<APILink>(provider => provider.GetService<APILink>());


            services.AddCors(options =>
            {
                var corsBuilder = new CorsPolicyBuilder();
                corsBuilder.AllowAnyHeader();
                corsBuilder.AllowAnyMethod();
                corsBuilder.WithOrigins("https://customer.xfinity.com"); // For anyone access.
                                                                         //corsBuilder.WithOrigins("http://localhost:56573"); // for a specific url. Don't add a forward slash on the end!

                options.AddPolicy("SiteCorsPolicy", corsBuilder.Build());
            });
            //Console.WriteLine(Configuration["Discord:Token"]);
            var config = new DiscordSocketConfig() {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers
            };
            var client = new DiscordSocketClient(config);
            client.LoginAsync(Discord.TokenType.Bot, Configuration["ConnectionStrings:Token"]).Wait();
            client.StartAsync().Wait();
            _ = client.DownloadUsersAsync(client.Guilds);
            services.AddSingleton(client);

            services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
            services.AddResponseCompression(options => {
                options.Providers.Add<GzipCompressionProvider>();
                options.EnableForHttps = true;
            });
            services.AddBugsnag(configuration => {
                configuration.ApiKey = "7740fdc81aa4f54c5cef05983c7984fe";
            });
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
                app.UseDatabaseErrorPage();
            } else {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

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
