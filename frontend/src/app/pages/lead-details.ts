import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api } from '../core/api';
import { Auth, errorText } from '../core/auth';
import { MoneyPipe, WhenPipe, leadStatusLabel, sourceLabel } from '../core/format';
import { LeadDetails, Ref, TeamMember } from '../core/models';
import { Dialog, Toasts } from '../core/ui';
import { ActivityFeed } from '../shared/activity-feed';
import { LeadForm } from '../shared/forms';

@Component({
  selector: 'app-lead-details',
  imports: [RouterLink, FormsModule, MoneyPipe, WhenPipe, ActivityFeed, Dialog, LeadForm],
  template: `
    @if (lead(); as l) {
      <div class="page">
        <nav class="crumbs"><a routerLink="/leads">العملاء المحتملون</a> <span>/</span> <span>{{ l.fullName }}</span></nav>

        @if (l.status === 'Converted' && l.convertedDealId) {
          <div class="banner won">تحوّل هذا العميل إلى صفقة. <a [routerLink]="['/deals', l.convertedDealId]">افتح الصفقة</a></div>
        }
        @if (l.status === 'Disqualified') {
          <div class="banner lost">مستبعد: {{ l.disqualifyReason }} <button type="button" class="link" (click)="act(api.leadAction(l.id, 'reopen'), 'أُعيد فتح العميل.')">إعادة فتح</button></div>
        }
        @if (l.duplicates.length) {
          <div class="banner warn">
            قد يكون مكررًا:
            @for (d of l.duplicates; track d.id) {
              <a [routerLink]="[d.kind === 'lead' ? '/leads' : '/contacts', d.id]">{{ d.name }}{{ d.detail ? ' · ' + d.detail : '' }}</a>
              <span class="muted">({{ d.reason }})</span>
            }
          </div>
        }

        <header class="hero card">
          <div class="grow">
            <div class="row wrap">
              <span class="chip" [class]="tone()">{{ statusLabel[l.status] }}</span>
              <span class="chip">{{ sourceLabel[l.source] }}</span>
              <span class="muted small">وصل {{ l.createdAt | when: 'ago' }}</span>
            </div>
            <h1>{{ l.fullName }}</h1>
            <p class="muted">{{ l.companyName ?? 'بدون شركة' }}{{ l.interest ? ' · مهتم بـ ' + l.interest : '' }}</p>
            <div class="contact-line">
              @if (l.phone) { <a class="ltr" [href]="'tel:' + l.phone">{{ l.phone }}</a> }
              @if (l.email) { <a class="ltr" [href]="'mailto:' + l.email">{{ l.email }}</a> }
            </div>
          </div>
          <div class="hero-actions">
            @if (open()) {
              <button type="button" class="btn won" (click)="startConvert()">تحويل إلى صفقة</button>
              @if (l.status !== 'Qualified') {
                <button type="button" class="btn" (click)="act(api.leadAction(l.id, 'qualify'), 'تم تأهيل العميل.')">تأهيل</button>
              }
              <button type="button" class="btn lost" (click)="disqualifyOpen.set(true)">استبعاد</button>
              <button type="button" class="btn ghost" (click)="editing.set(true)">تعديل</button>
            }
          </div>
        </header>

        <div class="layout">
          <section>
            <app-activity-feed [items]="l.activities" [target]="{ leadId: l.id }" [readonly]="!open()" (changed)="load()" />
          </section>

          <aside class="aside">
            <section class="card score-card">
              <div class="score-top">
                <div>
                  <div class="muted small">التقييم</div>
                  <div class="big num" [class.hot]="l.score.total >= 70">{{ l.score.total }}<span>/100</span></div>
                </div>
                <div class="meter"><i [style.width.%]="l.score.total"></i></div>
              </div>
              <ul class="factors">
                @for (f of l.score.factors; track f.label) {
                  <li>
                    <span>{{ f.label }}</span>
                    <span class="num ltr" [class.neg]="f.points < 0">{{ f.points > 0 ? '+' : '' }}{{ f.points }}</span>
                  </li>
                }
              </ul>
            </section>

            <section class="card side-card">
              <h2>الإسناد</h2>
              @if (l.owner) {
                <p>{{ l.owner.fullName }}</p>
              } @else {
                <p class="muted small">غير مسند لأحد بعد.</p>
                @if (open()) { <button type="button" class="btn sm" (click)="act(api.leadAction(l.id, 'claim'), 'أصبح العميل من عملائك.')">خذه لي</button> }
              }
              @if (auth.isManager() && open()) {
                <div class="row assign">
                  <select class="select" [(ngModel)]="assignTo">
                    <option value="">إسناد إلى…</option>
                    @for (u of team(); track u.id) { <option [value]="u.id">{{ u.fullName }} ({{ u.openLeads }})</option> }
                  </select>
                  <button type="button" class="btn sm" [disabled]="!assignTo" (click)="act(api.assignLead(l.id, assignTo), 'تم الإسناد.')">إسناد</button>
                </div>
              }
            </section>

            <section class="card side-card">
              <h2>معلومات</h2>
              <dl>
                <div><dt>القيمة المتوقعة</dt><dd class="num">{{ l.estimatedValue | money }}</dd></div>
                <div><dt>آخر تواصل</dt><dd>{{ l.lastContactedAt ? (l.lastContactedAt | when: 'ago') : 'لم يتم بعد' }}</dd></div>
              </dl>
            </section>
          </aside>
        </div>
      </div>

      @if (converting()) {
        <app-dialog heading="تحويل إلى صفقة" hint="ستُنشأ جهة اتصال وشركة (إن لزم) وصفقة في المرحلة الأولى، وينتقل سجل النشاط معها." [width]="520" (closed)="converting.set(false)">
          <label class="field"><span>عنوان الصفقة</span><input class="input" [(ngModel)]="conv.title" /></label>
          <div class="grid-2">
            <label class="field"><span>القيمة ($)</span><input class="input" type="number" min="0" [(ngModel)]="conv.value" /></label>
            <label class="field"><span>الإغلاق المتوقع</span><input class="input" type="date" [(ngModel)]="conv.close" /></label>
          </div>
          <label class="field">
            <span>الشركة</span>
            <select class="select" [(ngModel)]="conv.companyId">
              <option value="">{{ l.companyName ? 'إنشاء أو استخدام «' + l.companyName + '»' : 'بدون شركة' }}</option>
              @for (c of companies(); track c.id) { <option [value]="c.id">ربط بـ {{ c.name }}</option> }
            </select>
          </label>
          <div actions>
            <button type="button" class="btn won" [disabled]="busy() || !conv.title.trim()" (click)="convert()">تحويل</button>
            <button type="button" class="btn ghost" (click)="converting.set(false)">إلغاء</button>
          </div>
        </app-dialog>
      }

      @if (disqualifyOpen()) {
        <app-dialog heading="استبعاد العميل المحتمل" (closed)="disqualifyOpen.set(false)">
          <label class="field"><span>السبب</span><input class="input" [(ngModel)]="reason" placeholder="مثال: لا يوجد احتياج حاليًا" /></label>
          <div actions>
            <button type="button" class="btn primary" [disabled]="!reason.trim()" (click)="act(api.disqualify(l.id, reason), 'تم الاستبعاد.'); disqualifyOpen.set(false)">استبعاد</button>
            <button type="button" class="btn ghost" (click)="disqualifyOpen.set(false)">إلغاء</button>
          </div>
        </app-dialog>
      }

      @if (editing()) {
        <app-lead-form [lead]="l" (closed)="editing.set(false)" (saved)="editing.set(false); load()" />
      }
    } @else if (missing()) {
      <div class="page"><p class="empty">العميل غير موجود أو ليس ضمن صلاحياتك.</p></div>
    } @else {
      <p class="loading">جارٍ التحميل…</p>
    }
  `,
  styles: `
    .crumbs { font-size: 13px; color: var(--muted); margin-bottom: 14px; display: flex; gap: 8px; }
    .crumbs a:hover { color: var(--primary); }
    .banner { padding: 12px 16px; border-radius: 12px; margin-bottom: 14px; font-size: 14px; display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .banner a, .banner .link { color: var(--primary); text-decoration: underline; }
    .banner .link { border: 0; background: none; font: inherit; cursor: pointer; padding: 0; }
    .banner.won { background: var(--won-soft); color: var(--won); }
    .banner.lost { background: var(--lost-soft); color: var(--lost); }
    .banner.warn { background: var(--warn-soft); color: var(--warn); }
    .hero { display: flex; gap: 20px; padding: 22px 24px; flex-wrap: wrap; margin-bottom: 22px; }
    .hero h1 { margin-top: 10px; }
    .wrap { flex-wrap: wrap; gap: 8px; }
    .contact-line { display: flex; gap: 18px; margin-top: 8px; font-size: 14px; color: var(--primary); }
    .hero-actions { display: flex; gap: 8px; align-items: flex-start; flex-wrap: wrap; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 22px; align-items: start; }
    .aside { display: flex; flex-direction: column; gap: 16px; }
    .score-card { padding: 18px; }
    .score-top { display: flex; flex-direction: column; gap: 10px; }
    .big { font-size: 40px; font-weight: 500; line-height: 1.1; }
    .big span { font-size: 15px; color: var(--muted); font-weight: 400; }
    .big.hot { color: #b0602f; }
    .meter { height: 8px; border-radius: 4px; background: var(--sunk); overflow: hidden; }
    .meter i { display: block; height: 100%; background: linear-gradient(90deg, #e9b48f, var(--accent)); border-radius: 4px; }
    .factors { list-style: none; padding: 0; margin: 16px 0 0; display: flex; flex-direction: column; gap: 8px; font-size: 13px; }
    .factors li { display: flex; justify-content: space-between; gap: 10px; }
    .factors .num { color: var(--won); font-weight: 500; }
    .factors .num.neg { color: var(--lost); }
    .side-card { padding: 16px 18px; }
    .side-card h2 { font-size: 14px; margin-bottom: 8px; }
    .assign { margin-top: 12px; }
    .assign .select { height: 34px; }
    dl { margin: 0; display: flex; flex-direction: column; gap: 8px; }
    dl div { display: flex; justify-content: space-between; font-size: 13px; }
    dt { color: var(--muted); }
    dd { margin: 0; }
    @media (max-width: 980px) { .layout { grid-template-columns: 1fr; } }
  `
})
export class LeadDetailsPage {
  protected api = inject(Api);
  protected auth = inject(Auth);
  private toasts = inject(Toasts);
  private router = inject(Router);

  readonly id = input.required<string>();
  protected lead = signal<LeadDetails | null>(null);
  protected missing = signal(false);
  protected busy = signal(false);
  protected team = signal<TeamMember[]>([]);
  protected companies = signal<Ref[]>([]);
  protected editing = signal(false);
  protected converting = signal(false);
  protected disqualifyOpen = signal(false);
  protected reason = '';
  protected assignTo = '';
  protected conv = { title: '', value: 0, close: '', companyId: '' };

  protected statusLabel = leadStatusLabel;
  protected sourceLabel = sourceLabel;
  protected open = computed(() => {
    const s = this.lead()?.status;
    return s === 'New' || s === 'Contacted' || s === 'Qualified';
  });
  protected tone = computed(() => ({
    New: 'chip primary', Contacted: 'chip', Qualified: 'chip won', Disqualified: 'chip lost', Converted: 'chip accent'
  })[this.lead()!.status]);

  constructor() {
    effect(() => {
      this.id();
      untracked(() => { this.lead.set(null); this.load(); });
    });
    if (this.auth.isManager()) this.api.team().subscribe(t => this.team.set(t.filter(u => u.isActive)));
  }

  protected load() {
    this.api.lead(this.id()).subscribe({ next: l => this.lead.set(l), error: () => this.missing.set(true) });
  }

  protected act(call: Observable<void>, ok: string) {
    call.subscribe({
      next: () => { this.toasts.ok(ok); this.assignTo = ''; this.load(); },
      error: e => this.toasts.error(errorText(e))
    });
  }

  protected startConvert() {
    const l = this.lead()!;
    this.conv = {
      title: l.interest ? `${l.interest}${l.companyName ? ' – ' + l.companyName : ''}` : `صفقة ${l.companyName ?? l.fullName}`,
      value: l.estimatedValue ?? 0, close: '', companyId: ''
    };
    this.api.companies('', 1).subscribe(r => this.companies.set(r.items.map(c => ({ id: c.id, name: c.name }))));
    this.converting.set(true);
  }

  protected convert() {
    this.busy.set(true);
    this.api.convertLead(this.id(), {
      dealTitle: this.conv.title, dealValue: Number(this.conv.value) || 0,
      expectedClose: this.conv.close ? new Date(this.conv.close).toISOString() : null,
      existingCompanyId: this.conv.companyId || null
    }).subscribe({
      next: r => { this.toasts.ok('تم التحويل إلى صفقة.'); this.router.navigate(['/deals', r.dealId]); },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); }
    });
  }
}
