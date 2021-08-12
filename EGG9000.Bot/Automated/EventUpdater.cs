using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using System.Numerics;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using Humanizer;
using Microsoft.Extensions.Caching.Memory;
using EGG9000.Common.Helpers;

namespace EGG9000.Bot.Automated {
    public class EventUpdater : _UpdaterBase {
        private IConfiguration _config;
        private ApplicationDbContext _db;

        public EventUpdater(IConfiguration Configuration, DiscordSocketClient client,
            Bugsnag.IClient bugsnag) : base(TimeSpan.FromMinutes(1), TimeSpan.Zero, client, bugsnag) {
            _config = Configuration;
            _db = new ApplicationDbContext(_config["ConnectionStrings:DefaultConnection"]);
        }

        public async Task TestEvent(SocketMessage message, string[] args) {
            try {
                var eventC = _db.EventCustomizations.FirstOrDefault(x => x.Type == args[0]);

                if(eventC == null) {
                    await message.Channel.SendMessageAsync($"ERROR: Unable to find event type - {args[0]}");
                    return;
                }


                var e = await _db.Events.AsQueryable().OrderByDescending(x => x.Ends).FirstOrDefaultAsync(x => x.Type == eventC.Type);

                await message.Channel.SendMessageAsync(embed: GetEmbed(e, eventC));
            } catch(Exception e) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.Message}");
            }
        }

        public override async Task Run(object state) {

            var _db = new ApplicationDbContext(_config["ConnectionStrings:DefaultConnection"]);


            var response = await ContractsAPI.GetPeriodicalsAsync();

            var eventCustomizations = await _db.EventCustomizations.AsQueryable().ToListAsync();

            var recentEvents = await _db.Events.AsQueryable().Where(x => x.Ends > DateTimeOffset.Now.AddDays(-1)).ToListAsync();
            //var currentEvents = new List<Event>();

            if(response?.Events?.Events == null) {
                Console.WriteLine("Response is null for Event Updater");
                return;
            }

            var endedEvents = recentEvents.Where(x => !x.Ended && !response.Events.Events.Any(y => y.Identifier == x.Identifier)).ToList();

            foreach(var e in endedEvents) {
                e.Ended = true;
                var embed = GetEmbed(e, eventCustomizations.First(x => x.Type == e.Type), Ended: true);
                await UpdateMessages(e, embed);
            }

            foreach(var e in response.Events.Events) {
                var currentEvent = recentEvents.FirstOrDefault(x => x.Identifier == e.Identifier);
                if(currentEvent == null) {
                    var newEvent = new Event(e);
                    _db.Add(newEvent);
                    recentEvents.Add(newEvent);

                    var embed = GetEmbed(newEvent, eventCustomizations.First(x => x.Type == e.Type));

                    await PostMessages(newEvent, embed);

                    await _db.SaveChangesAsync();
                } else {
                    var difference = Math.Abs((DateTimeOffset.Now.AddSeconds(e.SecondsRemaining) - currentEvent.Ends).TotalSeconds);
                    //Console.WriteLine($"Difference in seconds {difference}");

                    var significantChange = false;

                    if(currentEvent.Ended) {
                        currentEvent.Ended = false;
                    }

                    if(currentEvent.Type != e.Type) {
                        currentEvent.Type = e.Type;
                        significantChange = true;
                    }

                    if(currentEvent.Subtitle != e.Subtitle) {
                        currentEvent.Subtitle = e.Subtitle;
                    }
                    if(currentEvent.Multiplier != e.Multiplier) {
                        currentEvent.Multiplier = e.Multiplier;
                        significantChange = true;
                    }

                    if(difference > 30) {
                        Console.WriteLine($"Updating event by add {difference} seconds");
                        currentEvent.Ends = DateTimeOffset.Now.AddSeconds(e.SecondsRemaining);
                    }

                    if(!string.IsNullOrEmpty(currentEvent.MessageIds)) {
                        var embed = GetEmbed(currentEvent, eventCustomizations.First(x => x.Type == e.Type));
                        if(significantChange) {
                            var crossOutEmbed = GetEmbed(currentEvent, eventCustomizations.First(x => x.Type == e.Type), true);
                            await UpdateMessages(currentEvent, crossOutEmbed);
                            await PostMessages(currentEvent, embed);
                        } else {

                            await UpdateMessages(currentEvent, embed);
                        }
                    }
                }
                await _db.SaveChangesAsync();
            }
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                var channel = guild.GetEventChannel();
                var newName = "game-events";
                var singleEmoji = "";
                var stackedEmoji = "";

                var eventsWithCustom = recentEvents.Where(x => !x.Ended).Select(x => new { Event = x, Custom = eventCustomizations.First(y => y.Type == x.Type) }).OrderByDescending(x => x.Custom.Priority);

                foreach(var e in eventsWithCustom) {
                    if(e.Custom.Priority > 0 || true) {
                        stackedEmoji += e.Custom.Emoji ?? "";
                    } else {
                        singleEmoji = e.Custom.Emoji ?? "";
                    }
                }
                if(stackedEmoji.Length > 0) {
                    newName = stackedEmoji + newName;
                } else if(singleEmoji.Length > 0) {
                    newName = singleEmoji + newName;
                } else {
                    //newName = newName;
                }
                if(channel.Name != newName && channel != null) {
                    await channel.ModifyAsync(x => x.Name = newName);
                }
            }
        }
        private async Task PostMessages(Event newEvent, Embed embed) {
            var messageIds = new List<ulong>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                var channel = guild.GetEventChannel();
                var message = await channel.SendMessageAsync(embed: embed);
                messageIds.Add(message.Id);
            }
            newEvent.MessageIds = JsonConvert.SerializeObject(messageIds);

        }

        private async Task UpdateMessages(Event currentEvent, Embed embed) {
            var messageIds = JsonConvert.DeserializeObject<List<ulong>>(currentEvent.MessageIds);
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                var channel = guild.GetEventChannel();
                if(channel != null) {
                    foreach(var mid in messageIds) {
                        try {
                            var message = ((RestUserMessage)await channel.GetMessageAsync(mid));
                            if(message != null) {
                                await message.ModifyAsync(msg => msg.Embed = embed);
                            }
                        } catch(Exception) {
                            Console.WriteLine($"Error Updating Messages: ");
                        }
                    }
                }
            }
        }

        public static Embed GetEmbed(Event e, EventCustomization eventC, bool CrossOut = false, bool Ended = false) {
            var multiplier = e.Multiplier;
            var equivalent_multiplier = Math.Round(Math.Pow(e.Multiplier, 0.21), 2);
            var percent = (1 - e.Multiplier) * 100;
            var description = $"**{e.Subtitle}**\n{eventC.Description}";
            description = description.Replace("{{percent}}", percent.ToString()).Replace("{{multiplier}}", multiplier.ToString());
            var title = "";
            switch(eventC.Type) {
                case "prestige-boost":
                    title = $"{multiplier}x <:Egg_soul_SE:724341890794913964> Soul Egg";
                    break;
                case "piggy-boost":
                    title = $"{multiplier}x <:Piggy_bank:724396277676113955> Piggy Bank Growth";
                    break;
            }
            title += (Ended ? "\nEnded" : $"\nAvailable for {(e.Ends - DateTimeOffset.Now).Humanize(precision: 2).ShortenTime()}");
            Color color = Color.Blue;
            if(CrossOut) {
                color = Color.Red;
            } else if(Ended) {
                color = Color.DarkGrey;
            }
            var embed = new EmbedBuilder()
                .WithTitle(CrossOut ? $"~~{title}~~" : title)
                .WithColor(color)
                .WithAuthor("Egg, Inc Special Event", "https://vignette.wikia.nocookie.net/egg-inc/images/2/23/Egg-inc-icon.jpg/revision/latest/scale-to-width-down/180?cb=20160721002751")
                .WithDescription(CrossOut ? $"~~{description}~~" : description)
                .WithFooter("Last Updated")
                .WithTimestamp(DateTimeOffset.Now);

            if(!string.IsNullOrWhiteSpace(eventC.ThumbnailURL)) {
                embed.WithThumbnailUrl(eventC.ThumbnailURL);
            }
            foreach(var tip in JsonConvert.DeserializeObject<List<dynamic>>(eventC.Fields)) {
                var value = ((string)tip.Value).Replace("{{equivalent_multiplier}}", equivalent_multiplier.ToString());
                value = value.Replace("{{percent}}", percent.ToString());
                value = value.Replace("{{multiplier}}", multiplier.ToString());
                var name = ((string)tip.Name).Replace("{{multiplier}}", multiplier.ToString());
                if(CrossOut) {
                    embed.AddField($"~~{name}~~", $"~~{value}~~");
                } else {
                    embed.AddField(name, value);
                }
            }

            return embed.Build();
        }

        //public static Embed GetEmbed(Ei.EggIncEvent e) {
        //    string msg = "";
        //    var timeRemaining = TimeSpan.FromSeconds(e.SecondsRemaining).Humanize(precision: 2).ShortenTime();
        //    var embed = new EmbedBuilder()
        //        .WithTitle($"{e.Subtitle} Available for {timeRemaining}")
        //        .WithColor(Color.Blue)
        //        .WithAuthor("Egg, Inc Special Event", "https://vignette.wikia.nocookie.net/egg-inc/images/2/23/Egg-inc-icon.jpg/revision/latest/scale-to-width-down/180?cb=20160721002751")
        //        .WithCurrentTimestamp();

        //    switch (e.Type) {
        //        case "piggy-boost":
        //            break;
        //        case "prestige-boost":
        //            break;
        //        case "earnings-boost":
        //            embed.WithDescription("*Sun is shining and birds are singing. Your truck driver is a happy camper. When the delivery man comes to pick up your eggs he's feeling extra generous and gives you four times as many bocks for your eggs.*");
        //            embed.AddField("Prestige Event Comparison", $"This would be equivalent to a { Math.Round(Math.Pow(e.Multiplier, 0.21), 2)}x prestige event, except you don't lose anything at the end of the event.");
        //            embed.WithThumbnailUrl("http://files.fms.sglade.com/Z6ZjSq05/dollars.png");
        //            break;
        //        case "gift-boost":
        //            break;
        //        case "drone-boost":
        //            break;
        //        case "epic-research-sale":
        //            msg += "Applies to Epic Research";
        //            break;
        //        case "research-sale":
        //            msg += "Applies to Common Research";
        //            break;
        //        case "vehicle-sale":
        //            break;
        //        case "hab-sale":
        //            embed.WithDescription("*In the process of upgrading your old chicken houses you decide to hire a cheeky contractor on the black market... Their agreement was simple: new houses, 70% off, and unlimited fresh eggs for the contractor's crew every morning.*");
        //            break;
        //        case "boost-sale":
        //            break;
        //        case "piggy-cap-boost":
        //            msg += "No cap on the piggy bank. Gains are retained when event ends.";
        //            embed.AddField(new EmbedFieldBuilder().WithName("TIP - Buy Warming Bulb Boosts").WithValue("If you plan on breaking your piggy bank soon, buy as many Warming Bulb boosts as you can afford. You will get more GEs back than you spent. Buy them one at a time."));
        //            embed.AddField(new EmbedFieldBuilder().WithName("TIP - Prestige and buy Research").WithValue("To take advantage of this event, prestige as many times as possible buying research for each egg. Don't skip eggs."));
        //            embed.AddField(new EmbedFieldBuilder().WithName("TIP - Epic Research HOLD TO RESEARCH is very useful").WithValue("It is a lot quicker to buy science with this research. In addition, you can hold down on multiple research at a time to buy more quickly."));
        //            break;
        //        case "boost-duration":
        //            break;
        //    }

        //    if (msg != "")
        //        embed.WithDescription(msg);


        //    return embed.Build();
        //}
    }
}