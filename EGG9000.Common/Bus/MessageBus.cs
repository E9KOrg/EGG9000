using Microsoft.Extensions.DependencyInjection;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public interface IMessageBus {
        Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
    }

    public interface IBusHandler<in T> where T : class {
        Task HandleAsync(T message, CancellationToken ct);
    }

    public sealed class NullMessageBus : IMessageBus {
        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class => Task.CompletedTask;
    }

    public sealed record BusEnvelope(string Type, string Payload) {
        public const string HubPath = "/hubs/bus";
        public const string PublishMethod = "Publish";
        public const string ReceiveMethod = "Receive";
        public const string AuthHeader = "authenticationKey";

        public static BusEnvelope Wrap<T>(T message) where T : class => new(typeof(T).Name, JsonSerializer.Serialize(message));
    }

    public sealed record BusRoute(string Type, Func<IServiceProvider, string, CancellationToken, Task> Invoke);

    public static class BusServiceCollectionExtensions {
        public static IServiceCollection AddBusHandler<TMessage, THandler>(this IServiceCollection services) where TMessage : class where THandler : class, IBusHandler<TMessage> {
            services.AddScoped<THandler>();
            services.AddSingleton(new BusRoute(typeof(TMessage).Name, (provider, payload, ct) =>
                provider.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<TMessage>(payload), ct)));
            services.AddSingleton<BusDispatcher>();
            return services;
        }

        public static IServiceCollection AddSharedBusHandlers(this IServiceCollection services) => services
            .AddBusHandler<ExpireCacheMessage, ExpireCacheHandler>()
            .AddBusHandler<UpdateApiVersionsMessage, UpdateApiVersionsHandler>()
            .AddBusHandler<StorageDictionaryAdoptedMessage, StorageDictionaryAdoptedHandler>();
    }
}
