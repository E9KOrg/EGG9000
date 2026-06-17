using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Site.Controllers {
    [AllowAnonymous]
    public class DonationController(ApplicationDbContext db, DiscordSocketClient discord, IConfiguration configuration) : Controller {
        private readonly ApplicationDbContext _db = db;
        private readonly DiscordSocketClient _discord = discord;
        private readonly IConfiguration _configuration = configuration;

        private const ulong PalaceGuildId = 656455567858073601;
        private const ulong DonorRoleId = 785575541469085746;
        private const ulong LiveAnnounceChannelId = 656455568353132546;
        private const ulong TestAnnounceChannelId = 777303939442802710;

        public IActionResult Index() {
            return View();
        }

        public IActionResult ThankYou() {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Endpoint() {
            var apiKey = SecretsHelper.GetConfigOrSecret(_configuration, "ConnectionStrings:StripeApiKey", "stripe_api_key");
            var webhookSecret = SecretsHelper.GetConfigOrSecret(_configuration, "ConnectionStrings:StripeWebhookSecret", "stripe_webhook_secret");

            // No key / signing secret means donations are not configured (e.g. inactive Stripe account).
            // Reject rather than trusting the request body.
            if(string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(webhookSecret)) {
                return NotFound();
            }
            StripeConfiguration.ApiKey = apiKey;

            var json = await new StreamReader(Request.Body).ReadToEndAsync();
            Stripe.Event stripeEvent;
            try {
                // Verifies the Stripe-Signature header against the webhook signing secret. Throws on
                // a forged/replayed/tampered payload, which is the only authentication this endpoint has.
                stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], webhookSecret);
            } catch(StripeException) {
                return BadRequest();
            }

            if(stripeEvent.Type != "checkout.session.completed" || stripeEvent.Data.Object is not Session session) {
                return Ok();
            }

            var guild = _discord.Guilds.FirstOrDefault(x => x.Id == PalaceGuildId);
            if(guild is null) return Ok();

            var lineItems = new SessionLineItemService().List(session.Id, new SessionLineItemListOptions { Limit = 20 });
            var donationType = string.Join("/", lineItems.Select(x => x.Description));
            var amount = (session.AmountTotal ?? 0) / 100;

            SocketGuildUser discordUser = null;
            if(!string.IsNullOrWhiteSpace(session.ClientReferenceId) && Guid.TryParse(session.ClientReferenceId, out var userId)) {
                _db.Donations.Add(new Donation {
                    UserId = userId,
                    Amount = amount,
                    When = new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc)),
                    Type = donationType
                });
                await _db.SaveChangesAsync();

                var dbuser = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.Id == userId);
                if(dbuser is not null) {
                    discordUser = guild.Users.FirstOrDefault(x => x.Id == dbuser.DiscordId);
                    var role = guild.Roles.FirstOrDefault(x => x.Id == DonorRoleId);
                    if(discordUser is not null && role is not null) {
                        _ = discordUser.AddRoleAsync(role).ConfigureAwait(false);
                    }
                }
            }

            var channelId = session.Livemode ? LiveAnnounceChannelId : TestAnnounceChannelId;
            var channel = guild.TextChannels.FirstOrDefault(x => x.Id == channelId);
            if(channel is not null) {
                _ = channel.SendMessageAsync($"{(discordUser == null ? "Anonymous" : discordUser.Mention)} donated {amount:C0} USD to a {donationType}. Thank you for your support! {(session.Livemode ? "" : "**TEST MODE** ")}");
            }

            return Ok();
        }
    }
}
