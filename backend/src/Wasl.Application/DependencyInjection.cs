using Microsoft.Extensions.DependencyInjection;
using Wasl.Application.Activities;
using Wasl.Application.Customers;
using Wasl.Application.Deals;
using Wasl.Application.Insights;
using Wasl.Application.Leads;
using Wasl.Application.Workspace;

namespace Wasl.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<TeamService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<LeadService>();
        services.AddScoped<DealService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<ReportService>();
        services.AddScoped<HomeService>();
        services.AddScoped<SearchService>();
        return services;
    }
}
