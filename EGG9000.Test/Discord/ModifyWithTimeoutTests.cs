using Discord;

using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ModifyWithTimeoutTests {
        [TestMethod]
        public async Task ModifyWithTimeoutAsync_PropagatesModifyFailure() {
            var stub = new FailingModifyMessage(Guid.NewGuid().ToString("N"));
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => stub.ModifyWithTimeoutAsync(_ => { }));
        }

        [TestMethod]
        public async Task ModifyWithTimeoutAsync_DoesNotLeakUnobservedException() {
            var marker = Guid.NewGuid().ToString("N");
            var captured = new List<Exception>();
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) => {
                foreach(var inner in e.Exception.Flatten().InnerExceptions) {
                    if(inner.Message.Contains(marker)) {
                        lock(captured) captured.Add(inner);
                    }
                }
                e.SetObserved();
            };
            TaskScheduler.UnobservedTaskException += handler;
            try {
                await InvokeAndSwallowAsync(marker);
                for(var i = 0; i < 10; i++) {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(50);
                    lock(captured) if(captured.Count > 0) break;
                }
                lock(captured) Assert.AreEqual(0, captured.Count, "faulted ModifyAsync task escaped as UnobservedTaskException");
            } finally {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task InvokeAndSwallowAsync(string marker) {
            var stub = new FailingModifyMessage(marker);
            try {
                await stub.ModifyWithTimeoutAsync(_ => { });
            } catch(Exception) {
            }
        }

        private sealed class FailingModifyMessage(string marker) : IUserMessage {
            public Task ModifyAsync(Action<MessageProperties> func, RequestOptions? options = null) {
                return Task.FromException(new HttpRequestException("Resource temporarily unavailable (discord.com:443) " + marker));
            }

            ulong IEntity<ulong>.Id => 1;
            DateTimeOffset ISnowflakeEntity.CreatedAt => throw new NotImplementedException();
            Task IDeletable.DeleteAsync(RequestOptions options) => throw new NotImplementedException();

            Task IUserMessage.PinAsync(RequestOptions options) => throw new NotImplementedException();
            Task IUserMessage.UnpinAsync(RequestOptions options) => throw new NotImplementedException();
            Task IUserMessage.CrosspostAsync(RequestOptions options) => throw new NotImplementedException();
            string IUserMessage.Resolve(TagHandling userHandling, TagHandling channelHandling, TagHandling roleHandling, TagHandling everyoneHandling, TagHandling emojiHandling) => throw new NotImplementedException();
            Task IUserMessage.EndPollAsync(RequestOptions options) => throw new NotImplementedException();
            IAsyncEnumerable<IReadOnlyCollection<IUser>> IUserMessage.GetPollAnswerVotersAsync(uint answerId, int? limit, ulong? afterId, RequestOptions options) => throw new NotImplementedException();
            MessageResolvedData IUserMessage.ResolvedData => throw new NotImplementedException();
            IUserMessage IUserMessage.ReferencedMessage => throw new NotImplementedException();
            IMessageInteractionMetadata IUserMessage.InteractionMetadata => throw new NotImplementedException();
            IReadOnlyCollection<MessageSnapshot> IUserMessage.ForwardedMessages => throw new NotImplementedException();
            Poll? IUserMessage.Poll => throw new NotImplementedException();

            Task IMessage.AddReactionAsync(IEmote emote, RequestOptions options) => throw new NotImplementedException();
            Task IMessage.RemoveReactionAsync(IEmote emote, IUser user, RequestOptions options) => throw new NotImplementedException();
            Task IMessage.RemoveReactionAsync(IEmote emote, ulong userId, RequestOptions options) => throw new NotImplementedException();
            Task IMessage.RemoveAllReactionsAsync(RequestOptions options) => throw new NotImplementedException();
            Task IMessage.RemoveAllReactionsForEmoteAsync(IEmote emote, RequestOptions options) => throw new NotImplementedException();
            IAsyncEnumerable<IReadOnlyCollection<IUser>> IMessage.GetReactionUsersAsync(IEmote emoji, int limit, RequestOptions options, ReactionType type) => throw new NotImplementedException();
            MessageType IMessage.Type => throw new NotImplementedException();
            MessageSource IMessage.Source => throw new NotImplementedException();
            bool IMessage.IsTTS => throw new NotImplementedException();
            bool IMessage.IsPinned => throw new NotImplementedException();
            bool IMessage.IsSuppressed => throw new NotImplementedException();
            bool IMessage.MentionedEveryone => throw new NotImplementedException();
            string IMessage.Content => throw new NotImplementedException();
            string IMessage.CleanContent => throw new NotImplementedException();
            DateTimeOffset IMessage.Timestamp => throw new NotImplementedException();
            DateTimeOffset? IMessage.EditedTimestamp => throw new NotImplementedException();
            IMessageChannel IMessage.Channel => throw new NotImplementedException();
            IUser IMessage.Author => throw new NotImplementedException();
            IThreadChannel IMessage.Thread => throw new NotImplementedException();
            IReadOnlyCollection<IAttachment> IMessage.Attachments => throw new NotImplementedException();
            IReadOnlyCollection<IEmbed> IMessage.Embeds => throw new NotImplementedException();
            IReadOnlyCollection<ITag> IMessage.Tags => throw new NotImplementedException();
            IReadOnlyCollection<ulong> IMessage.MentionedChannelIds => throw new NotImplementedException();
            IReadOnlyCollection<ulong> IMessage.MentionedRoleIds => throw new NotImplementedException();
            IReadOnlyCollection<ulong> IMessage.MentionedUserIds => throw new NotImplementedException();
            MessageActivity IMessage.Activity => throw new NotImplementedException();
            MessageApplication IMessage.Application => throw new NotImplementedException();
            MessageReference IMessage.Reference => throw new NotImplementedException();
            IReadOnlyDictionary<IEmote, ReactionMetadata> IMessage.Reactions => throw new NotImplementedException();
            IReadOnlyCollection<IMessageComponent> IMessage.Components => throw new NotImplementedException();
            IReadOnlyCollection<IStickerItem> IMessage.Stickers => throw new NotImplementedException();
            MessageFlags? IMessage.Flags => throw new NotImplementedException();
            IMessageInteraction IMessage.Interaction => throw new NotImplementedException();
            MessageRoleSubscriptionData IMessage.RoleSubscriptionData => throw new NotImplementedException();
            PurchaseNotification IMessage.PurchaseNotification => throw new NotImplementedException();
            MessageCallData? IMessage.CallData => throw new NotImplementedException();
        }
    }
}
