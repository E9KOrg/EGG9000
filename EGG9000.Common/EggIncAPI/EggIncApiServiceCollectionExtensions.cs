using EggSharp.Api;
using EggSharp.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Common.EggIncAPI {
    public static class EggIncApiServiceCollectionExtensions {
        public static IServiceCollection AddEggIncApi(this IServiceCollection services) {
            services.AddSingleton<IEggIncApiMetrics, EggIncApiMetrics>();
            services.AddEggSharpApi(ApiPresets.Egg9000, _ => new GoogleEiCodec(), _ => new AuxbrainSigner(EggIncApiSecrets.Salt), _ => EggIncApi.Versions);
            services.AddSingleton<IEggIncApi, EggIncApi>();
            return services;
        }
    }
}
