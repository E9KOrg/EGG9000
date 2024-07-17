using MassTransit;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Mocks {
    public class PublishEndpointMock : IPublishEndpoint {
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) {
            throw new NotImplementedException();
        }

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class {
            return Task.CompletedTask;
        }
    }
}
