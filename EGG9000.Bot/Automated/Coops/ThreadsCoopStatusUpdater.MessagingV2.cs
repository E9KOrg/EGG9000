using Discord;
using Discord.Rest;
using EGG9000.Common.Database.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.DiscordHelpersExt;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        public static List<string> PackTextChunksForComponentsV2(IEnumerable<string> chunks, int budget = ComponentsV2Safe.TextDisplayMax) {
            var messages = new List<string>();
            var current = new List<string>();
            var remaining = budget;

            foreach(var chunk in chunks) {
                var content = chunk.Length > budget ? chunk[..budget] : chunk;
                var neededChars = content.Length + (current.Count > 0 ? 1 : 0);
                if(current.Count > 0 && neededChars > remaining) {
                    messages.Add(string.Join("\n", current));
                    current = [];
                    remaining = budget;
                    neededChars = content.Length;
                }
                current.Add(content);
                remaining -= neededChars;
            }
            if(current.Count > 0) {
                messages.Add(string.Join("\n", current));
            }

            return messages;
        }

        internal static MessageComponent BuildV2StatusComponent(string text) =>
            new ComponentBuilderV2()
                .AddComponent(new ContainerBuilder().WithTextDisplaySafe(text))
                .Build();

        internal async Task UpdateChannelV2(List<string> msgs, string headerText, IThreadChannel coopChannel, Coop coop, List<IMessage> existingMessages) {
            msgs = [.. msgs.Where(x => x != "")];

            var contentChunks = new List<string>();
            if(!string.IsNullOrEmpty(headerText)) contentChunks.Add(headerText);
            contentChunks.AddRange(msgs);

            var packed = PackTextChunksForComponentsV2(contentChunks);
            for(var i = packed.Count; i < EstimateWorstCaseMessageSlotsV2(coop.MaxUsers.GetValueOrDefault()); i++) {
                packed.Add("឵");
            }

            if(string.IsNullOrWhiteSpace(coop.UpdateMessagesId)) {
                var updateMessagesID = new List<ulong>();
                var sentPosts = new List<IUserMessage>();
                foreach(var text in packed) {
                    var post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(components: BuildV2StatusComponent(text), flags: MessageFlags.ComponentsV2));
                    updateMessagesID.Add(post.Id);
                    sentPosts.Add(post);
                }
                coop.UpdateMessagesId = JsonConvert.SerializeObject(updateMessagesID);
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
                var updateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);
                var newUpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId);

                if(coopChannel != null) {
                    var pinnedMessages = false;
                    for(var i = 0; i < packed.Count; i++) {
                        if(updateMessageIDs.Count > i) {
                            try {
                                var post = (RestUserMessage)existingMessages.FirstOrDefault(x => x.Id == updateMessageIDs[i]);
                                if(post == null) {
                                    var newPost = (RestUserMessage)await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(components: BuildV2StatusComponent(packed[i]), flags: MessageFlags.ComponentsV2));
                                    newUpdateMessageIDs.Remove(updateMessageIDs[i]);
                                    newUpdateMessageIDs.Add(newPost.Id);
                                } else {
                                    post.ThrowIfModeMismatch(sendingComponentsV2: true);
                                    var postCaptureModify = post;
                                    var componentCapture = BuildV2StatusComponent(packed[i]);
                                    _queue.EnqueueLow(() => postCaptureModify.ModifyWithTimeoutAsync(msg => { msg.Components = componentCapture; msg.Flags = MessageFlags.ComponentsV2; msg.Content = null; msg.Embed = null; }));
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
                            var post = await _queue.EnqueueLowAsync(() => coopChannel.SendMessageAsync(components: BuildV2StatusComponent(packed[i]), flags: MessageFlags.ComponentsV2));
                            newUpdateMessageIDs.Add(post.Id);
                            pinnedMessages = true;
                            var postCapture = post;
                            _queue.EnqueueLow(() => postCapture.PinAsync());
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
                coop.UpdateMessagesId = JsonConvert.SerializeObject(newUpdateMessageIDs);
            }
        }
    }
}
