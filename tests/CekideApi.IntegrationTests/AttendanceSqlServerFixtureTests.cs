using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class AttendanceSqlServerFixtureTests
{
    [Fact]
    public async Task IndependentContextsShareCommittedDataAndCleanOnlyRegisteredIds()
    {
        await using var fixture = new AttendanceSqlServerFixture();
        await fixture.InitializeAsync();
        int id;
        await using (var first = fixture.CreateContext())
        {
            var plan = Plan.Create($"fixture-{Guid.NewGuid():N}", 100);
            first.Plans.Add(plan);
            await first.SaveChangesAsync();
            id = plan.Id;
            fixture.RegisterCleanup(db => db.Plans.Where(p => p.Id == id).ExecuteDeleteAsync());
        }

        await using (var second = fixture.CreateContext())
        {
            Assert.True(await second.Plans.AnyAsync(p => p.Id == id));
        }

        await fixture.CleanupAsync();
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.Plans.AnyAsync(p => p.Id == id));
    }

    [Fact]
    public void MissingConnectionFailsExplicitly()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AttendanceSqlServerFixture.ValidateConnectionString(null));
        Assert.Contains("CEKIDE_TEST_SQL_CONNECTION", error.Message);
    }

    [Theory]
    [InlineData("Server=localhost;Database=cekide-db;Integrated Security=True")]
    [InlineData("Server=localhost;Database=master;Integrated Security=True")]
    [InlineData("Server=remote.example.test;Database=cekide_attendance_test_local;Integrated Security=True")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=cekide-db;Integrated Security=True")]
    public void NonTestDatabaseIsRejected(string connection)
    {
        Assert.Throws<InvalidOperationException>(
            () => AttendanceSqlServerFixture.ValidateConnectionString(connection));
    }

    [Fact]
    public void LocalDbIsAllowed_only_for_dedicated_test_databases()
    {
        var connection = "Server=(localdb)\\MSSQLLocalDB;Database=cekide_attendance_test_local;Integrated Security=True";

        Assert.Contains("cekide_attendance_test_local",
            AttendanceSqlServerFixture.ValidateConnectionString(connection));
    }
}
