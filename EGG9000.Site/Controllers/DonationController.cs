using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Site.Controllers {
    public class DonationController(ApplicationDbContext db, DiscordSocketClient discord) : Controller {
        private readonly ApplicationDbContext _db = db;
        private readonly DiscordSocketClient _discord = discord;

        public IActionResult Index() {
            return View();
        }

        public IActionResult ThankYou() {
            return View();
        }

        public async Task<IActionResult> Endpoint([FromBody] EndPointObject body) {
            SocketGuildUser discordUser = null;
            var guild = _discord.Guilds.First(x => x.Id == 656455567858073601);


            var test = !body.livemode;
            Stripe.StripeConfiguration.ApiKey = test ? "sk_test_51Huh4PFPOewxUi5tQQHemFZFQ4x5CJ7edl8MmlS4QkIr2hFXcUSlp5DGL406mlg2MiJO6utmgHBDv5vL9Kgyqp5500L5GcePwf" : "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";
            var lineItems = new SessionLineItemService().List(body.data.@object.id, new SessionLineItemListOptions { Limit = 20 });
            var donationType = string.Join("/", lineItems.Select(x => x.Description));
            if(!string.IsNullOrWhiteSpace(body.data.@object.client_reference_id)) {
                var donation = new Donation {
                    UserId = Guid.Parse(body.data.@object.client_reference_id),
                    Amount = body.data.@object.amount_total / 100,
                    When = DateTimeOffset.FromUnixTimeSeconds(body.created), Type = donationType
                };
                _db.Donations.Add(donation);

                await _db.SaveChangesAsync();

                var dbuser = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.Id == Guid.Parse(body.data.@object.client_reference_id));
                discordUser = guild.Users.FirstOrDefault(x => x.Id == dbuser.DiscordId);
                var role = guild.Roles.FirstOrDefault(x => x.Id == 785575541469085746);
                if(role != null) {
                    _ = discordUser.AddRoleAsync(role).ConfigureAwait(false);
                }
            }
            //656455568353132546 general

            var channelId = test ? 777303939442802710 : (ulong)656455568353132546;
            var channel = guild.TextChannels.First(x => x.Id == channelId);


            _ = channel.SendMessageAsync($"{(discordUser == null ? "Anonymous" : discordUser.Mention)} donated {body.data.@object.amount_total / 100:C0} USD to a {donationType}. Thank you for your support! {(body.livemode ? "" : "**TEST MODE** ")}");

            return Content("");
        }
    }

    public class TotalDetails {
        public int amount_discount { get; set; }
        public int amount_tax { get; set; }
    }

    public class Object {
        public string id { get; set; }
        public string @object { get; set; }
        public object allow_promotion_codes { get; set; }
        public int amount_subtotal { get; set; }
        public int amount_total { get; set; }
        public object billing_address_collection { get; set; }
        public string cancel_url { get; set; }
        public string client_reference_id { get; set; }
        public string currency { get; set; }
        public string customer { get; set; }
        public object customer_email { get; set; }
        public bool livemode { get; set; }
        public object locale { get; set; }
        public string mode { get; set; }
        public string payment_intent { get; set; }
        public List<string> payment_method_types { get; set; }
        public string payment_status { get; set; }
        public object setup_intent { get; set; }
        public object shipping { get; set; }
        public object shipping_address_collection { get; set; }
        public object submit_type { get; set; }
        public object subscription { get; set; }
        public string success_url { get; set; }
        public TotalDetails total_details { get; set; }
    }

    public class Data {
        public Object @object { get; set; }
    }

    public class Request {
        public object id { get; set; }
        public object idempotency_key { get; set; }
    }

    public class EndPointObject {
        public string id { get; set; }
        public string @object { get; set; }
        public string api_version { get; set; }
        public int created { get; set; }
        public Data data { get; set; }
        public bool livemode { get; set; }
        public int pending_webhooks { get; set; }
        public Request request { get; set; }
        public string type { get; set; }
    }
}
