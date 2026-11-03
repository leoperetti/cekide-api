using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace CekideApi.Authorization;

public static class Roles
{
    public const string Owner = "owner";
    public const string Teacher = "teacher";
    public const string User = "user";
}

public static class Policies
{
    public const string OwnerOnly = "OwnerOnly";
    public const string StaffOnly = "StaffOnly";
    public const string StudentOnly = "StudentOnly";

    public static IServiceCollection AddCekideAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, TenantRequirementHandler>();

        services.AddAuthorization(options =>
        {
            var tenant = new TenantRequirement();

            AuthorizationPolicyBuilder Base() =>
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(tenant);

            options.DefaultPolicy = Base().Build();
            options.AddPolicy(OwnerOnly, Base().RequireRole(Roles.Owner).Build());
            options.AddPolicy(StaffOnly, Base().RequireRole(Roles.Owner, Roles.Teacher).Build());
            options.AddPolicy(StudentOnly, Base().RequireRole(Roles.User).Build());
        });

        return services;
    }
}

/// <summary>The JWT must have been issued for the gym tenant (claim "tenant").</summary>
public sealed class TenantRequirement : IAuthorizationRequirement;

public sealed class TenantRequirementHandler(IConfiguration configuration) : AuthorizationHandler<TenantRequirement>
{
    public const string DefaultTenantId = "cekide-app";
    public const string TenantClaim = "tenant";

    private readonly string _tenantId = configuration["Tenant:Id"] is { Length: > 0 } configured ? configured : DefaultTenantId;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRequirement requirement)
    {
        var tenant = context.User.FindFirstValue(TenantClaim);
        if (string.Equals(tenant, _tenantId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
