using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using CekideApi.Application;
using CekideApi.Authorization;
using CekideApi.Cors;
using CekideApi.Infrastructure;
using CekideApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Secrets (Jwt:Key, database, storage, Brevo) come from Key Vault in production; never from source control.
if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    var keyVaultName = builder.Configuration["KeyVault:Name"];
    if (!string.IsNullOrWhiteSpace(keyVaultName))
    {
        builder.Configuration.AddAzureKeyVault(new Uri($"https://{keyVaultName}.vault.azure.net/"), new DefaultAzureCredential());
    }
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddCekideAuthentication();
builder.Services.AddCekideAuthorization();
builder.Services.AddCekideCors();

var app = builder.Build();

app.Services.ValidateAuthenticationConfiguration();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
