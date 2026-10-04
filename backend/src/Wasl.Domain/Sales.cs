namespace Wasl.Domain;

/// <summary>A column of the pipeline. Each workspace configures its own.</summary>
public class Stage : Entity, ITenantOwned
{
    private Stage() { }

    public Stage(string name, int order, int probability, StageKind kind)
    {
        Rename(name);
        Order = order;
        SetProbability(probability);
        Kind = kind;
    }

    public Guid TenantId { get; set; }
    public string Name { get; private set; } = default!;
    public int Order { get; private set; }

    /// <summary>Chance a deal in this stage closes as won; drives the weighted forecast.</summary>
    public int Probability { get; private set; }
    public StageKind Kind { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("اسم المرحلة مطلوب.");
        Name = name.Trim();
    }

    public void SetProbability(int probability)
    {
        if (probability is < 0 or > 100) throw new DomainException("الاحتمالية بين 0 و100.");
        Probability = probability;
    }

    public void MoveTo(int order) => Order = order;
}

public class Deal : Entity, ITenantOwned
{
    private Deal() { }

    public Deal(string title, decimal value, Stage stage, Guid ownerId, Guid? companyId, Guid? contactId, DateTime? expectedClose)
    {
        if (stage.Kind != StageKind.Open) throw new DomainException("الصفقة الجديدة تبدأ في مرحلة مفتوحة.");
        SetDetails(title, value, companyId, contactId, expectedClose);
        StageId = stage.Id;
        OwnerId = ownerId;
        Status = DealStatus.Open;
        StageEnteredAt = LastActivityAt = DateTime.UtcNow;
    }

    public Guid TenantId { get; set; }
    public string Title { get; private set; } = default!;
    public decimal Value { get; private set; }
    public Guid StageId { get; private set; }
    public Stage Stage { get; private set; } = default!;
    public DealStatus Status { get; private set; }
    public Guid OwnerId { get; private set; }
    public User Owner { get; private set; } = default!;
    public Guid? CompanyId { get; private set; }
    public Company? Company { get; private set; }
    public Guid? ContactId { get; private set; }
    public Contact? Contact { get; private set; }
    public DateTime? ExpectedClose { get; private set; }
    public string? LostReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime StageEnteredAt { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>Set when the owner was warned the deal is going cold; cleared by new activity.</summary>
    public DateTime? StaleNotifiedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = default!;

    public void SetDetails(string title, decimal value, Guid? companyId, Guid? contactId, DateTime? expectedClose)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("عنوان الصفقة مطلوب.");
        if (value < 0) throw new DomainException("قيمة الصفقة لا يمكن أن تكون سالبة.");
        if (Status == DealStatus.Won && (value <= 0 || contactId is null))
            throw new DomainException("الصفقة الرابحة تحتاج قيمة وجهة اتصال.");

        Title = title.Trim();
        Value = value;
        CompanyId = companyId;
        ContactId = contactId;
        ExpectedClose = expectedClose;
    }

    public void Reassign(Guid ownerId) => OwnerId = ownerId;

    /// <summary>
    /// Moves the deal to another stage and returns the change for the history.
    /// Winning needs a value and a contact; losing needs a reason; moving back to an open stage reopens.
    /// </summary>
    public DealStageChange MoveTo(Stage target, Guid byUserId, string? lostReason)
    {
        if (target.Id == StageId) throw new DomainException("الصفقة في هذه المرحلة أصلًا.");

        switch (target.Kind)
        {
            case StageKind.Won:
                if (Value <= 0) throw new DomainException("حدد قيمة الصفقة قبل تسجيلها كرابحة.");
                if (ContactId is null) throw new DomainException("اربط جهة اتصال بالصفقة قبل تسجيلها كرابحة.");
                Status = DealStatus.Won;
                ClosedAt = DateTime.UtcNow;
                LostReason = null;
                break;
            case StageKind.Lost:
                if (string.IsNullOrWhiteSpace(lostReason)) throw new DomainException("سبب الخسارة مطلوب.");
                Status = DealStatus.Lost;
                ClosedAt = DateTime.UtcNow;
                LostReason = lostReason.Trim();
                break;
            default:
                Status = DealStatus.Open;
                ClosedAt = null;
                LostReason = null;
                break;
        }

        var change = new DealStageChange(Id, StageId, target.Id, byUserId) { TenantId = TenantId };
        StageId = target.Id;
        Stage = target;
        StageEnteredAt = DateTime.UtcNow;
        Touch();
        return change;
    }

    /// <summary>Any activity on the deal keeps it warm.</summary>
    public void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
        StaleNotifiedAt = null;
    }

    public bool IsStale(DateTime now, int staleAfterDays) =>
        Status == DealStatus.Open && LastActivityAt < now.AddDays(-staleAfterDays);

    public void MarkStaleNotified() => StaleNotifiedAt = DateTime.UtcNow;
}

/// <summary>Append-only history of stage moves; the funnel and cycle-time reports are built from it.</summary>
public class DealStageChange : Entity, ITenantOwned
{
    private DealStageChange() { }

    public DealStageChange(Guid dealId, Guid? fromStageId, Guid toStageId, Guid changedById)
    {
        DealId = dealId;
        FromStageId = fromStageId;
        ToStageId = toStageId;
        ChangedById = changedById;
    }

    public Guid TenantId { get; set; }
    public Guid DealId { get; private set; }
    public Guid? FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public Guid ChangedById { get; private set; }
    public DateTime At { get; private set; } = DateTime.UtcNow;
}

/// <summary>A call, meeting, email, task or note, attached to a deal, contact, company or lead.</summary>
public class Activity : Entity, ITenantOwned
{
    private Activity() { }

    public Activity(ActivityType type, string subject, Guid ownerId, Guid createdById)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new DomainException("عنوان النشاط مطلوب.");
        Type = type;
        Subject = subject.Trim();
        OwnerId = ownerId;
        CreatedById = createdById;
        // Logged calls, meetings, emails and notes already happened; tasks are to-dos.
        if (type != ActivityType.Task) CompletedAt = CreatedAt;
    }

    public Guid TenantId { get; set; }
    public ActivityType Type { get; private set; }
    public string Subject { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTime? DueAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid OwnerId { get; private set; }
    public User Owner { get; private set; } = default!;
    public Guid CreatedById { get; private set; }
    public Guid? DealId { get; private set; }
    public Deal? Deal { get; private set; }
    public Guid? ContactId { get; private set; }
    public Contact? Contact { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Company? Company { get; private set; }
    public Guid? LeadId { get; private set; }
    public Lead? Lead { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ReminderSentAt { get; private set; }

    public bool IsDone => CompletedAt is not null;

    public Activity WithNotes(string? notes)
    {
        Notes = Text.Clean(notes);
        return this;
    }

    public Activity Due(DateTime? dueAt)
    {
        DueAt = dueAt;
        return this;
    }

    public Activity About(Guid? dealId, Guid? contactId, Guid? companyId, Guid? leadId)
    {
        if (dealId is null && contactId is null && companyId is null && leadId is null)
            throw new DomainException("النشاط يجب أن يرتبط بصفقة أو جهة اتصال أو شركة أو عميل محتمل.");
        DealId = dealId;
        ContactId = contactId;
        CompanyId = companyId;
        LeadId = leadId;
        return this;
    }

    public void Complete()
    {
        if (IsDone) throw new DomainException("تم إنجاز هذه المهمة مسبقًا.");
        CompletedAt = DateTime.UtcNow;
    }

    public void Reopen()
    {
        if (Type != ActivityType.Task) throw new DomainException("يمكن إعادة فتح المهام فقط.");
        CompletedAt = null;
        ReminderSentAt = null;
    }

    public bool IsOverdue(DateTime now) => !IsDone && DueAt is { } due && due < now;

    public void MarkReminderSent() => ReminderSentAt = DateTime.UtcNow;
}
