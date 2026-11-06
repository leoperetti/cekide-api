using CekideApi.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests.Infrastructure;

public sealed class AttendanceSqlServerFixture : IAsyncLifetime, IAsyncDisposable
{
    public const string ConnectionVariable = "CEKIDE_TEST_SQL_CONNECTION";
    private readonly List<Func<CekideDbContext, Task>> _cleanup = [];
    private readonly DbContextOptions<CekideDbContext> _options;

    public AttendanceSqlServerFixture()
    {
        var connection = ValidateConnectionString(Environment.GetEnvironmentVariable(ConnectionVariable));
        _options = new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(connection).Options;
    }

    public FakeClock Clock { get; } = new();

    public static string ValidateConnectionString(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Set {ConnectionVariable} to a dedicated local test database.");
        }

        var parsed = new SqlConnectionStringBuilder(connection);
        var localDb = parsed.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase);
        if ((!string.Equals(parsed.DataSource, "localhost", StringComparison.OrdinalIgnoreCase) && !localDb) ||
            !parsed.InitialCatalog.StartsWith("cekide_attendance_test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionVariable} must use localhost or SQL Server LocalDB and a database named cekide_attendance_test_<suffix>.");
        }

        return parsed.ConnectionString;
    }

    public CekideDbContext CreateContext() => new(_options, Clock);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.OpenConnectionAsync();
    }

    public void RegisterCleanup(Func<CekideDbContext, Task> cleanup) => _cleanup.Add(cleanup);

    public async Task CleanupAsync()
    {
        await using var db = CreateContext();
        while (_cleanup.Count > 0)
        {
            var last = _cleanup.Count - 1;
            await _cleanup[last](db);
            _cleanup.RemoveAt(last);
        }
    }

    public async ValueTask DisposeAsync() => await CleanupAsync();

    Task IAsyncLifetime.DisposeAsync() => CleanupAsync();
}

[CollectionDefinition("AttendanceSqlServer")]
public sealed class AttendanceSqlServerCollection : ICollectionFixture<AttendanceSqlServerFixture>;

[CollectionDefinition("RecoverySqlServer")]
public sealed class RecoverySqlServerCollection : ICollectionFixture<AttendanceSqlServerFixture>;
