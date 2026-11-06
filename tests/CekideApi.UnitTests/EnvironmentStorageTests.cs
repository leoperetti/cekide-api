using CekideApi.Infrastructure.Storage;

namespace CekideApi.UnitTests;

public class EnvironmentStorageTests
{
    [Theory]
    [InlineData("Development", "cekidecontainer", "development/")]
    [InlineData("Staging", "cekidecontainer", "staging/")]
    [InlineData("Production", "cekidecontainer", "production/")]
    public void Environment_selects_the_shared_container_and_its_fixed_prefix(
        string environmentName,
        string expectedContainer,
        string expectedPrefix)
    {
        var target = EnvironmentStorageTarget.For(environmentName);

        Assert.Equal(expectedContainer, target.ContainerName);
        Assert.Equal(expectedPrefix, target.Prefix);
    }

    [Theory]
    [InlineData("Development", "students/17/routine.pdf", "development/students/17/routine.pdf")]
    [InlineData("Staging", "students/17/routine.pdf", "staging/students/17/routine.pdf")]
    [InlineData("Production", "students/17/routine.pdf", "production/students/17/routine.pdf")]
    public void Blob_name_preserves_relative_path_and_applies_one_environment_prefix(
        string environmentName,
        string relativePath,
        string expectedBlobName)
    {
        var target = EnvironmentStorageTarget.For(environmentName);

        Assert.Equal(expectedBlobName, target.GetBlobName(relativePath));
    }

    [Theory]
    [InlineData("/students/17/routine.pdf")]
    [InlineData("\\students\\17\\routine.pdf")]
    [InlineData("C:/students/17/routine.pdf")]
    [InlineData("../production/routine.pdf")]
    [InlineData("students/../production/routine.pdf")]
    [InlineData("students//17/routine.pdf")]
    [InlineData("development/students/17/routine.pdf")]
    [InlineData("staging/students/17/routine.pdf")]
    [InlineData("production/students/17/routine.pdf")]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_blob_paths_are_rejected(string path)
    {
        var target = EnvironmentStorageTarget.For("Development");

        Assert.Throws<ArgumentException>(() => { target.GetBlobName(path); });
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Preview")]
    [InlineData("")]
    public void Unsupported_environment_names_are_rejected(string environmentName)
    {
        Assert.Throws<ArgumentException>(() => { EnvironmentStorageTarget.For(environmentName); });
    }

    [Fact]
    public void Account_endpoint_must_be_https_and_cannot_embed_credentials()
    {
        Assert.Equal(
            "https://cekideblobaccount.blob.core.windows.net/",
            StorageOptions.ParseAccountUrl("https://cekideblobaccount.blob.core.windows.net").AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => { StorageOptions.ParseAccountUrl("http://localhost:10000/devstoreaccount1"); });
        Assert.Throws<InvalidOperationException>(() => { StorageOptions.ParseAccountUrl("https://account.blob.core.windows.net/?sig=secret"); });
        Assert.Throws<InvalidOperationException>(() => { StorageOptions.ParseAccountUrl(null); });
    }

    [Fact]
    public void Azurite_is_restricted_to_testing()
    {
        var options = new StorageOptions { AzuriteConnectionString = "UseDevelopmentStorage=true" };

        Assert.Throws<InvalidOperationException>(() => { AzureBlobRoutineStorage.CreateAzurite(options, "Development"); });
    }

    [Fact]
    public void Azurite_requires_an_explicit_test_connection_string()
    {
        var options = new StorageOptions();

        Assert.Throws<InvalidOperationException>(() => { AzureBlobRoutineStorage.CreateAzurite(options, "Testing"); });
    }
}
