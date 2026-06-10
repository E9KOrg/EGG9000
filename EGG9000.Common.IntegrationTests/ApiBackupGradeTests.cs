using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using EGG9000.Common.Database;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

// Verifies what the live Egg Inc API actually returns for a known-good EID. A field existing in
// the proto does not guarantee the API still populates it, so these tests assert on real responses
// to confirm which grade sources survive before any code relies on them.
[TestClass]
[TestCategory("Network")]
public class ApiBackupGradeTests {

    private static string Eid => EggIncApi.UserId;

    // Load the API salt from the bot's user-secrets store so the salt-gated
    // get_contract_player_info endpoint is exercised the same way it runs in prod.
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
        Assert.IsTrue(fc.Success, $"first_contact failed for {Eid}: {fc.Error}. "
            + "Shipped ClientVersion/AppVersion/AppBuild may be stale.");
        Assert.IsNotNull(fc.Backup, "first_contact succeeded but Backup was null.");
    }

    // If the API drops `contracts` from backups, Backup.Contracts (MyContracts, proto field 13)
    // is null - which also NPEs the CustomBackup ctor, since it dereferences backup.Contracts.
    [TestMethod]
    public async Task FirstContact_BackupStillContainsContracts() {
        var fc = await EggIncApi.FirstContact(Eid);
        Assert.IsTrue(fc.Success, $"first_contact failed: {fc.Error}");

        var my = fc.Backup.Contracts;
        TestContext.WriteLine($"Backup.Contracts (MyContracts) null? {my is null}");
        if(my is not null) {
            TestContext.WriteLine($"  Contracts (active): {my.Contracts.Count}");
            TestContext.WriteLine($"  Archive: {my.Archive.Count}");
            TestContext.WriteLine($"  LastCpi null? {my.LastCpi is null}");
        }

        Assert.IsNotNull(my, "Backup.Contracts is null - the API no longer returns `contracts` in "
            + "backups. CustomBackup ctor dereferences backup.Contracts and will NPE.");
    }

    // Does any grade survive in the backup? Two candidate sources:
    //   - MyContracts.last_cpi.grade
    //   - LocalContract.grade per active/archived contract (used by GetGrade's contract inference)
    [TestMethod]
    public async Task FirstContact_BackupCarriesPlayerGrade() {
        var fc = await EggIncApi.FirstContact(Eid);
        Assert.IsTrue(fc.Success, $"first_contact failed: {fc.Error}");

        var my = fc.Backup.Contracts;
        if(my is null) {
            Assert.Inconclusive("Backup.Contracts is null; no grade source to inspect. "
                + "See FirstContact_BackupStillContainsContracts.");
            return;
        }

        var lastCpiGrade = my.LastCpi?.Grade ?? Ei.Contract.Types.PlayerGrade.GradeUnset;
        var perContract = my.Contracts.Concat(my.Archive)
            .Where(c => c is not null)
            .Select(c => c.Grade)
            .Where(g => g != Ei.Contract.Types.PlayerGrade.GradeUnset)
            .ToList();

        TestContext.WriteLine($"last_cpi.grade: {lastCpiGrade}");
        TestContext.WriteLine($"LocalContract grades present (non-unset): {perContract.Count}");
        if(perContract.Count > 0)
            TestContext.WriteLine($"  most recent-ish grades: {string.Join(", ", perContract.Take(5))}");

        var anyGrade = lastCpiGrade != Ei.Contract.Types.PlayerGrade.GradeUnset || perContract.Count > 0;
        Assert.IsTrue(anyGrade, "No grade survives anywhere in the backup (last_cpi.grade unset AND "
            + "no LocalContract carries a grade). Grade cannot be recovered from first_contact.");
    }

    // Exercises the salt-gated get_contract_player_info endpoint: skips locally, runs where the salt
    // is configured (prod/CI). Asserts the endpoint returns a populated grade.
    [TestMethod]
    public async Task GetContractPlayerInfo_ReturnsGrade() {
        if(!EggIncApiSecrets.IsSaltAvailable) {
            Assert.Inconclusive("API salt not configured; ei_ctx/get_contract_player_info is "
                + "disabled locally. Run where 'egg_inc_api_salt' is set to reproduce prod.");
            return;
        }

        var info = await EggIncApi.GetContractPlayerInfo(Eid);
        TestContext.WriteLine($"GetContractPlayerInfo null? {info is null}");
        if(info is not null)
            TestContext.WriteLine($"grade: {info.Grade}, status: {info.Status}, total_cxp: {info.TotalCxp}");

        Assert.IsNotNull(info, "get_contract_player_info returned null - endpoint is dead or changed, "
            + "so LastGrade can no longer be sourced from it.");
        Assert.AreNotEqual(Ei.Contract.Types.PlayerGrade.GradeUnset, info.Grade,
            "get_contract_player_info returned but grade is unset.");
    }

    public TestContext TestContext { get; set; }
}
