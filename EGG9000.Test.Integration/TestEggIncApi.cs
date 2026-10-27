using EGG9000.Common.EggIncAPI;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Test.Integration;

public static class TestEggIncApi {
    public static IEggIncApi Create() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEggIncApi();
        return services.BuildServiceProvider().GetRequiredService<IEggIncApi>();
    }
}
