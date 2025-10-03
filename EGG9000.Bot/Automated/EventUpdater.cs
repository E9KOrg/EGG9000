using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Ei;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public static class TimeUtils {
        public static DateTimeOffset RoundToClosestFifteen(this DateTimeOffset dto) {
            var totalMinutes = dto.TimeOfDay.TotalMinutes;
            var roundedTotalMinutes = Math.Round(totalMinutes / 15.0) * 15.0;
            var roundedDto = new DateTimeOffset(dto.Date.AddMinutes(roundedTotalMinutes), dto.Offset);
            return roundedDto;
        }
    }

    public class EventUpdater(IServiceProvider provider) : _UpdaterBase<EventUpdater>(TimeSpan.FromMinutes(1), TimeSpan.Zero, provider) {
        private class EventWithCustom {
            public Event Event { get; set; }
            public EventCustomization Customization { get; set; }
        }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await CheckShells(_db);

            var response = await ContractsAPI.GetPeriodicalsAsync();
            var responseDateTime = DateTimeOffset.UtcNow;
            var recentEvents = await _db.Events.AsQueryable().Where(x => x.Ends > DateTimeOffset.Now.AddDays(-1)).ToListAsync(CancellationToken.None);

            if(response?.Events?.Events == null) {
                _logger.LogWarning("Response is null for Event Updater");
                return;
            }

            var endedEvents = recentEvents.Where(x => !x.Ended && !response.Events.Events.Any(y => y.Identifier == x.Identifier)).ToList();

            foreach(var e in endedEvents) {
                e.Ended = true;
                await UpdateMessages(e, _db, Ended: true);
            }

            var events = response.Events.Events.ToList();
            foreach(var evt in events) {
                var currentEvent = recentEvents.FirstOrDefault(x => x.Identifier == evt.Identifier);
                if(currentEvent == null) {
                    var newEvent = new Event(evt);
                    newEvent.Ends = newEvent.Ends.RoundToClosestFifteen();
                    _db.Add(newEvent);
                    recentEvents.Add(newEvent);
                    await PostMessages(newEvent, _db);
                } else {
                    var significantChange = currentEvent.SignficantlyDifferent(evt);
                    var timeChange = Math.Abs(currentEvent.Ends.Subtract(responseDateTime.AddSeconds(evt.SecondsRemaining)).TotalSeconds) > 240;

                    currentEvent.Type = evt.Type;
                    currentEvent.Subtitle = evt.Subtitle;
                    currentEvent.Multiplier = evt.Multiplier;
                    currentEvent.Ended = false;

                    if(!string.IsNullOrEmpty(currentEvent.MessageIds)) {
                        if(significantChange) {
                            await UpdateMessages(currentEvent, _db, Crossout: true);
                            await PostMessages(currentEvent, _db);
                        } else if (timeChange) {
                            _logger.LogInformation($"Time change for {currentEvent.Type}, of {currentEvent.Ends.Subtract(DateTimeOffset.UtcNow.AddSeconds(evt.SecondsRemaining)).TotalSeconds} seconds");
                            currentEvent.Ends = responseDateTime.AddSeconds(evt.SecondsRemaining).RoundToClosestFifteen();
                            await UpdateMessages(currentEvent, _db);
                        }
                    }
                }
                await _db.SaveChangesAsync(CancellationToken.None);
                StillAlive();
            }
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild is null)
                    continue; 
                var newName = "game-events";
                var singleEmoji = "";
                var stackedEmoji = "";

                /* 'Normal' Game Events channel*/
                var channel = await _client.GetChannelAsync(GuildChannelType.GameEvents, guild);
                if(channel != default) {
                    var eventsWithCustom = recentEvents.Where(x => !x.Ended && !x.CcOnly).Select(async x => new EventWithCustom {
                        Event = x,
                        Customization = await _db.GetCustomizationAsync(dbguild, x)
                    }).Select(t => t.Result).ToList();

                    foreach(var e in eventsWithCustom) {
                        if(e.Customization?.Priority > 0 || true) {
                            stackedEmoji += e.Customization?.Emoji ?? "";
                        } else {
                            singleEmoji = e.Customization?.Emoji ?? "";
                        }
                    }
                    if(stackedEmoji.Length > 0) {
                        newName = stackedEmoji + newName;
                    } else if(singleEmoji.Length > 0) {
                        newName = singleEmoji + newName;
                    }

                    if(channel.Name != newName && channel != null) {
                        await channel.ModifyAsync(x => x.Name = newName);
                    }
                    StillAlive();
                }

                //"Reset" vars
                newName = "subscriber-game-events";
                singleEmoji = "";
                stackedEmoji = "";

                /* Subscriber-Only Game Events channel */
                var ccChannel = await _client.GetChannelAsync(GuildChannelType.SubscriptionGameEvents, guild);
                if(ccChannel != null) {
                    var ccEventsWithCustom = recentEvents.Where(x => !x.Ended && x.CcOnly).Select(async x => new EventWithCustom {
                        Event = x,
                        Customization = await _db.GetCustomizationAsync(dbguild, x)
                    }).Select(t => t.Result).ToList();

                    foreach(var se in ccEventsWithCustom) {
                        if(se.Customization?.Priority > 0 || true) {
                            stackedEmoji += se.Customization?.Emoji ?? "";
                        } else {
                            singleEmoji = se.Customization?.Emoji ?? "";
                        }
                    }
                    if(stackedEmoji.Length > 0) {
                        newName = stackedEmoji + newName;
                    } else if(singleEmoji.Length > 0) {
                        newName = singleEmoji + newName;
                    }

                    if(ccChannel.Name != newName && ccChannel != null) {
                        await ccChannel.ModifyAsync(x => x.Name = newName);
                    }
                }
            }
        }
        private async Task PostMessages(Event newEvent, ApplicationDbContext _db) {
            var messageIds = new List<ulong>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            foreach(var dbguild in dbguilds) {
                var customization = await _db.GetCustomizationAsync(dbguild, newEvent);
                var (embed, embedImage) = await GetEventEmbed(_db, newEvent, customization, false, false);

                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild is null)
                    continue;
                var ccEventChannel = await _client.GetChannelAsync(GuildChannelType.SubscriptionGameEvents, guild);
                var eventChannel = await _client.GetChannelAsync(GuildChannelType.GameEvents, guild);

                RestUserMessage message = null;
                var notification = customization?.Settings?.Notifications?
                    .OrderByDescending(x => x.MinValue)
                    .FirstOrDefault(x => (decimal)newEvent.Multiplier >= x.MinValue && x.GuildID == dbguild.DiscordSeverId) ?? null;

                //If the event is subscriber-only
                if(newEvent.CcOnly) {
                    //Send to non-CCs without ping
                    if(eventChannel != null) {
                        var ultraNotification = customization?.Settings?.Notifications?.FirstOrDefault(x => x.MinValue == -1) ?? null;
                        message = await eventChannel.SendFileIfExistsAsync(embedImage, text: ultraNotification != null ? $"<@&{ultraNotification.RoleID}>" : null, embed: embed);
                    }

                    //If the CC event channel was found, that's where we'll ping for CC events
                    if(ccEventChannel != null) {
                        var ccMessage = await ccEventChannel.SendFileIfExistsAsync(embedImage, text: notification != null ? $"<@&{notification.RoleID}>" : null, embed: embed);
                        //Add the CC event channel message to the IDs
                        messageIds.Add(ccMessage.Id);
                    }
                } else {
                    //Only send to non-CC channel, with ping
                    if(eventChannel != null) {
                        message = await eventChannel.SendFileIfExistsAsync(embedImage, text: notification != null ? $"<@&{notification.RoleID}>" : null, embed: embed);
                    }
                }

                
                //Always add the message id
                if(message != null) messageIds.Add(message.Id);
                StillAlive();
            }
            newEvent.MessageIds = JsonConvert.SerializeObject(messageIds);

        }

        private async Task UpdateMessages(Event currentEvent, ApplicationDbContext _db, bool Ended = false, bool Crossout = false) {
            var messageIds = JsonConvert.DeserializeObject<List<ulong>>(currentEvent.MessageIds);
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            foreach(var dbguild in dbguilds) {
                var customization = await _db.GetCustomizationAsync(dbguild, currentEvent);
                var (embed, embedImage) = await GetEventEmbed(_db, currentEvent, customization, Ended, Crossout);

                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild == null) continue;
                var eventChannel = await _client.GetChannelAsync(GuildChannelType.GameEvents, guild);
                if(eventChannel != null) {
                    foreach(var mid in messageIds) {
                        try {
                            var message = (RestUserMessage)await eventChannel.GetMessageAsync(mid);
                            if(message != null) {

                                var notification = customization.Settings.Notifications?
                                    .OrderByDescending(x => x.MinValue)
                                    .FirstOrDefault(x => (decimal)currentEvent.Multiplier >= x.MinValue && x.GuildID == dbguild.DiscordSeverId);
                                await message.ModifyAsync(msg => {
                                    msg.Embed = embed;
                                    msg.Content = (notification != null && !currentEvent.CcOnly) ? $"<@&{notification.RoleID}>" : null;
                                    msg.Attachments = embedImage.HasValue ? new List<FileAttachment> { embedImage.Value } : [];
                                });
                            }
                        } catch(Exception) {
                            _logger.LogWarning("Error Updating Messages for {guild}", guild.Name);
                        }
                    }
                }

                var ccEventChannel = await _client.GetChannelAsync(GuildChannelType.SubscriptionGameEvents, guild);
                if(ccEventChannel != null && currentEvent.CcOnly) {
                    foreach(var mid in messageIds) {
                        try {
                            var message = (RestUserMessage)await ccEventChannel.GetMessageAsync(mid);
                            if(message != null) {
                                var notification = customization.Settings.Notifications?
                                    .OrderByDescending(x => x.MinValue)
                                    .FirstOrDefault(x => (decimal)currentEvent.Multiplier >= x.MinValue && x.GuildID == dbguild.DiscordSeverId);
                                await message.ModifyAsync(msg => {
                                    msg.Embed = embed;
                                    msg.Content = notification != null ? $"<@&{notification.RoleID}>" : null;
                                    msg.Attachments = embedImage.HasValue ? new List<FileAttachment> { embedImage.Value } : [];
                                });
                            }
                        } catch(Exception) {
                            _logger.LogWarning("Error Updating Messages for {guild}", guild.Name);
                        }
                    }
                }
                StillAlive();
            }
        }

        public static async Task<(Embed, FileAttachment?)> GetEventEmbed(ApplicationDbContext _db, Event e, EventCustomization eventC, bool Ended = false, bool CrossOut = false){
            var multiplier = e.Multiplier;
            var equivalent_multiplier = Math.Round(Math.Pow(e.Multiplier, 0.21), 2);
            var percent = Math.Round((1 - e.Multiplier) * 100, 2);
            var description = $"**{e.Subtitle}**\n";
            var title = "";
            FileAttachment? eventImage = null;

            if(eventC is not null) {
                description += eventC.Description;
                switch(eventC.Type) {
                    case "prestige-boost":
                        title = $"{multiplier}x <:Egg_soul_SE:724341890794913964> Soul Egg";
                        break;
                    case "piggy-boost":
                        title = $"{multiplier}x <:Piggy_bank:724396277676113955> Piggy Bank Growth";
                        break;
                }
            }

            description = description.Replace("{{percent}}", percent.ToString()).Replace("{{multiplier}}", multiplier.ToString());
            if(Ended) title += $"\nEnded <t:{e.Ends.ToUnixTimeSeconds()}:R>";
            else title += $"\nEnds <t:{e.Ends.ToUnixTimeSeconds()}:R> (<t:{e.Ends.ToUnixTimeSeconds()}>)";

            var color = e.CcOnly ? new Color(uint.Parse("a932c7", NumberStyles.HexNumber)) : Color.Blue;
            if(CrossOut) color = Color.Red;
            else if(Ended) color = Color.DarkGrey;

            var embed = new EmbedBuilder()
                .WithTitle(CrossOut ? $"~~{title}~~" : title)
                .WithColor(color)
                .WithDescription(CrossOut ? $"~~{description}~~" : description);

            string discordImagePath;
            var generatedImage = await _db.GetEventImageAsync(e);
            if (generatedImage is null) { // Either the site had an issue, or the image didn't exist
                discordImagePath = e.CcOnly ? "https://cdn.discordapp.com/emojis/1131045418319495369.webp?size=96&quality=lossless"
                    : "https://vignette.wikia.nocookie.net/egg-inc/images/2/23/Egg-inc-icon.jpg/revision/latest/scale-to-width-down/180?cb=20160721002751";
            } else {
                discordImagePath = $"attachment://{e.Type}.png";
                eventImage = generatedImage.GetFileAttachment($"{e.Type}.png", "Event Image");
            }

            embed.WithAuthor($"Egg, Inc {(e.CcOnly ? "ULTRA-Only Event" : "Special Event")}", discordImagePath);

            if(!string.IsNullOrWhiteSpace(eventC?.ThumbnailURL)) {
                embed.WithThumbnailUrl(eventC.ThumbnailURL);
            }

            if(eventC is not null) {
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
            }

            return (embed.Build(), eventImage);
        }

        public async Task CheckShells(ApplicationDbContext db) {
            var config = await ContractsAPI.Post<ConfigResponse, ConfigRequest>(new ConfigRequest { ArtifactsUnlocked = true, FuelTankUnlocked = true, SoulEggs = 2e30 }, ContractsAPI.UserId, true);

            if(config is null) return; // This randomly failed while I was working
            var shells = config.DlcCatalog.ShellObjects.Where(x => x.Expires).ToList();

            var expiringShells = db.ExpiringShells.Where(x => x.Expires > DateTimeOffset.Now.AddHours(-1));


            var shellsToUpdate = new List<ExpiringShell>();
            foreach(var shell in shells) {
                var expiringShell = expiringShells.FirstOrDefault(x => x.Identifier == shell.Identifier);
                if(expiringShell is null) {
                    expiringShell = new ExpiringShell(shell);
                    if(shell.SecondsRemaining > 240) {
                        shellsToUpdate.Add(expiringShell);
                        db.ExpiringShells.Add(expiringShell);
                        await db.SaveChangesAsync();
                    }

                } else {
                    if(expiringShell.Name != shell.Name ||
                        (expiringShell.Expires - DateTimeOffset.Now.AddSeconds(shell.SecondsRemaining)).Duration() > TimeSpan.FromMinutes(1) ||
                        expiringShell.Price != shell.Price ||
                        expiringShell.AssetType != shell.AssetType ||
                        shell.SecondsRemaining < 0
                    ) {
                        expiringShell.Name = shell.Name;
                        expiringShell.Expires = DateTimeOffset.Now.AddSeconds(shell.SecondsRemaining);
                        expiringShell.Price = shell.Price;
                        expiringShell.AssetType = shell.AssetType;
                        expiringShell.Json = JsonConvert.SerializeObject(shell);

                        if(shell.SecondsRemaining < 0) {
                            expiringShell.Archived = true;
                        }
                        if(shell.SecondsRemaining > 0)
                            shellsToUpdate.Add(expiringShell);
                        await db.SaveChangesAsync();
                    }
                }
            }
            await PostOrUpdateShellMessages(shellsToUpdate, db);
            await db.SaveChangesAsync();
        }

        public static Embed GetShellEmbed(ExpiringShell expiringShell) {
            var shell = JsonConvert.DeserializeObject<ShellObjectSpec>(expiringShell.Json);
            var embed = new EmbedBuilder()
                .WithColor(shell.SecondsRemaining > 0 ? Color.Blue : Color.DarkGrey)
                .WithAuthor("Egg, Inc Limited Time Shell", "https://vignette.wikia.nocookie.net/egg-inc/images/2/23/Egg-inc-icon.jpg/revision/latest/scale-to-width-down/180?cb=20160721002751")
                .WithDescription($"New {expiringShell.AssetType}: {expiringShell.Name} for {expiringShell.Price}<:tickets:998630687831769189>\nExpires <t:{DateTimeOffset.Now.AddSeconds(shell.SecondsRemaining).ToUnixTimeSeconds()}:R>")
                ;
            return embed.Build();

        }

        public async Task PostOrUpdateShellMessages(List<ExpiringShell> expiringShells, ApplicationDbContext db) {
            var dbguilds = await db.Guilds.AsQueryable().ToListAsync();
            foreach(var shell in expiringShells) {
                var embed = GetShellEmbed(shell);
                if(string.IsNullOrEmpty(shell.MessageIds)) {
                    var messageIDs = new List<(ulong, ulong)>();
                    foreach(var dbguild in dbguilds) {
                        var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                        if(guild is null) continue;
                        var channel = await _client.GetChannelAsync(GuildChannelType.LimitedTimeShells, guild);
                        if(channel is not null) {
                            var ShellsRole = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.LimitedTimeShellsRole)?.Id;

                            var message = await channel.SendMessageAsync(ShellsRole.HasValue ? $"<@&{ShellsRole}>" : null, embed: embed);

                            messageIDs.Add((channel.Id, message.Id));
                        }
                    }
                    shell.MessageIds = JsonConvert.SerializeObject(messageIDs);
                } else {
                    var messageIDs = JsonConvert.DeserializeObject<List<(ulong, ulong)>>(shell.MessageIds);
                    foreach(var message in messageIDs) {
                        var channel = _client.GetChannel(message.Item1);
                        var dbguild = dbguilds.First(x => x.ChannelDetails.Any(x => x.Id == channel?.Id));
                        var ShellsRole = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.LimitedTimeShellsRole)?.Id;
                        if(channel is not null) {
                            await (channel as SocketTextChannel).ModifyMessageAsync(message.Item2, msg => { msg.Embed = embed; msg.Content = ShellsRole.HasValue ? $"<@&{ShellsRole}>" : null; });
                        }
                    }
                }
            }
        }
    }
}