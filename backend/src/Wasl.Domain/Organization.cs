using System.Security.Cryptography;

namespace Wasl.Domain;

public class Tenant : Entity
{
    private Tenant() { }

    public Tenant(string name, string code)
    {
        Name = name;
        Code = code.Trim().ToLowerInvariant();
        RegenerateIntakeKey();
    }

    public string Name { get; private set; } = default!;

    /// <summary>Typed on the login screen to choose the workspace.</summary>
    public string Code { get; private set; } = default!;

    /// <summary>Secret in the public web-to-lead URL. Rotating it cuts off old forms.</summary>
    public string IntakeKey { get; private set; } = default!;

    /// <summary>An open deal with no activity for this many days is flagged as going cold.</summary>
    public int StaleAfterDays { get; private set; } = 10;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public void RegenerateIntakeKey() =>
        IntakeKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void SetStaleAfterDays(int days)
    {
        if (days is < 1 or > 90) throw new DomainException("مدة الإهمال يجب أن تكون بين 1 و90 يومًا.");
        StaleAfterDays = days;
    }
}

public class User : Entity, ITenantOwned
{
    private User() { }

    public User(string fullName, string email, UserRole role)
    {
        FullName = fullName.Trim();
        Email = Text.NormalizeEmail(email)!;
        Role = role;
    }

    public Guid TenantId { get; set; }
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Whether automatic lead assignment may pick this person.</summary>
    public bool ReceivesLeads { get; private set; } = true;

    /// <summary>Breaks ties in lead assignment: the person who waited longest goes next.</summary>
    public DateTime? LastLeadAssignedAt { get; private set; }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void Update(string fullName, UserRole role, bool isActive, bool receivesLeads)
    {
        FullName = fullName.Trim();
        Role = role;
        IsActive = isActive;
        ReceivesLeads = receivesLeads;
    }

    public void MarkLeadAssigned() => LastLeadAssignedAt = DateTime.UtcNow;
}

public class Notification : Entity, ITenantOwned
{
    private Notification() { }

    public Notification(Guid userId, string title, string? link)
    {
        UserId = userId;
        Title = title;
        Link = link;
    }

    public Guid TenantId { get; set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;

    /// <summary>App route to open, e.g. "/deals/{id}".</summary>
    public string? Link { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public void MarkRead() => IsRead = true;
}
