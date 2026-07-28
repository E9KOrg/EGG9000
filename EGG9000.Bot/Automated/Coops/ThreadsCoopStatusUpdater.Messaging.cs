using Discord;
using Discord.Rest;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        private class StatusResponse {
            public ContractCoopStatusResponse Status { get; set; }
            public List<IMessage> DiscordMessages { get; set; }
        }

        internal static async Task<List<IMessage>> GetDiscordMessages(ITextChannel coopChannel, Coop coop, CancellationToken cancellationToken) {
            var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");

            IEnumerable<IMessage> discordMessages;
            try {

                discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];
            } catch(Exception) {
                try {
                    await Task.Delay(100, cancellationToken);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];

                } catch(Exception) {
                    await Task.Delay(100, cancellationToken);
                    discordMessages = UpdateMessageIDs.Count > 0 ? await coopChannel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.After, 12, options: new RequestOptions { CancelToken = cancellationToken }).FlattenAsync() : [];
                }
            }

            var messages = new List<IMessage>();
            foreach(var id in UpdateMessageIDs) {
                var message = discordMessages.FirstOrDefault(x => x.Id == id);
                if(message == null) {
                    for(var i = 0; i < 10; i++) {
                        try {
                            message = await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                            break;
                        } catch(Exception) {
                            await Task.Delay(500, cancellationToken);
                        }
                    }
                    message ??= await coopChannel.GetMessageAsync(id, options: new RequestOptions { CancelToken = cancellationToken });
                }
                if(message != null)
                    messages.Add(message);
            }

            return messages;
        }

        private async Task<StatusResponse> GetStatus(Coop coop, ITextChannel channel, CancellationToken cancellationToken) {
            var policy = Policy
               .Handle<Exception>()
               .WaitAndRetry(
               [
                 TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(3),
                            TimeSpan.FromSeconds(7)
               ]);
            Task<ContractCoopStatusResponse> statusTask;

            if(!coop.UserCoopsXrefs.Any(x => x.JoinedCoop)) {
                statusTask = policy.Execute(async () => await EggIncApi.GetCoopStatusBot(coop.ContractID, coop.Name, _logger: _logger, cancellationToken: cancellationToken));
            } else if(coop.LastUpdateToChannel is null || coop.LastUpdateToChannel < DateTimeOffset.UtcNow.AddHours(-4)) {
                statusTask = policy.Execute(async () => await EggIncApi.GetCoopStatusBot(coop.ContractID, coop.Name, _logger: _logger, cancellationToken: cancellationToken));
            } else {
                var joinedUsers = coop.UserCoopsXrefs.Where(x => x.JoinedCoop).ToList();
                statusTask = policy.Execute(async () => await EggIncApi.GetCoopStatus(coop.ContractID, coop.Name, EIID: joinedUsers.ElementAt(rand.Next(joinedUsers.Count)).EggIncId, _logger: _logger, cancellationToken: cancellationToken));
            }
            var messageTask = GetDiscordMessages(channel, coop, cancellationToken);

            await Task.WhenAll(statusTask, messageTask);
            if(statusTask.Result is null) {
                _logger.LogWarning("Status task for {coop} is null, first time {first}", coop.Name, !coop.UserCoopsXrefs.Any(x => x.JoinedCoop));
            }

            return new StatusResponse {
                Status = statusTask.Result,
                DiscordMessages = messageTask.Result
            };
        }

        internal async Task UpdateChannel(List<string> msgs, Embed embed, IThreadChannel coopChannel, Coop coop, List<IMessage> existingMessages) {
            var sw = new Stopwatch();
            sw.Restart();
            var times = new List<long>();

            msgs = [.. msgs.Where(x => x != "")];

            msgs.Insert(0, "@@@EMBED");

            for(var i = msgs.Count; i < EstimateWorstCaseMessageSlots(coop.MaxUsers.GetValueOrDefault()); i++) {
                msgs.Add("឵");
            }
            if(string.IsNullOrWhiteSpace(coop.UpdateMessagesId)) {
                var UpdateMessagesID = new List<ulong>();
                var sentPosts = new List<IUserMessage>();
                foreach(var msg in msgs) {
                    IUserMessage post;
                    if(msg == "@@@EMBED") {
                        post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(embed: embed));
                    } else {
                        var msgCapture = msg;
                        post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(msgCapture));
                    }
                    UpdateMessagesID.Add(post.Id);
                    sentPosts.Add(post);
                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(UpdateMessagesID);
                var capturedChannel = coopChannel;
                var capturedPosts = sentPosts;
                _queue.EnqueueLow(async () => {
                    foreach(var p in capturedPosts) await p.PinAsync();
                    try {
                        var messages = await capturedChannel.GetMessagesAsync().FlattenAsync();
                        await capturedChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                    } catch(TimeoutException) {
                        var messages = await capturedChannel.GetMessagesAsync().FlattenAsync();
                        await capturedChannel.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                    }
                });
            } else {
                var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);
                var NewUpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);

                if(coopChannel != null) {

                    var pinnedMessages = false;
                    for(var i = 0; i < msgs.Count; i++) {
                        if(UpdateMessageIDs.Count > i) {
                            try {
                                var post = (RestUserMessage)existingMessages.FirstOrDefault(x => x.Id == UpdateMessageIDs[i]);
                                if(post == null) {
                                    if(msgs[i] == "@@@EMBED") {
                                        post = (RestUserMessage)await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(embed: embed));
                                    } else {
                                        var msgCapture = msgs[i];
                                        post = (RestUserMessage)await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(msgCapture));
                                    }
                                    NewUpdateMessageIDs.Remove(UpdateMessageIDs[i]);
                                    NewUpdateMessageIDs.Add(post.Id);
                                } else {
                                    var postCaptureModify = post;
                                    if(msgs[i] == "@@@EMBED") {
                                        _queue.EnqueueLow(() => postCaptureModify.ModifyWithTimeoutAsync(msg => { msg.Embed = embed; msg.Content = null; }));
                                    } else {
                                        var changes = post.Content.CompareChanges(msgs[i]);
                                        if(changes > 0) {
                                            var msgCapture = msgs[i];
                                            _queue.EnqueueLow(() => postCaptureModify.ModifyWithTimeoutAsync(msg => msg.Content = msgCapture));
                                        } else {
                                        }
                                    }
                                }
                                if(!post.IsPinned) {
                                    try {
                                        var postCapturePin = post;
                                        _queue.EnqueueLow(() => postCapturePin.PinAsync());
                                        pinnedMessages = true;
                                    } catch(JsonReaderException) {
                                        _logger.LogWarning("JsonReaderException when pinning message in coop {coop}", coop.Name);
                                    }
                                }
                            } catch(Discord.Net.HttpException httpEx) when(httpEx.DiscordCode == DiscordErrorCode.MissingPermissions) {
                                _logger.LogWarning("Missing permissions to update message in coop {coop}", coop.Name);
                            } catch(Exception e) {
                                _logger.LogError(e, "Error updating messages");
                                _bugSnag.Notify(e);
                            }
                        } else {
                            if(msgs[i] == "@@@EMBED") {
                                var post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(embed: embed));
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                var postCapture = post;
                                _queue.EnqueueLow(() => postCapture.PinAsync());
                            } else {
                                var msgCapture = msgs[i];
                                var post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(msgCapture));
                                NewUpdateMessageIDs.Add(post.Id);
                                pinnedMessages = true;
                                var postCapture = post;
                                _queue.EnqueueLow(() => postCapture.PinAsync());
                            }
                        }

                    }
                    if(pinnedMessages) {
                        var capturedCoopChannelForDelete = coopChannel;
                        _queue.EnqueueLow(async () => {
                            try {
                                var messages = await capturedCoopChannelForDelete.GetMessagesAsync().FlattenAsync();
                                await capturedCoopChannelForDelete.DeleteMessagesBatchAsync(messages.Where(x => x.Type == MessageType.ChannelPinnedMessage));
                            } catch(Discord.Net.HttpException httpEx) when(httpEx.DiscordCode == DiscordErrorCode.UnknownMessage) { }
                        });
                    }

                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(NewUpdateMessageIDs);
            }
        }

        public static List<string> GetStatusStringAsync(CoopDetails coopDetails, DBContract contract) {
            var table = new List<List<FixedWidthCell>> {new () {
                new($"{coopDetails.CoopParticipants.Count}/{contract.MaxUsers}"),
                new("Discord", CellAlignment.Center),
                new("EB", CellAlignment.Center),
                new("Total", CellAlignment.Center),
                new("Rate", CellAlignment.Center),
                new("📈", CellAlignment.Center),
                new("%", CellAlignment.Center),
                new("🟡", CellAlignment.Center, true),
                new("⏲️", CellAlignment.Center, true),
                new("Silo"),
                new(""),
            }};
            var everyoneJoined = coopDetails.CoopParticipants.All(x => x.CoopStatus is not null);

            table.AddRange(coopDetails.CoopParticipants.OrderByDescending(x => x.Projected).Select(x => {
                var sleeping = x.OfflineTime.TotalMinutes > x.SiloTimeMinutes ? "💤" : "";

                if(x.OfflineTime.TotalMinutes > x.SiloTimeMinutes) {
                    sleeping = $"💤 Empty Silos {x.OfflineTime.Add(TimeSpan.FromMinutes(0 - x.SiloTimeMinutes)).Humanize(maxUnit: TimeUnit.Hour).ShortenTime()}";
                }

                if(coopDetails.Coop.FinishedOrFailed())
                    sleeping = "";

                if(x.CoopStatus?.TimeCheatDetected ?? false)
                    sleeping += " ⏱️";

                sleeping = Truncate(sleeping, 24);


                var percent = coopDetails.GetProjectedShare(x);

                if(x.DBUser is null) {

                }

                return new List<FixedWidthCell> {
                    new(Truncate((everyoneJoined || x.DBUser is null ? "" : x.CoopStatus is not null ? "✅" : "❌") + (x.DBUser is null ? "👽" : "") + MyRegex().Replace(x.CoopStatus?.UserName ?? x.Backup?.UserName, ""), 11)),
                    new(Truncate(MyRegex().Replace(x.DiscordUser?.GetCleanName() ?? "", ""), 11)),
                    new(x.EarningsBonus.ToEggString(), CellAlignment.Right),
                    new(x.EggsShipped.ToEggString(), CellAlignment.Right),
                    new($"{(x.Rate * 3600).ToEggString()}/h", CellAlignment.Right),
                    new(x.Projected.ToEggString(), CellAlignment.Right),
                    new($"{Math.Round(percent)}%", CellAlignment.Right),
                    new(x.BoostTokens.ToString()),
                    new(x.OfflineTime.Humanize(maxUnit: TimeUnit.Hour).ShortenTime()),
                    new(TimeSpan.FromMinutes((double)x.SiloTimeMinutes).Humanize(2, maxUnit: TimeUnit.Hour).ShortenTime()),
                    new(sleeping),
                };
            }));



            return ChunkTableAtLimit(table, V1MessageContentCharBudget);
        }

        public static List<string> ChunkTableAtLimit(List<List<FixedWidthCell>> table, int limit) {
            var header = table[0];
            var columnWidths = ComputeColumnWidths(table);
            string Render(List<List<FixedWidthCell>> rows) => $"```{GetTable(rows, columnWidths)}```";

            var chunks = new List<string>();
            var current = new List<List<FixedWidthCell>> { header };
            foreach(var row in table.Skip(1)) {
                var candidate = new List<List<FixedWidthCell>>(current) { row };
                if(current.Count > 1 && Render(candidate).Length > limit) {
                    chunks.Add(Render(current));
                    current = [header, row];
                } else {
                    current.Add(row);
                }
            }
            chunks.Add(Render(current));

            return chunks;
        }

        public static List<string> ChunkAtDiscordMessageLimit(string text, int limit = 2000) {
            var msgs = new List<string>();

            while(text.Length > limit) {
                var index = text.LastIndexOf('\n', limit - 3);

                msgs.Add(text[..index] + "```");
                text = "```" + text[index..];
            }

            msgs.Add(text);

            return msgs;
        }
    }
}
