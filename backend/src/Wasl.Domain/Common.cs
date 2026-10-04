namespace Wasl.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>Belongs to one customer company (tenant). Filtered and stamped automatically by the DbContext.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>A broken business rule. The API returns it as 422 with this message.</summary>
public class DomainException(string message) : Exception(message);

public enum UserRole
{
    Rep = 1,
    Manager = 2,
    Admin = 3
}

public enum LeadSource
{
    Website = 1,
    Referral = 2,
    Social = 3,
    Event = 4,
    ColdCall = 5,
    Other = 6
}

public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Disqualified = 4,
    Converted = 5
}

public enum StageKind
{
    Open = 1,
    Won = 2,
    Lost = 3
}

public enum DealStatus
{
    Open = 1,
    Won = 2,
    Lost = 3
}

public enum ActivityType
{
    Call = 1,
    Meeting = 2,
    Email = 3,
    Task = 4,
    Note = 5,
    /// <summary>Written by the system, e.g. "moved to Negotiation".</summary>
    System = 6
}

public static class Text
{
    /// <summary>Keeps digits only and the last 9 of them, so "+970 59-123 4567" and "0591234567" match.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length <= 9 ? digits : digits[^9..];
    }

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
