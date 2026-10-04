namespace Wasl.Domain;

public class Company : Entity, ITenantOwned
{
    private Company() { }

    public Company(string name, Guid ownerId)
    {
        Rename(name);
        OwnerId = ownerId;
    }

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = default!;
    public string? Industry { get; private set; }
    public string? City { get; private set; }
    public string? Website { get; private set; }
    public string? Phone { get; private set; }
    public Guid OwnerId { get; private set; }
    public User Owner { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("اسم الشركة مطلوب.");
        Name = name.Trim();
    }

    public void Update(string name, string? industry, string? city, string? website, string? phone, Guid ownerId)
    {
        Rename(name);
        Industry = Text.Clean(industry);
        City = Text.Clean(city);
        Website = Text.Clean(website);
        Phone = Text.Clean(phone);
        OwnerId = ownerId;
    }
}

public class Contact : Entity, ITenantOwned
{
    private Contact() { }

    public Contact(string fullName, Guid ownerId)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("اسم جهة الاتصال مطلوب.");
        FullName = fullName.Trim();
        OwnerId = ownerId;
    }

    public Guid TenantId { get; set; }
    public string FullName { get; private set; } = default!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }

    /// <summary>Digits-only phone used to catch duplicates typed in different formats.</summary>
    public string? PhoneKey { get; private set; }
    public string? JobTitle { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Company? Company { get; private set; }
    public Guid OwnerId { get; private set; }
    public User Owner { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public void Update(string fullName, string? email, string? phone, string? jobTitle, Guid? companyId, Guid ownerId)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("اسم جهة الاتصال مطلوب.");
        FullName = fullName.Trim();
        Email = Text.NormalizeEmail(email);
        Phone = Text.Clean(phone);
        PhoneKey = Text.NormalizePhone(phone);
        JobTitle = Text.Clean(jobTitle);
        CompanyId = companyId;
        OwnerId = ownerId;
    }
}

/// <summary>A potential customer who has not been qualified into a deal yet.</summary>
public class Lead : Entity, ITenantOwned
{
    private Lead() { }

    public Lead(string fullName, LeadSource source)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("اسم العميل المحتمل مطلوب.");
        FullName = fullName.Trim();
        Source = source;
        Status = LeadStatus.New;
    }

    public Guid TenantId { get; set; }
    public string FullName { get; private set; } = default!;
    public string? CompanyName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? PhoneKey { get; private set; }
    public string? Interest { get; private set; }
    public decimal? EstimatedValue { get; private set; }
    public LeadSource Source { get; private set; }
    public LeadStatus Status { get; private set; }

    /// <summary>0–100, recomputed by the scorer whenever the lead or its activity changes.</summary>
    public int Score { get; private set; }

    public Guid? OwnerId { get; private set; }
    public User? Owner { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? LastContactedAt { get; private set; }
    public string? DisqualifyReason { get; private set; }
    public Guid? ConvertedDealId { get; private set; }

    public bool IsClosed => Status is LeadStatus.Converted or LeadStatus.Disqualified;

    public void Update(string fullName, string? companyName, string? email, string? phone, string? interest, decimal? estimatedValue, LeadSource source)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("اسم العميل المحتمل مطلوب.");
        if (estimatedValue < 0) throw new DomainException("القيمة المتوقعة لا يمكن أن تكون سالبة.");

        FullName = fullName.Trim();
        CompanyName = Text.Clean(companyName);
        Email = Text.NormalizeEmail(email);
        Phone = Text.Clean(phone);
        PhoneKey = Text.NormalizePhone(phone);
        Interest = Text.Clean(interest);
        EstimatedValue = estimatedValue;
        Source = source;
    }

    public void AssignTo(Guid ownerId)
    {
        EnsureOpen();
        OwnerId = ownerId;
    }

    public void SetScore(int score) => Score = Math.Clamp(score, 0, 100);

    /// <summary>Any logged call, meeting or email moves a new lead to Contacted.</summary>
    public void RecordContact()
    {
        LastContactedAt = DateTime.UtcNow;
        if (Status == LeadStatus.New) Status = LeadStatus.Contacted;
    }

    public void Qualify()
    {
        EnsureOpen();
        Status = LeadStatus.Qualified;
    }

    public void Disqualify(string reason)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("سبب الاستبعاد مطلوب.");
        Status = LeadStatus.Disqualified;
        DisqualifyReason = reason.Trim();
    }

    public void Reopen()
    {
        if (Status != LeadStatus.Disqualified) throw new DomainException("يمكن إعادة فتح العملاء المستبعدين فقط.");
        Status = LeadStatus.Contacted;
        DisqualifyReason = null;
    }

    public void MarkConverted(Guid dealId)
    {
        EnsureOpen();
        if (OwnerId is null) throw new DomainException("يجب إسناد العميل المحتمل لموظف قبل تحويله.");
        Status = LeadStatus.Converted;
        ConvertedDealId = dealId;
    }

    private void EnsureOpen()
    {
        if (IsClosed) throw new DomainException("هذا العميل المحتمل مغلق (محوَّل أو مستبعد).");
    }
}
