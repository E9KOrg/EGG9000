using System.Linq;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
[TestCategory("Network")]
public class ApiBackupGradeTests {

    private static string Eid => EggIncApi.UserId;

    [ClassInitialize]
    public static void InitSalt(TestContext _) {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a")
            .Build();
        SecretsHelper.Initialize(config);
    }

    [TestMethod]
    public async Task FirstContact_Succeeds() {
        var fc = await EggIncApi.FirstContact(Eid);
        Assert.IsTrue(fc.Success, $"first_contact failed for {Eid}: {fc.Error}");
        Assert.IsNotNull(fc.Backup, "first_contact succeeded but Backup was null");
    }

    [TestMethod]
    public async Task FirstContact_BackupStillContainsContracts() {
        var fc = await EggIncApi.FirstContact(Eid);
        Assert.IsTrue(fc.Success, $"first_contact failed: {fc.Error}");

        var my = fc.Backup.Contracts;
        if(my is not null) {
            TestContext.WriteLine($"active: {my.Contracts.Count}, archive: {my.Archive.Count}, last_cpi null: {my.LastCpi is null}");
        }

        Assert.IsNotNull(my, "Backup.Contracts is null; CustomBackup dereferences it and will throw");
    }

    [TestMethod]
    public async Task FirstContact_BackupCarriesPlayerGrade() {
        var fc = await EggIncApi.FirstContact(Eid);
        Assert.IsTrue(fc.Success, $"first_contact failed: {fc.Error}");

        var my = fc.Backup.Contracts;
        if(my is null) {
            Assert.Inconclusive("Backup.Contracts is null; no grade source to inspect");
            return;
        }

        var lastCpiGrade = my.LastCpi?.Grade ?? Ei.Contract.Types.PlayerGrade.GradeUnset;
        var perContract = my.Contracts.Concat(my.Archive)
            .Where(c => c is not null && c.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset)
            .Select(c => c.Grade)
            .ToList();

        TestContext.WriteLine($"last_cpi.grade: {lastCpiGrade}, contract grades: {perContract.Count}");

        Assert.IsTrue(lastCpiGrade != Ei.Contract.Types.PlayerGrade.GradeUnset || perContract.Count > 0,
            "No grade in the backup: last_cpi.grade unset and no contract carries a grade");
    }

    [TestMethod]
    public async Task GetContractPlayerInfo_ReturnsGrade() {
        if(!EggIncApiSecrets.IsSaltAvailable) {
            Assert.Inconclusive("API salt not configured; ei_ctx/get_contract_player_info is disabled");
            return;
        }

        var info = await EggIncApi.GetContractPlayerInfo(Eid);
        if(info is not null)
            TestContext.WriteLine($"grade: {info.Grade}, status: {info.Status}");

        Assert.IsNotNull(info, "get_contract_player_info returned null");
        Assert.AreNotEqual(Ei.Contract.Types.PlayerGrade.GradeUnset, info.Grade, "grade is unset");
    }

    public TestContext TestContext { get; set; }
}
