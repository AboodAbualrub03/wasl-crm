using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wasl.Application;
using Wasl.Domain;
using Wasl.Infrastructure;

namespace Wasl.Api.Infrastructure;

/// <summary>The signed-in user, read from the JWT of the current request.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid UserId => Guid.Parse(Claim("sub"));
    public Guid TenantId => Guid.Parse(Claim(AppClaims.Tenant));
    public UserRole Role => Enum.Parse<UserRole>(Claim(AppClaims.Role));

    private string Claim(string type) =>
        Principal?.FindFirstValue(type) ?? throw new UnauthorizedAccessException($"Missing claim {type}.");
}

/// <summary>
/// One error shape for the whole API (RFC 7807). Business errors carry an Arabic message the UI
/// shows as is; anything unexpected is logged and replaced with a generic message.
/// </summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "يجب تسجيل الدخول."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "عدّل شخص آخر هذه الصفقة للتو. حدّث الصفحة وحاول مجددًا."),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (StatusCodes.Status409Conflict, "القيمة مستخدمة مسبقًا."),
            BadHttpRequestException e => (e.StatusCode, "طلب غير صالح."),
            _ => (StatusCodes.Status500InternalServerError, "حدث خطأ غير متوقع.")
        };

        if (status >= 500) logger.LogError(exception, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title }
        });
    }
}

public static class RateLimits
{
    public const string Login = "login";
    public const string Intake = "intake";
}
