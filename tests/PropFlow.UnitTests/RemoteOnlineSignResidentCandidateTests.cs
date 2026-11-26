using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Storage;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.UnitTests;

public sealed class RemoteOnlineSignResidentCandidateTests
{
    [Fact]
    public async Task Search_returns_only_active_linked_accounts_with_resident_role_and_preserves_user_id()
    {
        var eligibleUser = Guid.NewGuid(); var eligibleResident = Guid.NewGuid();
        var inactiveUser = Guid.NewGuid(); var noRoleUser = Guid.NewGuid(); var missingUser = Guid.NewGuid();
        var items = new[]
        {
            new ResidentAccountScopeItem(eligibleResident, eligibleUser, "RS-01", "Nguyen Van An", "an@example.test", true, ["A-101"]),
            new ResidentAccountScopeItem(Guid.NewGuid(), inactiveUser, "RS-02", "Tran Thi Binh", "binh@example.test", true, []),
            new ResidentAccountScopeItem(Guid.NewGuid(), noRoleUser, "RS-03", "Le Cuong", "cuong@example.test", true, []),
            new ResidentAccountScopeItem(Guid.NewGuid(), missingUser, "RS-04", "Pham Dung", "dung@example.test", true, []),
            new ResidentAccountScopeItem(Guid.NewGuid(), null, "RS-05", "Unlinked", null, true, []),
            new ResidentAccountScopeItem(Guid.NewGuid(), Guid.NewGuid(), "RS-06", "Inactive profile", null, false, [])
        };
        await using var fixture = new Fixture(items, id => id == eligibleUser ? "ACTIVE" : id == inactiveUser ? "LOCKED" : id == noRoleUser ? "ACTIVE" : null,
            id => id == eligibleUser);

        var result = await fixture.Service.SearchEligibleResidentsAsync("Nguyen", default);

        var candidate = Assert.Single(result);
        Assert.Equal(eligibleUser, candidate.UserId);
        Assert.Equal(eligibleResident, candidate.ResidentId);
        Assert.Equal("RS-01", candidate.ResidentCode);
        Assert.Equal(["A-101"], candidate.Apartments);
    }

    [Theory]
    [InlineData("Nguyen")]
    [InlineData("RS-01")]
    [InlineData("an@example.test")]
    public async Task Search_uses_resident_scope_name_code_and_email_contract(string term)
    {
        var user = Guid.NewGuid();
        var item = new ResidentAccountScopeItem(Guid.NewGuid(), user, "RS-01", "Nguyen Van An", "an@example.test", true, []);
        await using var fixture = new Fixture([item], _ => "ACTIVE", _ => true);
        Assert.Single(await fixture.Service.SearchEligibleResidentsAsync(term, default));
    }

    [Fact]
    public async Task Search_rejects_term_longer_than_one_hundred_characters()
    {
        await using var fixture = new Fixture([], _ => "ACTIVE", _ => true);
        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.SearchEligibleResidentsAsync(new string('x', 101), default));
        Assert.Equal("resident_search_invalid", error.Code);
    }

    [Fact]
    public async Task Empty_search_returns_only_eligible_candidates_and_is_limited_to_twenty()
    {
        var users = Enumerable.Range(1, 25).Select(_ => Guid.NewGuid()).ToArray();
        var items = users.Select((user, index) => new ResidentAccountScopeItem(Guid.NewGuid(), user, $"RS-{index:000}", $"Resident {index}", null, true, [])).ToArray();
        await using var fixture = new Fixture(items, _ => "ACTIVE", _ => true);

        var candidates = await fixture.Service.SearchEligibleResidentsAsync("   ", default);

        Assert.Equal(20, candidates.Count);
        Assert.All(candidates, candidate => Assert.Contains(candidate.UserId, users));
    }

    [Fact]
    public async Task Assign_revalidates_candidate_when_eligibility_changes_after_search()
    {
        var user = Guid.NewGuid(); var enabled = true;
        var item = new ResidentAccountScopeItem(Guid.NewGuid(), user, "RS-01", "Nguyen Van An", null, true, []);
        await using var fixture = new Fixture([item], _ => "ACTIVE", _ => enabled);
        var candidate = Assert.Single(await fixture.Service.SearchEligibleResidentsAsync("Nguyen", default));
        var owner = Guid.NewGuid();
        var contract = new PropFlow.Modules.RemoteOnlineSign.Domain.RemoteContract { CreatorId = owner, SigningMode = PropFlow.Modules.RemoteOnlineSign.Domain.RemoteSigningMode.PRE_SIGNED_INVESTOR };
        fixture.Db.Contracts.Add(contract); await fixture.Db.SaveChangesAsync();
        enabled = false;
        var error = await Assert.ThrowsAsync<RemoteContractException>(() => fixture.Service.AssignAsync(owner, contract.Id, new(candidate.UserId), default));
        Assert.Equal("resident_not_eligible", error.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public RemoteOnlineSignDbContext Db { get; } = new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        public RemoteContractService Service { get; }
        public Fixture(IReadOnlyList<ResidentAccountScopeItem> items, Func<Guid, string?> status, Func<Guid, bool> role)
        {
            Service = new(Db, new Storage(), new Otp(), new PdfSignatureStamper(), NullLogger<RemoteContractService>.Instance,
                new RemoteTestAccountAccess(role), new RemoteTestAccountDirectory(status), new RemoteTestResidentScope(role, items), new RemoteTestContractCodes());
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Storage : IRemotePdfStorage
    {
        public Task<StoredPdf> PutOriginalAsync(Guid id, Stream s, long l, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredPdf> PutDerivedAsync(Guid id, string p, Stream s, long l, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string k, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsWithHashAsync(string k, string h, CancellationToken ct) => Task.FromResult(false);
        public Task DeleteIfUnreferencedAsync(string k, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Otp : IPrivilegedActionOtp
    {
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid u,string p,string h,CancellationToken ct)=>throw new NotSupportedException();
        public Task VerifyAsync(Guid u,Guid c,string p,string h,string code,CancellationToken ct)=>throw new NotSupportedException();
    }
}
