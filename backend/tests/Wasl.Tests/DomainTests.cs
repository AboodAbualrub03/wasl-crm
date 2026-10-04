using Wasl.Domain;

namespace Wasl.Tests;

public class DealTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Stage Lead = new("تواصل", 1, 10, StageKind.Open);
    private static readonly Stage Proposal = new("عرض", 2, 50, StageKind.Open);
    private static readonly Stage Won = new("ربحنا", 3, 100, StageKind.Won);
    private static readonly Stage Lost = new("خسرنا", 4, 0, StageKind.Lost);

    private static Deal NewDeal(decimal value = 5000, Guid? contact = null) =>
        new("نظام", value, Lead, Owner, null, contact ?? Guid.NewGuid(), null);

    [Fact]
    public void New_deal_must_start_in_an_open_stage()
    {
        Assert.Throws<DomainException>(() => new Deal("x", 100, Won, Owner, null, null, null));
    }

    [Fact]
    public void Moving_records_the_change_and_resets_stage_clock()
    {
        var deal = NewDeal();
        var change = deal.MoveTo(Proposal, Owner, null);

        Assert.Equal(Lead.Id, change.FromStageId);
        Assert.Equal(Proposal.Id, change.ToStageId);
        Assert.Equal(Proposal.Id, deal.StageId);
        Assert.Equal(DealStatus.Open, deal.Status);
    }

    [Fact]
    public void Cannot_win_without_a_value()
    {
        var deal = NewDeal(value: 0);
        Assert.Throws<DomainException>(() => deal.MoveTo(Won, Owner, null));
    }

    [Fact]
    public void Cannot_win_without_a_contact()
    {
        var deal = new Deal("نظام", 1000, Lead, Owner, null, null, null);
        Assert.Throws<DomainException>(() => deal.MoveTo(Won, Owner, null));
    }

    [Fact]
    public void Winning_closes_the_deal()
    {
        var deal = NewDeal();
        deal.MoveTo(Won, Owner, null);

        Assert.Equal(DealStatus.Won, deal.Status);
        Assert.NotNull(deal.ClosedAt);
    }

    [Fact]
    public void Losing_requires_a_reason()
    {
        var deal = NewDeal();
        Assert.Throws<DomainException>(() => deal.MoveTo(Lost, Owner, " "));
        deal.MoveTo(Lost, Owner, "السعر");
        Assert.Equal("السعر", deal.LostReason);
    }

    [Fact]
    public void Moving_a_lost_deal_back_reopens_it()
    {
        var deal = NewDeal();
        deal.MoveTo(Lost, Owner, "السعر");
        deal.MoveTo(Proposal, Owner, null);

        Assert.Equal(DealStatus.Open, deal.Status);
        Assert.Null(deal.ClosedAt);
        Assert.Null(deal.LostReason);
    }

    [Fact]
    public void A_won_deal_cannot_lose_its_contact_through_editing()
    {
        var deal = NewDeal();
        deal.MoveTo(Won, Owner, null);
        Assert.Throws<DomainException>(() => deal.SetDetails("نظام", 5000, null, null, null));
    }

    [Fact]
    public void Stale_means_open_and_silent_longer_than_the_threshold()
    {
        var deal = NewDeal();
        Assert.False(deal.IsStale(DateTime.UtcNow, 10));
        Assert.True(deal.IsStale(DateTime.UtcNow.AddDays(11), 10));

        deal.MoveTo(Lost, Owner, "x");
        Assert.False(deal.IsStale(DateTime.UtcNow.AddDays(30), 10));
    }
}

public class LeadTests
{
    [Fact]
    public void Contact_activity_moves_a_new_lead_to_contacted()
    {
        var lead = new Lead("سامي", LeadSource.Website);
        lead.RecordContact();

        Assert.Equal(LeadStatus.Contacted, lead.Status);
        Assert.NotNull(lead.LastContactedAt);
    }

    [Fact]
    public void Disqualifying_requires_a_reason_and_closes_the_lead()
    {
        var lead = new Lead("سامي", LeadSource.Website);
        Assert.Throws<DomainException>(() => lead.Disqualify(""));

        lead.Disqualify("لا احتياج");
        Assert.True(lead.IsClosed);
        Assert.Throws<DomainException>(() => lead.Qualify());
    }

    [Fact]
    public void Converting_needs_an_owner_and_is_final()
    {
        var lead = new Lead("سامي", LeadSource.Website);
        Assert.Throws<DomainException>(() => lead.MarkConverted(Guid.NewGuid()));

        lead.AssignTo(Guid.NewGuid());
        lead.MarkConverted(Guid.NewGuid());
        Assert.Equal(LeadStatus.Converted, lead.Status);
        Assert.Throws<DomainException>(() => lead.AssignTo(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("+970 59-123 4567", "591234567")]
    [InlineData("0591234567", "591234567")]
    [InlineData("059 123 4567", "591234567")]
    [InlineData("123", "123")]
    public void Phones_typed_differently_normalize_to_the_same_key(string input, string expected)
    {
        Assert.Equal(expected, Text.NormalizePhone(input));
    }
}

public class LeadScoringTests
{
    private static Lead Make(LeadSource source, string? email = null, string? phone = null, string? company = null, decimal? value = null)
    {
        var lead = new Lead("اسم", source);
        lead.Update("اسم", company, email, phone, null, value, source);
        return lead;
    }

    [Fact]
    public void Referral_with_business_email_and_budget_scores_high()
    {
        var lead = Make(LeadSource.Referral, "a@company.ps", "0599", "شركة", 20_000);
        var score = LeadScoring.Calculate(lead, 0, DateTime.UtcNow);

        Assert.True(score.Total >= 80, $"score was {score.Total}");
    }

    [Fact]
    public void Business_email_beats_personal_email()
    {
        var business = LeadScoring.Calculate(Make(LeadSource.Website, "a@firm.ps"), 0, DateTime.UtcNow).Total;
        var personal = LeadScoring.Calculate(Make(LeadSource.Website, "a@gmail.com"), 0, DateTime.UtcNow).Total;

        Assert.True(business > personal);
    }

    [Fact]
    public void Every_point_has_a_reason_and_the_total_matches()
    {
        var score = LeadScoring.Calculate(Make(LeadSource.Event, "x@y.ps", "1", "c", 5000), 2, DateTime.UtcNow);
        Assert.Equal(Math.Clamp(score.Factors.Sum(f => f.Points), 0, 100), score.Total);
        Assert.All(score.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
    }

    [Fact]
    public void Engagement_bonus_is_capped()
    {
        var lead = Make(LeadSource.Other);
        var three = LeadScoring.Calculate(lead, 3, DateTime.UtcNow).Total;
        var ten = LeadScoring.Calculate(lead, 10, DateTime.UtcNow).Total;
        Assert.Equal(three, ten);
    }

    [Fact]
    public void Old_untouched_lead_loses_points()
    {
        var lead = Make(LeadSource.Website, "a@firm.ps");
        var fresh = LeadScoring.Calculate(lead, 0, DateTime.UtcNow).Total;
        var stale = LeadScoring.Calculate(lead, 0, DateTime.UtcNow.AddDays(45)).Total;
        Assert.True(stale < fresh);
    }

    [Fact]
    public void Score_never_leaves_zero_to_hundred()
    {
        var low = LeadScoring.Calculate(Make(LeadSource.Other), 0, DateTime.UtcNow.AddDays(90));
        Assert.InRange(low.Total, 0, 100);
    }
}
