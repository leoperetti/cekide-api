using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;

if (args.Length != 1 || args[0] is not ("staging" or "production"))
    throw new ArgumentException("Expected staging or production.");

var staging = args[0] == "staging";
var database = staging ? "cekide-db-staging" : "cekide-db";
var otherDatabase = staging ? "cekide-db" : "cekide-db-staging";
var user = staging ? "cekide-api-stagi-id-a925" : "cekide-api-id-8230";
var clientId = staging
    ? "592310f3-be13-498a-bf41-eda2d8762698"
    : "acd40a92-c0c7-4bd7-bca0-60ae8b31bb1b";

using var tokenProcess = new Process
{
    StartInfo = new ProcessStartInfo("az")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        ArgumentList =
        {
            "account", "get-access-token", "--resource", "https://database.windows.net/",
            "--query", "accessToken", "--output", "tsv"
        }
    }
};
tokenProcess.Start();
var tokenOutput = tokenProcess.StandardOutput.ReadToEndAsync();
var tokenError = tokenProcess.StandardError.ReadToEndAsync();
await tokenProcess.WaitForExitAsync();
await tokenError;
if (tokenProcess.ExitCode != 0)
    throw new InvalidOperationException("Azure CLI could not obtain a SQL access token.");
var token = (await tokenOutput).Trim();
var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
if (claims.RootElement.GetProperty("appid").GetString() != clientId
    || claims.RootElement.GetProperty("tid").GetString() != "b7bd0c07-88dc-4c47-81ba-608fd7607a82")
    throw new InvalidOperationException("The Azure CLI token does not belong to the expected identity/tenant.");

SqlConnection CreateConnection(string target) => new(new SqlConnectionStringBuilder
{
    DataSource = "tcp:cekide.database.windows.net,1433",
    InitialCatalog = target,
    Encrypt = SqlConnectionEncryptOption.Mandatory,
    TrustServerCertificate = false,
    ConnectTimeout = 60,
    Pooling = false
}.ConnectionString) { AccessToken = token };

async Task OpenAsync(SqlConnection connection)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await connection.OpenAsync();
            return;
        }
        catch (SqlException error) when (error.Number is 40613 or 40615 && attempt < 6)
        {
            Console.WriteLine($"Database/firewall not ready ({error.Number}); retry {attempt}/5 in 20 seconds.");
            await Task.Delay(TimeSpan.FromSeconds(20));
        }
    }
}

await using (var connection = CreateConnection(database))
{
    await OpenAsync(connection);
    await using var command = connection.CreateCommand();
    command.CommandTimeout = 30;
    command.CommandText = """
        SELECT DB_NAME(), CURRENT_USER,
            HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'ALTER'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'REFERENCES'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'SELECT'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'INSERT'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'UPDATE'),
            HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'DELETE'),
            HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'),
            HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY USER'),
            IS_ROLEMEMBER('db_owner');
        """;
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || reader.GetString(0) != database || reader.GetString(1) != user)
        throw new InvalidOperationException("Unexpected database or SQL user.");
    for (var i = 2; i < 12; i++)
    {
        var expected = i < 9 ? 1 : 0;
        if (reader.IsDBNull(i) || reader.GetInt32(i) != expected)
            throw new InvalidOperationException($"Unexpected effective permission at column {i}.");
    }
    Console.WriteLine($"PASS: {user} connected to {database}; schema migration permissions confirmed without db_owner/user administration.");
}

await using (var connection = CreateConnection(otherDatabase))
{
    try
    {
        await OpenAsync(connection);
    }
    catch (SqlException error) when (error.Number is 18456 or 916 or 4060)
    {
        Console.WriteLine($"PASS: access to {otherDatabase} denied by SQL (error {error.Number}).");
        return;
    }
    throw new InvalidOperationException($"Isolation failure: identity can connect to {otherDatabase}.");
}
