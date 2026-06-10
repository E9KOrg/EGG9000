using EGG9000.Common.EggIncAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
[TestCategory("Network")]
public class ApiPeriodicalsTests {
    [TestMethod]
    public async Task Periodicals_ReturnsContractsForShippedVersions() {
        var resp = await EggIncApi.GetPeriodicalsAsync();
        Assert.IsTrue(EggIncApi.IsValidPeriodicalsResponse(resp),
            "Periodicals returned no contracts. Shipped ClientVersion/AppVersion/AppBuild may be stale, "
            + "or auxbrain rejected the request.");
    }
}
