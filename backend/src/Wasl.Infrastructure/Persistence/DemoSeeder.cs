using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Wasl.Application;
using Wasl.Domain;

namespace Wasl.Infrastructure.Persistence;

/// <summary>
/// A believable workspace: a software house selling systems to local businesses, with five months
/// of history so the board, the forecast and the reports have something real to show.
/// A second, tiny workspace exists to prove tenants never see each other.
/// </summary>
public static class DemoSeeder
{
    public const string Password = "Demo@12345";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(ct)) return;
        new Workspace(db, hasher, DateTime.UtcNow).SeedNour();
        new Workspace(db, hasher, DateTime.UtcNow).SeedOfuq();
        await db.SaveChangesAsync(ct);
    }

    private sealed class Workspace(AppDbContext db, IPasswordHasher hasher, DateTime now)
    {
        private Tenant _tenant = default!;
        private List<Stage> _stages = [];

        private Stage Open(int i) => _stages.Where(s => s.Kind == StageKind.Open).OrderBy(s => s.Order).ElementAt(i);
        private Stage Won => _stages.First(s => s.Kind == StageKind.Won);
        private Stage Lost => _stages.First(s => s.Kind == StageKind.Lost);
        private DateTime Ago(double days) => now.AddDays(-days);

        public void SeedNour()
        {
            Tenant("نور للحلول البرمجية", "nour");
            Pipeline();

            User("علاء ربيع", "admin@nour.test", UserRole.Admin, receivesLeads: false);
            var lian = User("ليان منصور", "manager@nour.test", UserRole.Manager, receivesLeads: false);
            var karim = User("كريم داود", "karim@nour.test", UserRole.Rep);
            var salma = User("سلمى عيسى", "salma@nour.test", UserRole.Rep);
            var basel = User("باسل حمدان", "basel@nour.test", UserRole.Rep);

            var yasmin = Company("مطاعم الياسمين", "مطاعم", "رام الله", karim, 160);
            var shifa = Company("صيدليات الشفاء", "صيدليات", "نابلس", salma, 170);
            var ofuq = Company("مدارس الأفق الأهلية", "تعليم", "الخليل", basel, 90);
            var rahma = Company("مستشفى الرحمة التخصصي", "رعاية صحية", "جنين", salma, 100);
            var adala = Company("مكتب العدالة للمحاماة", "خدمات قانونية", "رام الله", karim, 45);
            var nukhba = Company("معارض النخبة للسيارات", "سيارات", "نابلس", basel, 110);
            var qamar = Company("فندق القمر", "ضيافة", "بيت لحم", karim, 75);
            var bayan = Company("مجموعة البيان التجارية", "تجارة وتوزيع", "الخليل", salma, 65);
            var riyada = Company("شركة الريادة للتأمين", "تأمين", "رام الله", basel, 60);
            var smile = Company("عيادات الابتسامة لطب الأسنان", "رعاية صحية", "طولكرم", salma, 20);
            var zaytouna = Company("مصنع الزيتونة للمنظفات", "صناعة", "جنين", karim, 55);
            var bakery = Company("مخابز الحارة", "أغذية", "قلقيلية", basel, 3);

            var c1 = Contact("مازن الأحمد", "مدير العمليات", "mazen@yasmin-rest.ps", "0599 120 340", yasmin, karim);
            var c2 = Contact("رنا عبد الهادي", "المديرة المالية", "rana@alshifa-pharma.ps", "0598 331 200", shifa, salma);
            Contact("فادي سليم", "مسؤول المشتريات", "fadi@alshifa-pharma.ps", "0597 441 900", shifa, salma);
            var c3 = Contact("أ. منير خليل", "مدير المدارس", "munir@ofuq-schools.ps", "0569 210 455", ofuq, basel);
            var c4 = Contact("د. هالة يوسف", "المديرة الطبية", "hala@rahma-hospital.ps", "0599 800 121", rahma, salma);
            Contact("سامي عياد", "رئيس قسم تكنولوجيا المعلومات", "sami.it@rahma-hospital.ps", "0598 800 122", rahma, salma);
            var c5 = Contact("المحامي وائل سمارة", "الشريك المدير", "wael@adala-law.ps", "0597 555 010", adala, karim);
            var c6 = Contact("جورج حنا", "مدير المبيعات", "george@nukhba-cars.ps", "0599 610 777", nukhba, basel);
            var c7 = Contact("ديما قسيس", "مديرة الفندق", "dima@alqamar-hotel.ps", "0568 300 912", qamar, karim);
            var c8 = Contact("طارق البيان", "المدير العام", "tareq@albayan-group.ps", "0599 700 300", bayan, salma);
            var c9 = Contact("لمى ناصر", "مديرة المطالبات", "lama@riyada-ins.ps", "0598 222 640", riyada, basel);
            var c10 = Contact("د. إياد عمر", "المالك", "iyad@smile-dental.ps", "0597 131 313", smile, salma);
            var c11 = Contact("حسن الزيتاوي", "مدير المصنع", "hasan@zaytouna-ind.ps", "0599 450 450", zaytouna, karim);
            var c12 = Contact("أبو صالح", "صاحب المخابز", null, "0592 404 404", bakery, basel);

            // Open pipeline: (deal, stage index reached, created, last touch, expected close)
            var d1 = OpenDeal("نظام نقاط بيع لـ 6 فروع", 18_000, yasmin, c1, karim, 2, 25, 2, 20);
            var d2 = OpenDeal("نظام إدارة الصيدليات والمخزون", 24_000, shifa, c2, salma, 3, 40, 1, 10);
            var d3 = OpenDeal("منصة تعليم إلكتروني وتطبيق أولياء الأمور", 32_000, ofuq, c3, basel, 1, 12, 3, 45);
            var d4 = OpenDeal("نظام مواعيد وملفات المرضى", 45_000, rahma, c4, salma, 2, 30, 13, 30);
            var d5 = OpenDeal("موقع إلكتروني وأتمتة إدارة القضايا", 6_500, adala, c5, karim, 0, 5, 5, 60);
            var d6 = OpenDeal("تطبيق حجز تجربة قيادة", 15_000, nukhba, c6, basel, 3, 35, 16, 5);
            var d7 = OpenDeal("نظام حجوزات وإدارة غرف الفندق", 21_000, qamar, c7, karim, 1, 9, 1, 40);
            var d8 = OpenDeal("ERP للمستودعات والتوزيع", 60_000, bayan, c8, salma, 0, 4, 4, 90);
            var d9 = OpenDeal("بوابة مطالبات التأمين الإلكترونية", 38_000, riyada, c9, basel, 2, 22, 6, 25);
            var d10 = OpenDeal("تطبيق مواعيد لعيادات الأسنان", 9_000, smile, c10, salma, 1, 15, 11, 35);
            var d11 = OpenDeal("نظام طلبات التوزيع للمندوبين", 27_000, zaytouna, c11, karim, 3, 50, 4, 3);
            var d12 = OpenDeal("تطبيق طلبات مسبقة للمخابز", 4_000, bakery, c12, basel, 0, 2, 2, 50);

            ClosedDeal("موقع وهوية رقمية", 7_500, yasmin, c1, karim, won: true, 120, 85);
            ClosedDeal("نقاط بيع للصيدلية الرئيسية", 12_000, shifa, c2, salma, won: true, 150, 110);
            ClosedDeal("تطبيق إشعارات للطلاب", 8_000, ofuq, c3, basel, won: true, 80, 40);
            ClosedDeal("نظام إدارة المختبر", 30_000, rahma, c4, salma, won: true, 95, 20);
            ClosedDeal("أتمتة الفواتير والتحصيل", 14_000, bayan, c8, salma, won: true, 60, 8);
            ClosedDeal("لوحة متابعة المبيعات", 11_000, zaytouna, c11, karim, won: true, 45, 3);
            ClosedDeal("برنامج ولاء للعملاء", 16_000, nukhba, c6, basel, won: false, 100, 70, "السعر أعلى من الميزانية", reachedBeforeLoss: 3);
            ClosedDeal("تطبيق خدمة الغرف", 9_500, qamar, c7, karim, won: false, 70, 30, "اختاروا منافسًا", reachedBeforeLoss: 2);
            ClosedDeal("نظام إدارة الوثائق", 22_000, riyada, c9, basel, won: false, 55, 15, "تأجيل المشروع للسنة القادمة");
            ClosedDeal("موقع حجوزات المواعيد", 5_000, adala, c5, karim, won: false, 40, 10, "السعر أعلى من الميزانية", reachedBeforeLoss: 2);

            // Logged work, so timelines read like a real sales process.
            Log(d1, karim, ActivityType.Meeting, "اجتماع لعرض النظام على مدير العمليات", "طلبوا ربط النظام مع تطبيق التوصيل.", 18);
            Log(d1, karim, ActivityType.Email, "إرسال عرض السعر الأول", null, 9);
            Log(d1, karim, ActivityType.Call, "اتصال متابعة بخصوص العرض", "يريدون خصمًا على الفروع الإضافية.", 2);
            Log(d2, salma, ActivityType.Meeting, "عرض تجريبي للنظام في الصيدلية الرئيسية", null, 25);
            Log(d2, salma, ActivityType.Call, "نقاش شروط الدفع", "موافقة مبدئية على الدفع على ثلاث دفعات.", 1);
            Log(d3, basel, ActivityType.Meeting, "اجتماع تعريفي مع إدارة المدارس", "مهتمون بتطبيق أولياء الأمور بالدرجة الأولى.", 3);
            Log(d4, salma, ActivityType.Meeting, "جولة على أقسام المستشفى", null, 20);
            Log(d4, salma, ActivityType.Email, "إرسال عرض السعر", null, 13);
            Log(d6, basel, ActivityType.Call, "مكالمة تفاوض على السعر", "طلبوا تخفيض 15%.", 16);
            Log(d7, karim, ActivityType.Meeting, "زيارة الفندق ومعاينة سير العمل", null, 1);
            Log(d8, salma, ActivityType.Call, "اتصال أول مع المدير العام", "عندهم 4 مستودعات و25 مندوبًا.", 4);
            Log(d9, basel, ActivityType.Email, "إرسال عرض السعر ونطاق العمل", null, 6);
            Log(d10, salma, ActivityType.Call, "اتصال تعريفي", null, 11);
            Log(d11, karim, ActivityType.Meeting, "اجتماع التفاوض النهائي", "بانتظار موافقة مجلس الإدارة.", 4);
            Log(d12, basel, ActivityType.Call, "اتصال أول مع أبو صالح", "يريد التطبيق قبل رمضان.", 2);
            Log(d5, karim, ActivityType.Note, "تحوّل من عميل محتمل", null, 5);

            Task(d1, karim, "إرسال العرض المعدّل بعد الخصم", 0.2);
            Task(d11, karim, "متابعة قرار مجلس إدارة الزيتونة", -1);
            Task(d7, karim, "تجهيز عرض تجريبي لنظام الحجوزات", 3);
            Task(d2, salma, "إرسال مسودة العقد للمراجعة", 0.3);
            Task(d4, salma, "اتصال لإعادة تحريك صفقة المستشفى", 1);
            Task(d8, salma, "تحديد موعد زيارة المستودعات", 2);
            Task(d9, basel, "تجهيز عرض السعر النهائي للريادة", -2);
            Task(d3, basel, "اتصال لتحديد موعد العرض التجريبي", 1);
            Task(d6, basel, "الرد على طلب الخصم", -4);
            Task(d2, lian, "مراجعة الخصم المطلوب على صفقة الشفاء", 0.4);

            // Leads: (name, company, email, phone, source, owner, value, days ago, interest, touches)
            Lead("رامي الخطيب", "سوبرماركت الخير", "rami@alkhair-market.ps", "0599 111 222", LeadSource.Website, karim, 8_000, 1, "نقاط بيع لفرعين");
            Lead("هبة سلامة", "نادي اللياقة", "heba.salameh@gmail.com", "0598 765 432", LeadSource.Social, salma, 3_000, 2, "إدارة الاشتراكات والحضور");
            Lead("د. سامر عودة", "عيادة سامر للعيون", "samer@eyeclinic.ps", "0597 000 111", LeadSource.Referral, salma, 12_000, 6, "ملفات مرضى ومواعيد", touches: 2, status: LeadStatus.Contacted);
            Lead("منى حداد", "روضة البراعم", "mona@baraem.ps", null, LeadSource.Event, basel, 5_000, 10, "تطبيق تواصل مع الأهالي", touches: 2, status: LeadStatus.Qualified);
            Lead("خالد ناصر", null, "khaled.n@hotmail.com", "0592 223 334", LeadSource.ColdCall, karim, null, 20, null, touches: 1, status: LeadStatus.Contacted);
            Lead("م. يوسف قاسم", "الأمل للمقاولات", "yousef@alamal-co.ps", "0599 878 787", LeadSource.Website, null, 25_000, 0.3, "متابعة المشاريع والمقاولين الفرعيين");
            Lead("لينا جابر", "متجر لينا للأزياء", "lina.store@gmail.com", null, LeadSource.Social, basel, 2_500, 3, "متجر إلكتروني");
            Lead("أحمد زيد", "مطبعة الزيد", "ahmad@zaidprint.ps", "0598 010 020", LeadSource.Referral, karim, 9_000, 8, "إدارة طلبات الطباعة", touches: 3, status: LeadStatus.Qualified);
            Lead("سارة يونس", "جمعية سيدات الريف", null, "0569 998 887", LeadSource.Event, salma, 4_000, 14, "موقع ومتجر للمنتجات", touches: 1, status: LeadStatus.Contacted);
            Lead("عمر الشريف", "شركة النقل السريع", "omar@fastmove.ps", "0597 343 434", LeadSource.Website, null, 15_000, 0.1, "تتبع الشحنات");
            Lead("نادر بركات", null, "nader.b@yahoo.com", null, LeadSource.ColdCall, basel, null, 40, null, status: LeadStatus.Disqualified);
            Lead("ريم أبو علي", "صالون ريم", null, "0595 554 443", LeadSource.Social, karim, 1_500, 45, "حجز مواعيد");
            // Same email as a contact at Al-Shifa: the lead page shows the duplicate warning.
            Lead("عبير منصور", "صيدليات الشفاء", "rana@alshifa-pharma.ps", null, LeadSource.Website, salma, null, 0.5, "فرع جديد");

            // Leads that already became deals, so conversion by source is measurable.
            Converted("المحامي وائل سمارة", adala.Name, "wael@adala-law.ps", LeadSource.Referral, karim, d5, 7);
            Converted("أ. منير خليل", ofuq.Name, "munir@ofuq-schools.ps", LeadSource.Event, basel, d3, 14);
            Converted("ديما قسيس", qamar.Name, "dima@alqamar-hotel.ps", LeadSource.Website, karim, d7, 11);
            Converted("طارق البيان", bayan.Name, "tareq@albayan-group.ps", LeadSource.Referral, salma, d8, 6);
            Converted("أبو صالح", bakery.Name, null, LeadSource.Social, basel, d12, 3);

            Notify(karim, "عميل محتمل جديد مسند إليك: رامي الخطيب", "/leads");
            Notify(salma, "عميل محتمل جديد مسند إليك: عبير منصور", "/leads");
            Notify(lian, "تم تسجيل صفقة رابحة: لوحة متابعة المبيعات (11,000$)", "/reports");
        }

        public void SeedOfuq()
        {
            Tenant("أفق التقنية", "ofuq");
            Pipeline();
            var admin = User("مها العلي", "admin@ofuq.test", UserRole.Admin);
            var company = Company("شركة اختبار العزل", "تقنية", "عمّان", admin, 10);
            var contact = Contact("زيد العزل", "مدير", "zaid@isolation.test", null, company, admin);
            OpenDeal("صفقة لا يجب أن تظهر في نور", 1_000, company, contact, admin, 0, 5, 1, 30);
        }

        // ---------- builders ----------

        private void Tenant(string name, string code)
        {
            _tenant = new Tenant(name, code);
            db.Tenants.Add(_tenant);
        }

        private void Pipeline()
        {
            _stages =
            [
                new("تواصل أولي", 1, 10, StageKind.Open),
                new("اجتماع تعريفي", 2, 25, StageKind.Open),
                new("عرض سعر", 3, 50, StageKind.Open),
                new("تفاوض", 4, 75, StageKind.Open),
                new("ربحنا", 5, 100, StageKind.Won),
                new("خسرنا", 6, 0, StageKind.Lost)
            ];
            foreach (var s in _stages) s.TenantId = _tenant.Id;
            db.Stages.AddRange(_stages);
        }

        private User User(string name, string email, UserRole role, bool receivesLeads = true)
        {
            var u = new User(name, email, role) { TenantId = _tenant.Id };
            u.Update(name, role, true, receivesLeads);
            u.SetPasswordHash(hasher.Hash(u, Password));
            db.Users.Add(u);
            return u;
        }

        private Company Company(string name, string industry, string city, User owner, int daysAgo)
        {
            var c = new Company(name, owner.Id) { TenantId = _tenant.Id };
            c.Update(name, industry, city, null, null, owner.Id);
            db.Companies.Add(c);
            Set(c, x => x.CreatedAt, Ago(daysAgo));
            return c;
        }

        private Contact Contact(string name, string title, string? email, string? phone, Company company, User owner)
        {
            var c = new Contact(name, owner.Id) { TenantId = _tenant.Id };
            c.Update(name, email, phone, title, company.Id, owner.Id);
            db.Contacts.Add(c);
            Set(c, x => x.CreatedAt, db.Entry(company).Property(x => x.CreatedAt).CurrentValue);
            return c;
        }

        /// <summary>Creates an open deal and walks it through the stages up to <paramref name="stageIndex"/>.</summary>
        private Deal OpenDeal(string title, decimal value, Company company, Contact contact, User owner,
            int stageIndex, double createdAgo, double lastTouchAgo, int closesIn)
        {
            var deal = new Deal(title, value, Open(0), owner.Id, company.Id, contact.Id, now.Date.AddDays(closesIn)) { TenantId = _tenant.Id };
            db.Deals.Add(deal);
            Walk(deal, owner, createdAgo, stageIndex, Math.Max(lastTouchAgo, 0.5), null);
            Set(deal, x => x.CreatedAt, Ago(createdAgo));
            Set(deal, x => x.LastActivityAt, Ago(lastTouchAgo));
            return deal;
        }

        private void ClosedDeal(string title, decimal value, Company company, Contact contact, User owner,
            bool won, double createdAgo, double closedAgo, string? lostReason = null, int reachedBeforeLoss = 1)
        {
            var deal = new Deal(title, value, Open(0), owner.Id, company.Id, contact.Id, now.Date.AddDays(-closedAgo)) { TenantId = _tenant.Id };
            db.Deals.Add(deal);
            var reached = won ? 3 : reachedBeforeLoss;
            Walk(deal, owner, createdAgo, reached, closedAgo + 1, won ? Won : Lost, lostReason, closedAgo);
            Set(deal, x => x.CreatedAt, Ago(createdAgo));
            Set(deal, x => x.LastActivityAt, Ago(closedAgo));
            Set(deal, x => x.ClosedAt, Ago(closedAgo));
            Log(deal, owner, ActivityType.System, won ? "ربحنا الصفقة" : $"خسرنا الصفقة: {lostReason}", null, closedAgo);
        }

        /// <summary>Writes a stage history spread evenly between creation and the last move.</summary>
        private void Walk(Deal deal, User owner, double createdAgo, int stageIndex, double lastMoveAgo,
            Stage? final, string? lostReason = null, double? finalAgo = null)
        {
            History(deal.Id, null, Open(0), owner, createdAgo);
            var steps = stageIndex + (final is null ? 0 : 1);
            for (var i = 1; i <= stageIndex; i++)
            {
                var at = createdAgo - (createdAgo - lastMoveAgo) * i / Math.Max(steps, 1);
                deal.MoveTo(Open(i), owner.Id, null);
                History(deal.Id, Open(i - 1), Open(i), owner, at);
            }
            if (final is not null)
            {
                deal.MoveTo(final, owner.Id, lostReason);
                History(deal.Id, Open(stageIndex), final, owner, finalAgo ?? lastMoveAgo);
            }
            Set(deal, x => x.StageEnteredAt, Ago(finalAgo ?? (stageIndex == 0 ? createdAgo : createdAgo - (createdAgo - lastMoveAgo) * stageIndex / Math.Max(steps, 1))));
        }

        private void History(Guid dealId, Stage? from, Stage to, User by, double ago)
        {
            var change = new DealStageChange(dealId, from?.Id, to.Id, by.Id) { TenantId = _tenant.Id };
            db.DealStageChanges.Add(change);
            Set(change, x => x.At, Ago(ago));
        }

        private void Log(Deal deal, User owner, ActivityType type, string subject, string? notes, double ago)
        {
            var a = new Activity(type, subject, owner.Id, owner.Id) { TenantId = _tenant.Id }
                .WithNotes(notes).About(deal.Id, deal.ContactId, null, null);
            db.Activities.Add(a);
            Set(a, x => x.CreatedAt, Ago(ago));
            Set(a, x => x.CompletedAt, Ago(ago));
        }

        private void Task(Deal deal, User owner, string subject, double dueInDays)
        {
            var due = now.Date.AddDays(Math.Floor(dueInDays)).AddHours(dueInDays % 1 == 0 ? 9 : 10 + dueInDays % 1 * 20);
            var a = new Activity(ActivityType.Task, subject, owner.Id, owner.Id) { TenantId = _tenant.Id }
                .Due(due).About(deal.Id, null, null, null);
            db.Activities.Add(a);
            Set(a, x => x.CreatedAt, Ago(Math.Max(1, -dueInDays + 2)));
            // Tasks already overdue in the demo have had their reminder, so the job does not flood on first run.
            if (due < now) a.MarkReminderSent();
        }

        private void Lead(string name, string? company, string? email, string? phone, LeadSource source, User? owner,
            decimal? value, double daysAgo, string? interest, int touches = 0, LeadStatus status = LeadStatus.New)
        {
            var lead = new Lead(name, source) { TenantId = _tenant.Id };
            lead.Update(name, company, email, phone, interest, value, source);
            if (owner is not null) lead.AssignTo(owner.Id);
            db.Leads.Add(lead);
            Set(lead, x => x.CreatedAt, Ago(daysAgo));

            for (var i = 0; i < touches; i++)
            {
                var a = new Activity(i % 2 == 0 ? ActivityType.Call : ActivityType.Email,
                        i % 2 == 0 ? "اتصال تعريفي" : "إرسال معلومات عن الخدمات", owner!.Id, owner.Id) { TenantId = _tenant.Id }
                    .About(null, null, null, lead.Id);
                db.Activities.Add(a);
                var at = Ago(daysAgo - (i + 1) * Math.Max(daysAgo / (touches + 1), 0.1));
                Set(a, x => x.CreatedAt, at);
                Set(a, x => x.CompletedAt, at);
                lead.RecordContact();
                Set(lead, x => x.LastContactedAt, at);
            }

            if (status == LeadStatus.Qualified) lead.Qualify();
            if (status == LeadStatus.Disqualified) lead.Disqualify("لا يوجد احتياج حاليًا");
            if (status == LeadStatus.Contacted && touches == 0)
            {
                lead.RecordContact();
                Set(lead, x => x.LastContactedAt, Ago(daysAgo / 2));
            }

            // CreatedAt was back-dated above, so the score reflects the lead's real age.
            lead.SetScore(LeadScoring.Calculate(lead, touches, now).Total);
        }

        private void Converted(string name, string company, string? email, LeadSource source, User owner, Deal deal, double daysAgo)
        {
            var lead = new Lead(name, source) { TenantId = _tenant.Id };
            lead.Update(name, company, email, null, null, deal.Value, source);
            lead.AssignTo(owner.Id);
            lead.SetScore(LeadScoring.Calculate(lead, 1, now).Total);
            lead.MarkConverted(deal.Id);
            db.Leads.Add(lead);
            Set(lead, x => x.CreatedAt, Ago(daysAgo));
        }

        private void Notify(User user, string title, string link)
        {
            db.Notifications.Add(new Notification(user.Id, title, link) { TenantId = _tenant.Id });
        }

        private void Set<TEntity, TValue>(TEntity entity, Expression<Func<TEntity, TValue>> property, TValue value)
            where TEntity : class => db.Entry(entity).Property(property).CurrentValue = value;
    }
}
