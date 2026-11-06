using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

const string tenant = "b7bd0c07-88dc-4c47-81ba-608fd7607a82";
const string endpoint = "https://cekideblobaccount.blob.core.windows.net";

if (args.Length == 3 && args[0] == "new-certificate")
{
    var privatePath = Path.GetFullPath(args[1]);
    var publicPath = Path.GetFullPath(args[2]);
    var repository = Path.GetFullPath(Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar;
    if (privatePath.StartsWith(repository, StringComparison.OrdinalIgnoreCase)
        || File.Exists(privatePath) || File.Exists(publicPath))
        throw new InvalidOperationException("Certificate must be new and outside the repository.");
    using var rsa = RSA.Create(3072);
    var request = new CertificateRequest("CN=cekide-blob-development", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(6));
    File.WriteAllText(privatePath, certificate.ExportCertificatePem() + rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, certificate.ExportCertificatePem());
    Console.WriteLine($"Development certificate created outside repository; expires {certificate.NotAfter.ToUniversalTime():O}. No private material printed.");
    return;
}

if (args.Length is < 1 or > 2 || args[0] is not ("development" or "staging" or "production")
    || (args.Length == 2 && args[1] != "--rest-check"))
    throw new ArgumentException("Expected environment [--rest-check], or new-certificate <private-pem> <public-pem>.");

var environment = args[0];
var expectedPrincipal = environment switch
{
    "development" => "d4c38e0f-5597-4ce4-b6b7-ccb19ac49aae",
    "staging" => "446b2b59-9468-4814-b273-a15a8b44bcbc",
    _ => "a71db939-b848-448f-b0e1-f11adfb616d0"
};
if (environment != "development"
    && Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") !=
        (environment == "staging" ? "cekide-api-staging" : "cekide-api"))
    throw new InvalidOperationException("Azure probes must run inside their assigned App Service.");
if (environment == "development"
    && (Environment.GetEnvironmentVariable("AZURE_TENANT_ID") != tenant
        || !Guid.TryParse(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"), out _)
        || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_CLIENT_CERTIFICATE_PATH"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"))))
    throw new InvalidOperationException("Development requires the dedicated tenant/client certificate environment, without a client secret.");
var options = new BlobClientOptions();
options.Retry.MaxRetries = 2;
options.Retry.Delay = TimeSpan.FromSeconds(2);
var credential = environment == "development"
    ? (Azure.Core.TokenCredential)new EnvironmentCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
var identityToken = await credential.GetTokenAsync(
    new Azure.Core.TokenRequestContext(["https://storage.azure.com/.default"]), default);
var tokenParts = identityToken.Token.Split('.');
if (tokenParts.Length != 3)
    throw new InvalidOperationException("Storage identity token is not in the expected format.");
var payload = tokenParts[1].Replace('-', '+').Replace('_', '/');
payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
using (var claims = JsonDocument.Parse(Convert.FromBase64String(payload)))
{
    if (!claims.RootElement.TryGetProperty("oid", out var principal)
        || principal.GetString() != expectedPrincipal
        || !claims.RootElement.TryGetProperty("tid", out var tokenTenant)
        || tokenTenant.GetString() != tenant)
        throw new InvalidOperationException("Storage token does not belong to the assigned environment identity and tenant.");
}
Console.WriteLine($"PASS: Storage token belongs to the expected {environment} principal and tenant. Token not printed.");
if (args.Length == 2)
{
    var token = await credential.GetTokenAsync(new Azure.Core.TokenRequestContext(["https://storage.azure.com/.default"]), default);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    using var request = new HttpRequestMessage(HttpMethod.Get,
        $"{endpoint}/cekidecontainer?restype=container&comp=list&prefix={environment}%2F_abac-probe%2F&maxresults=1");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
    request.Headers.Add("x-ms-version", "2023-11-03");
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var body = System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync());
        throw new InvalidOperationException($"Independent REST check failed: HTTP {(int)response.StatusCode}, code {body.Root?.Element("Code")?.Value}.");
    }
    Console.WriteLine("PASS: independent REST own synthetic-prefix list with API 2023-11-03.");
    return;
}
var container = new BlobContainerClient(new Uri($"{endpoint}/cekidecontainer"), credential, options);
var run = Guid.NewGuid().ToString("N");
var ownName = $"{environment}/_abac-probe/{run}.txt";
var ownBlob = container.GetBlobClient(ownName);
var created = new HashSet<BlobClient>();
var failures = new List<string>();
var checks = 0;

async Task ExpectDenied(Func<Task> action, string label)
{
    try { await action(); }
    catch (RequestFailedException error) when (error.Status == 403 && error.ErrorCode == "AuthorizationPermissionMismatch")
    {
        checks++;
        Console.WriteLine($"PASS: {label} denied by Blob authorization.");
        return;
    }
    failures.Add($"{label}: expected AuthorizationPermissionMismatch/403.");
}

try
{
    Console.WriteLine($"Synthetic probe ID: {run}");
    var previousProbes = 0;
    await foreach (var unused in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{environment}/_abac-probe/", default))
        previousProbes++;
    Console.WriteLine($"Own synthetic-prefix list succeeded; existing probe count: {previousProbes}.");
    created.Add(ownBlob);
    await ownBlob.UploadAsync(BinaryData.FromString("Synthetic ABAC probe. No business data."), overwrite: false);
    var download = await ownBlob.DownloadContentAsync();
    if (download.Value.Content.ToString() != "Synthetic ABAC probe. No business data.")
        throw new InvalidOperationException("Own synthetic probe content mismatch.");
    await ownBlob.UploadAsync(BinaryData.FromString("Updated synthetic probe."), overwrite: true);
    var names = new List<string>();
    await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{environment}/_abac-probe/{run}", default))
        names.Add(item.Name);
    if (names.Count != 1 || names[0] != ownName)
        throw new InvalidOperationException("Own-prefix listing did not return exactly the synthetic probe.");
    checks += 4;
    Console.WriteLine("PASS: own-prefix create, read, overwrite and list.");

    foreach (var other in new[] { "development", "staging", "production" }.Where(value => value != environment))
    {
        var foreign = container.GetBlobClient($"{other}/_abac-probe/{run}.txt");
        await ExpectDenied(async () => { await foreign.DownloadContentAsync(); }, $"{other} read");
        await ExpectDenied(async () =>
        {
            await foreign.UploadAsync(BinaryData.FromString("Synthetic unexpected-write cleanup target."), overwrite: false);
            created.Add(foreign);
        }, $"{other} create");
        await ExpectDenied(async () =>
        {
            await foreign.UploadAsync(BinaryData.FromString("Synthetic unexpected-overwrite cleanup target."), overwrite: true);
            created.Add(foreign);
        }, $"{other} overwrite");
        await ExpectDenied(async () => { await foreign.DeleteIfExistsAsync(); }, $"{other} delete");
        await ExpectDenied(async () =>
        {
            await foreach (var unused in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{other}/_abac-probe/{run}", default)) { break; }
        }, $"{other} list");
    }
    foreach (var prefix in new[] { "", environment, $"{environment}-other/" })
    {
        await ExpectDenied(async () =>
        {
            await foreach (var unused in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, default)) { break; }
        }, "root or boundary-prefix list");
    }
    await ownBlob.DeleteAsync();
    created.Remove(ownBlob);
    checks++;
    Console.WriteLine("PASS: own-prefix delete.");
}
finally
{
    foreach (var blob in created)
    {
        try { await blob.DeleteIfExistsAsync(); }
        catch (RequestFailedException error)
        {
            failures.Add($"Synthetic probe cleanup failed: HTTP {error.Status}, code {error.ErrorCode}. Probe ID: {run}.");
        }
    }
    foreach (var failure in failures)
        Console.Error.WriteLine(failure);
}
if (failures.Count != 0)
    throw new InvalidOperationException($"{failures.Count} ABAC checks/cleanup operations failed. Probe ID: {run}.");
Console.WriteLine($"PASS: {checks} Blob ABAC checks for {environment}. No business blobs accessed.");
