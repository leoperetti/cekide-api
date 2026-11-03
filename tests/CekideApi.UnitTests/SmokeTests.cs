using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class SmokeTests
{
    [Fact]
    public void Domain_assembly_is_wired_into_the_unit_test_project()
    {
        Assert.Equal("CekideApi.Domain", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
