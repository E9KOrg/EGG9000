using Discord;
using Discord.Rest;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Services;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class HeaderChannelWebhook {
        public ulong WebhookId { get; set; }
        public string WebhookToken { get; set; }
    }

    public class CoopMessageSender(ICoopCreationQueue queue, IServiceProvider provider, ILogger<CoopMessageSender> logger) {
        private readonly ICoopCreationQueue _queue = queue;
        private readonly IServiceProvider _provider = provider;
        private readonly ILogger<CoopMessageSender> _logger = logger;
        // Lane is keyed off the thread's own id rather than a shared in-process counter or a
        // per-message sequence number. A counter resets on every restart and is shared across
        // all concurrently-sending coops (not reproducible); a per-message sequence alternates
        // lanes *within* a single thread, which is exactly what we don't want - a thread should
        // stay on whichever lane its first message landed on for its whole lifetime, so every
        // later edit/append against that thread keeps using a matching webhook (or bot) identity.
        // Keying off thread.Id is deterministic and never changes for a given thread, so every
        // call for that thread - this batch or a future one - resolves to the same lane.
        internal static bool IsWebhookLane(ulong threadId) => threadId % 2 == 1;

        public async Task<TrackedMessage> SendAsync(IThreadChannel thread, HeaderChannelWebhook webhookInfo, string content = null, Embed embed = null) {
            if(webhookInfo != null && IsWebhookLane(thread.Id)) {
                try {
                    var messageId = await SendViaWebhookAsync(thread.Id, webhookInfo, content, embed);
                    return new TrackedMessage(messageId, webhookInfo.WebhookId);
                } catch(Exception ex) {
                    _logger.LogWarning(ex, "Webhook send failed for thread {thread}, falling back to bot lane", thread.Id);
                }
            }
            var botMessageId = await SendViaBotAsync(thread, content, embed);
            return new TrackedMessage(botMessageId, null);
        }

        public async Task<TrackedMessage> EditAsync(IThreadChannel thread, TrackedMessage tracked, HeaderChannelWebhook webhookInfo, string content = null, Embed embed = null) {
            if(tracked.WebhookId != null) {
                if(webhookInfo == null || webhookInfo.WebhookId != tracked.WebhookId) {
                    _logger.LogWarning("Cannot edit message {messageId} in thread {thread}: no matching webhook info supplied for webhook {webhookId}, replacing with a new message", tracked.MessageId, thread.Id, tracked.WebhookId);
                    return await ReplaceStaleMessageAsync(thread, tracked, content, embed);
                }
                try {
                    await EditViaWebhookAsync(thread.Id, tracked.MessageId, webhookInfo, content, embed);
                    return tracked;
                } catch(Exception ex) {
                    _logger.LogWarning(ex, "Webhook edit failed for message {messageId} in thread {thread}, the webhook may be dead, replacing with a new message", tracked.MessageId, thread.Id);
                    return await ReplaceStaleMessageAsync(thread, tracked, content, embed);
                }
            }
            await EditViaBotAsync(thread, tracked.MessageId, content, embed);
            return tracked;
        }

        // Self-heals a message whose webhook lane has died (webhook deleted/invalidated, or the
        // TrackedMessage's WebhookId no longer matches the header channel's current webhook). The
        // underlying Discord message still exists, only the token used to edit it is dead, so the
        // normal "message no longer exists, resend" path never triggers on its own. Best-effort
        // delete the stale message, then post a fresh replacement.
        //
        // Forces the bot lane directly rather than going through SendAsync's normal lane
        // selection: we already know the webhook lane just failed, so re-rolling it here would,
        // for coops whose other tracked messages share that same dead webhook, keep re-picking
        // the webhook lane and repeating this same delete-and-resend on every status update until
        // the roll happens to land on the bot lane.
        private async Task<TrackedMessage> ReplaceStaleMessageAsync(IThreadChannel thread, TrackedMessage tracked, string content, Embed embed) {
            try {
                await _queue.EnqueueAsync(async () => {
                    await thread.DeleteMessageAsync(tracked.MessageId);
                    return true;
                }, tag: "CoopMessageSender.DeleteStale");
            } catch(Exception ex) {
                _logger.LogWarning(ex, "Best-effort delete of stale message {messageId} in thread {thread} failed (message may already be gone)", tracked.MessageId, thread.Id);
            }
            var botMessageId = await SendViaBotAsync(thread, content, embed);
            return new TrackedMessage(botMessageId, null);
        }

        private Task<ulong> SendViaWebhookAsync(ulong threadId, HeaderChannelWebhook webhookInfo, string content, Embed embed) {
            return _queue.EnqueueAsync(async () => {
                using var client = new DiscordWebhookClient(webhookInfo.WebhookId, webhookInfo.WebhookToken);
                return await client.SendMessageAsync(text: content, embeds: embed != null ? [embed] : null, threadId: threadId);
            }, tag: "CoopMessageSender.SendViaWebhook");
        }

        private Task<ulong> SendViaBotAsync(IThreadChannel thread, string content, Embed embed) {
            return _queue.EnqueueAsync(async () => {
                var message = await thread.SendMessageAsync(text: content, embed: embed);
                return message.Id;
            }, tag: "CoopMessageSender.SendViaBot");
        }

        private Task EditViaWebhookAsync(ulong threadId, ulong messageId, HeaderChannelWebhook webhookInfo, string content, Embed embed) {
            return _queue.EnqueueAsync(async () => {
                using var client = new DiscordWebhookClient(webhookInfo.WebhookId, webhookInfo.WebhookToken);
                await client.ModifyMessageAsync(messageId, props => {
                    props.Content = content;
                    if(embed != null) props.Embeds = new[] { embed };
                }, threadId: threadId);
                return true;
            }, tag: "CoopMessageSender.EditViaWebhook");
        }

        private Task EditViaBotAsync(IThreadChannel thread, ulong messageId, string content, Embed embed) {
            return _queue.EnqueueAsync(async () => {
                var message = (IUserMessage)await thread.GetMessageAsync(messageId);
                if(message == null) return false;
                await message.ModifyWithTimeoutAsync(props => {
                    props.Content = content;
                    if(embed != null) props.Embed = embed;
                });
                return true;
            }, tag: "CoopMessageSender.EditViaBot");
        }
    }
}
