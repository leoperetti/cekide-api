using CekideApi.Application.Notifications;
using CekideApi.Application.Payments;
using CekideApi.Application.Plans;
using CekideApi.Application.Routines;
using CekideApi.Application.Students;
using CekideApi.Application.Attendance;
using CekideApi.Application.Kiosks;
using CekideApi.Application.Shifts;
using CekideApi.Application.Recoveries;
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
        services.AddScoped<AttendanceQuotaNotificationService>();
        services.AddScoped<StudentLinkingService>();
        services.AddScoped<StudentInvitationService>();
        services.AddScoped<MyPaymentsService>();
        services.AddScoped<AttendanceCheckInService>();
        services.AddScoped<AttendanceCalendarService>();
        services.AddScoped<KioskSessionService>();
        services.AddScoped<ShiftService>();
        services.AddScoped<RecoveryAccrualService>();
        services.AddScoped<RecoveryStateService>();
        services.AddScoped<RecoveryAvailabilityService>();
        services.AddScoped<RecoveryReservationService>();
        return services;
    }
}
