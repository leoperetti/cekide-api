using CekideApi.Application.Notifications;
using CekideApi.Application.Payments;
using CekideApi.Application.Plans;
using CekideApi.Application.Routines;
using CekideApi.Application.Students;
using Microsoft.Extensions.DependencyInjection;

namespace CekideApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<PlanService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<StudentService>();
        services.AddScoped<RoutineService>();
        services.AddScoped<OverdueNotificationService>();
        services.AddScoped<StudentLinkingService>();
        services.AddScoped<StudentInvitationService>();
        services.AddScoped<MyPaymentsService>();
        return services;
    }
}
