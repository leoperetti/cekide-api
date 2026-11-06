using CekideApi.Application.Abstractions;
using CekideApi.Application.Kiosks;
using CekideApi.Domain;
using CekideApi.UnitTests.Support;

namespace CekideApi.UnitTests;

public class KioskSessionServiceTests
{
    [Fact]
    public async Task Create_and_activate_return_raw_credentials_once_and_store_only_hashes()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.CreateAsync("Entrance", "cekide-app", "teacher-subject");

        Assert.Equal("pending", created.Session.Status);
        Assert.Equal(fixture.Clock.UtcNow.AddMinutes(10).UtcDateTime, created.ActivationExpiresAt);
        Assert.NotEqual(created.ActivationCode, fixture.Repository.Items.Single().ActivationCodeHash);
        Assert.Equal(KioskSessionService.Hash(created.ActivationCode), fixture.Repository.Items.Single().ActivationCodeHash);
        Assert.Null(fixture.Repository.Items.Single().TokenHash);

        var credential = await fixture.Service.ActivateAsync(created.ActivationCode);

        Assert.Equal(created.Session.Id, credential.SessionId);
        Assert.NotEqual(credential.Token, fixture.Repository.Items.Single().TokenHash);
        Assert.Equal(KioskSessionService.Hash(credential.Token), fixture.Repository.Items.Single().TokenHash);
        var secondUse = await Assert.ThrowsAsync<AttendanceException>(() =>
            fixture.Service.ActivateAsync(created.ActivationCode));
        Assert.Equal("invalid_activation_code", secondUse.Code);
    }

    [Fact]
    public async Task List_reports_derived_state_and_revocation_is_idempotent()
    {
        var fixture = new Fixture();
        var activation = await fixture.Service.CreateAsync("Entrance", "cekide-app", "teacher-subject");
        var session = fixture.Repository.Items.Single();
        await fixture.Service.ActivateAsync(activation.ActivationCode);

        var active = await fixture.Service.ListAsync("cekide-app");
        Assert.Equal("active", active.Single().Status);

        await fixture.Service.RevokeAsync(session.Id, "cekide-app", "teacher-subject");
        await fixture.Service.RevokeAsync(session.Id, "cekide-app", "another-subject");

        var revoked = await fixture.Service.ListAsync("cekide-app");
        Assert.Equal("revoked", revoked.Single().Status);
        Assert.Equal("teacher-subject", session.RevokedBy);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository = new InMemoryKioskSessionRepository();
            Service = new KioskSessionService(Repository, new FakeUnitOfWork(), Clock);
        }

        public TestClock Clock { get; } = new();
        public InMemoryKioskSessionRepository Repository { get; }
        public KioskSessionService Service { get; }
    }

    private sealed class InMemoryKioskSessionRepository : IKioskSessionRepository
    {
        private int _nextId = 1;
        public List<KioskSession> Items { get; } = [];

        public Task<KioskSession?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.Id == id));

        public Task<KioskSession?> GetByActivationCodeHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.ActivationCodeHash == hash));

        public Task<KioskSession?> GetByTokenHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.TokenHash == hash));

        public Task<IReadOnlyList<KioskSession>> ListForTenantAsync(string tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KioskSession>>(
                Items.Where(session => session.Tenant == tenant).OrderByDescending(session => session.Id).ToArray());

        public Task<bool> IsRevokedAsync(int id, string tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(session =>
                session.Id == id && session.Tenant == tenant && session.RevokedAt is not null));

        public void Add(KioskSession session)
        {
            EntityIds.Assign(session, _nextId++);
            Items.Add(session);
        }
    }
}
