using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.Http;

namespace EGG9000.Common.EggIncAPI {
    public static class EggIncApiServiceCollectionExtensions {
        public static IServiceCollection AddEggIncApi(this IServiceCollection services) {
            services.AddHttpClient(EggIncApi.AndroidClient, EggIncApi.ConfigureAndroidClient).ConfigurePrimaryHttpMessageHandler(Handler);
            services.AddHttpClient(EggIncApi.IosClient, EggIncApi.ConfigureIosClient).ConfigurePrimaryHttpMessageHandler(Handler);
            services.AddHttpClient(EggIncApi.CoopStatusClient, EggIncApi.ConfigureCoopStatusClient).ConfigurePrimaryHttpMessageHandler(Handler);
            services.AddSingleton<IEggIncApi, EggIncApi>();
            return services;
        }

        private static HttpMessageHandler Handler() => new SocketsHttpHandler {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
    }
}
