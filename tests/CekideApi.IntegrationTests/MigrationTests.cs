using CekideApi.Infrastructure.Persistence;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CekideApi.IntegrationTests;

/// <summary>Guards that the SQL Server migrations always describe the current EF model.</summary>
public class MigrationTests
{
    private static CekideDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>()
            .UseSqlServer("Server=(localdb)\\unused;Database=cekide-db;Trusted_Connection=True")
            .Options, new FakeClock());

    [Fact]
    public void The_database_has_migrations()
    {
        using var context = SqlServerContext();

        var migrations = context.GetService<IMigrationsAssembly>().Migrations;

        Assert.NotEmpty(migrations);
    }

    [Fact]
    public void The_model_snapshot_matches_the_current_model()
    {
        using var context = SqlServerContext();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(((IMutableModel)snapshot.Model).FinalizeModel());
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        Assert.Empty(differences);
    }
}
