using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wasl.Application;
using Wasl.Domain;

namespace Wasl.Infrastructure;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "wasl";
    public string Audience { get; set; } = "wasl-web";
    public string Key { get; set; } = default!;
    public int LifetimeMinutes { get; set; } = 600;
}

public static class AppClaims
{
    public const string Role = "role";
    public const string Tenant = "tid";
}

/// <summary>PBKDF2 from ASP.NET Core Identity, without pulling in the rest of Identity.</summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        _inner.VerifyHashedPassword(user, user.PasswordHash, password) != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed;
}

public class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    public (string Token, DateTime ExpiresAt) Issue(User user)
    {
        var o = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(o.LifetimeMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(AppClaims.Role, user.Role.ToString()),
                new Claim(AppClaims.Tenant, user.TenantId.ToString())
            ])
        });
        return (token, expires);
    }
}
