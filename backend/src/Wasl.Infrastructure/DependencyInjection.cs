using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wasl.Application;
using Wasl.Infrastructure.Jobs;
using Wasl.Infrastructure.Persistence;

namespace Wasl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
            config.GetConnectionString("Default"),
            sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ILeadAssigner, SqlLeadAssigner>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));

        services.AddSingleton<FollowUpJob>();
        services.AddHostedService(sp => sp.GetRequiredService<FollowUpJob>());
        return services;
    }
}
