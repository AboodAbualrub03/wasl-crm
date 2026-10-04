namespace Wasl.Domain;

public record ScoreFactor(string Label, int Points);

public record LeadScore(int Total, IReadOnlyList<ScoreFactor> Factors);

/// <summary>
/// Rule-based lead score out of 100. Every point comes with a reason, so a rep can see
/// why one lead is ranked above another instead of trusting a black box.
/// </summary>
public static class LeadScoring
{
    private static readonly HashSet<string> PersonalDomains =
        ["gmail.com", "hotmail.com", "yahoo.com", "outlook.com", "live.com", "icloud.com"];

    public static LeadScore Calculate(Lead lead, int touchCount, DateTime now)
    {
        var factors = new List<ScoreFactor>();

        factors.Add(lead.Source switch
        {
            LeadSource.Referral => new("جاء عن طريق توصية", 25),
            LeadSource.Website => new("طلب من الموقع بنفسه", 20),
            LeadSource.Event => new("تعرّفنا عليه في فعالية", 15),
            LeadSource.Social => new("من وسائل التواصل", 10),
            _ => new("تواصل بارد أو مصدر آخر", 5)
        });

        if (lead.Email is { } email)
        {
            var domain = email[(email.IndexOf('@') + 1)..];
            factors.Add(PersonalDomains.Contains(domain)
                ? new("بريد شخصي", 5)
                : new("بريد على نطاق شركة", 15));
        }

        if (lead.Phone is not null) factors.Add(new("رقم هاتف متوفر", 10));
        if (lead.CompanyName is not null) factors.Add(new("يمثّل شركة", 10));

        if (lead.EstimatedValue is >= 10_000) factors.Add(new("قيمة متوقعة عالية", 20));
        else if (lead.EstimatedValue is >= 3_000) factors.Add(new("قيمة متوقعة متوسطة", 10));

        if (touchCount > 0) factors.Add(new($"تفاعل مسجل ({touchCount})", Math.Min(touchCount * 5, 15)));

        var age = now - lead.CreatedAt;
        if (age.TotalDays <= 7) factors.Add(new("جديد هذا الأسبوع", 5));
        else if (age.TotalDays > 30 && lead.LastContactedAt is null) factors.Add(new("أكثر من شهر بلا تواصل", -15));

        var total = Math.Clamp(factors.Sum(f => f.Points), 0, 100);
        return new LeadScore(total, factors);
    }
}
