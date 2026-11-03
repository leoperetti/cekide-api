using CekideApi.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CekideApi.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef`; reads CekideDatabaseConnectionString from the environment or falls back to LocalDB.</summary>
public sealed class CekideDbContextFactory : IDesignTimeDbContextFactory<CekideDbContext>
{
    private const string LocalDb = "Server=(localdb)\\MSSQLLocalDB;Database=cekide-db;Trusted_Connection=True;TrustServerCertificate=True";

    public CekideDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DependencyInjection.ConnectionStringKey) ?? LocalDb;
        var options = new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(connectionString).Options;
        return new CekideDbContext(options, new SystemClock());
    }
}
