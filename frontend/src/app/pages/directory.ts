import { Component, OnInit, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { Api } from '../core/api';
import { InitialsPipe, MoneyPipe, WhenPipe } from '../core/format';
import { CompanyDetails, CompanyItem, ContactDetails, ContactItem, DealRow, MyTasks, Paged } from '../core/models';
import { Toasts } from '../core/ui';
import { ActivityFeed } from '../shared/activity-feed';
import { CompanyForm, ContactForm, DealForm } from '../shared/forms';

@Component({
  selector: 'app-deal-rows',
  imports: [RouterLink, MoneyPipe],
  template: `
    @for (d of deals(); track d.id) {
      <a class="deal-row" [routerLink]="['/deals', d.id]">
        <span class="mark {{ d.status }}"></span>
        <span class="grow">
          <strong>{{ d.title }}</strong>
          <span class="muted small">{{ d.status === 'Open' ? d.stageName : d.status === 'Won' ? 'رابحة' : 'خاسرة' }} · {{ d.owner.fullName }}</span>
        </span>
        <span class="num">{{ d.value | money }}</span>
      </a>
    } @empty {
      <p class="empty">لا صفقات.</p>
    }
  `,
  styles: `
    .deal-row { display: flex; align-items: center; gap: 12px; padding: 12px 18px; border-bottom: 1px solid var(--line); font-size: 14px; }
    .deal-row:last-child { border-bottom: 0; }
    .deal-row:hover { background: #fbf8f1; }
    .deal-row strong { font-weight: 500; display: block; }
    .mark { width: 8px; height: 8px; border-radius: 50%; background: var(--primary); flex-shrink: 0; }
    .mark.Won { background: var(--won); }
    .mark.Lost { background: var(--lost); }
  `
})
export class DealRows {
  readonly deals = input.required<DealRow[]>();
}

// ---------- Companies ----------

@Component({
  selector: 'app-companies',
  imports: [FormsModule, MoneyPipe, CompanyForm],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>الشركات</h1><p class="sub">دليل مشترك لكل الفريق.</p></div>
        <button type="button" class="btn accent" (click)="creating.set(true)">شركة جديدة</button>
      </div>
      <div class="toolbar">
        <input class="input" type="search" placeholder="اسم، مدينة، قطاع" [(ngModel)]="search" (ngModelChange)="typed$.next()" />
      </div>
      <section class="card">
        @if (result(); as r) {
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>الشركة</th><th class="hide-sm">المدينة</th><th class="hide-sm">جهات الاتصال</th><th>صفقات مفتوحة</th><th class="hide-sm">ربحنا منها</th><th class="hide-sm">المسؤول</th></tr></thead>
              <tbody>
                @for (c of r.items; track c.id) {
                  <tr class="link" (click)="router.navigate(['/companies', c.id])">
                    <td><div class="title">{{ c.name }}</div><div class="sub">{{ c.industry }}</div></td>
                    <td class="hide-sm">{{ c.city }}</td>
                    <td class="hide-sm">{{ c.contactCount }}</td>
                    <td class="num">{{ c.openDeals ? c.openDeals + ' · ' : '' }}{{ c.openValue | money }}</td>
                    <td class="hide-sm num won-text">{{ c.wonValue | money }}</td>
                    <td class="hide-sm">{{ c.owner.fullName }}</td>
                  </tr>
                } @empty { <tr><td colspan="6" class="empty">لا نتائج.</td></tr> }
              </tbody>
            </table>
          </div>
        } @else { <p class="loading">جارٍ التحميل…</p> }
      </section>
    </div>
    @if (creating()) {
      <app-company-form (closed)="creating.set(false)" (saved)="router.navigate(['/companies', $event])" />
    }
  `,
  styles: `.won-text { color: var(--won); }`
})
export class CompaniesPage implements OnInit {
  private api = inject(Api);
  protected router = inject(Router);
  protected result = signal<Paged<CompanyItem> | null>(null);
  protected creating = signal(false);
  protected search = '';
  protected typed$ = new Subject<void>();

  ngOnInit() {
    this.typed$.pipe(debounceTime(300)).subscribe(() => this.load());
    this.load();
  }

  private load() {
    this.api.companies(this.search, 1).subscribe(r => this.result.set(r));
  }
}

@Component({
  selector: 'app-company-details',
  imports: [RouterLink, MoneyPipe, WhenPipe, InitialsPipe, ActivityFeed, DealRows, CompanyForm, ContactForm, DealForm],
  template: `
    @if (company(); as c) {
      <div class="page">
        <nav class="crumbs"><a routerLink="/companies">الشركات</a> <span>/</span> <span>{{ c.name }}</span></nav>
        <header class="hero card">
          <div class="mono">{{ c.name | initials }}</div>
          <div class="grow">
            <h1>{{ c.name }}</h1>
            <p class="muted">{{ c.industry }}{{ c.city ? ' · ' + c.city : '' }} · المسؤول {{ c.owner.fullName }}</p>
            <p class="links">
              @if (c.website) { <a class="ltr" [href]="'https://' + c.website" target="_blank" rel="noopener">{{ c.website }}</a> }
              @if (c.phone) { <a class="ltr" [href]="'tel:' + c.phone">{{ c.phone }}</a> }
            </p>
          </div>
          <div class="totals">
            <div><span class="muted small">مفتوح</span><strong class="num">{{ c.openValue | money }}</strong></div>
            <div><span class="muted small">ربحنا</span><strong class="num won-text">{{ c.wonValue | money }}</strong></div>
          </div>
          <div class="hero-actions">
            <button type="button" class="btn accent sm" (click)="dealOpen.set(true)">صفقة جديدة</button>
            <button type="button" class="btn ghost sm" (click)="editing.set(true)">تعديل</button>
          </div>
        </header>

        <div class="layout">
          <section>
            <h2 class="sec">كل ما حدث مع {{ c.name }}</h2>
            <app-activity-feed [items]="c.activities" [target]="{ companyId: c.id }" [showRelated]="true" (changed)="load()" />
          </section>
          <aside class="aside">
            <section class="card">
              <header class="card-head"><h2>الصفقات</h2></header>
              <app-deal-rows [deals]="c.deals" />
            </section>
            <section class="card">
              <header class="card-head"><h2>الأشخاص</h2><button type="button" class="btn ghost sm" (click)="contactOpen.set(true)">إضافة</button></header>
              @for (p of c.contacts; track p.id) {
                <a class="person" [routerLink]="['/contacts', p.id]">
                  <span class="avatar">{{ p.fullName | initials }}</span>
                  <span class="grow"><strong>{{ p.fullName }}</strong><span class="muted small">{{ p.jobTitle }}</span></span>
                </a>
              } @empty { <p class="empty">لا جهات اتصال.</p> }
            </section>
            <p class="muted small">أُضيفت {{ c.createdAt | when: 'full' }}</p>
          </aside>
        </div>
      </div>

      @if (editing()) { <app-company-form [company]="c" (closed)="editing.set(false)" (saved)="editing.set(false); load()" /> }
      @if (contactOpen()) { <app-contact-form [presetCompanyId]="c.id" (closed)="contactOpen.set(false)" (saved)="contactOpen.set(false); load()" /> }
      @if (dealOpen()) { <app-deal-form [presetCompanyId]="c.id" (closed)="dealOpen.set(false)" (saved)="router.navigate(['/deals', $event])" /> }
    } @else { <p class="loading">جارٍ التحميل…</p> }
  `,
  styles: `
    .crumbs { font-size: 13px; color: var(--muted); margin-bottom: 14px; display: flex; gap: 8px; }
    .hero { display: flex; gap: 18px; align-items: center; padding: 20px 24px; margin-bottom: 22px; flex-wrap: wrap; }
    .mono { width: 56px; height: 56px; border-radius: 16px; background: var(--nav); color: #fff; display: grid; place-items: center; font-size: 18px; font-weight: 500; flex-shrink: 0; }
    .links { display: flex; gap: 16px; font-size: 13px; color: var(--primary); margin-top: 2px; }
    .totals { display: flex; gap: 22px; }
    .totals div { display: flex; flex-direction: column; }
    .totals strong { font-size: 18px; font-weight: 500; }
    .won-text { color: var(--won); }
    .hero-actions { display: flex; gap: 8px; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 22px; align-items: start; }
    .aside { display: flex; flex-direction: column; gap: 16px; }
    .sec { font-size: 15px; margin-bottom: 12px; }
    .person { display: flex; align-items: center; gap: 10px; padding: 11px 18px; border-bottom: 1px solid var(--line); font-size: 14px; }
    .person:last-child { border-bottom: 0; }
    .person:hover { background: #fbf8f1; }
    .person strong { font-weight: 500; display: block; }
    @media (max-width: 980px) { .layout { grid-template-columns: 1fr; } }
  `
})
export class CompanyDetailsPage {
  private api = inject(Api);
  protected router = inject(Router);
  readonly id = input.required<string>();
  protected company = signal<CompanyDetails | null>(null);
  protected editing = signal(false);
  protected contactOpen = signal(false);
  protected dealOpen = signal(false);

  constructor() {
    effect(() => { this.id(); untracked(() => { this.company.set(null); this.load(); }); });
  }

  protected load() {
    this.api.company(this.id()).subscribe(c => this.company.set(c));
  }
}

// ---------- Contacts ----------

@Component({
  selector: 'app-contacts',
  imports: [FormsModule, InitialsPipe, ContactForm],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>جهات الاتصال</h1><p class="sub">الأشخاص الذين نتعامل معهم داخل الشركات.</p></div>
        <button type="button" class="btn accent" (click)="creating.set(true)">جهة اتصال جديدة</button>
      </div>
      <div class="toolbar">
        <input class="input" type="search" placeholder="اسم، بريد، هاتف، شركة" [(ngModel)]="search" (ngModelChange)="typed$.next()" />
      </div>
      <section class="card">
        @if (result(); as r) {
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>الاسم</th><th>الشركة</th><th class="hide-sm">الهاتف</th><th class="hide-sm">البريد</th></tr></thead>
              <tbody>
                @for (c of r.items; track c.id) {
                  <tr class="link" (click)="router.navigate(['/contacts', c.id])">
                    <td><div class="row"><span class="avatar">{{ c.fullName | initials }}</span><div><div class="title">{{ c.fullName }}</div><div class="sub">{{ c.jobTitle }}</div></div></div></td>
                    <td>{{ c.company?.name }}</td>
                    <td class="hide-sm"><span class="ltr">{{ c.phone }}</span></td>
                    <td class="hide-sm"><span class="ltr">{{ c.email }}</span></td>
                  </tr>
                } @empty { <tr><td colspan="4" class="empty">لا نتائج.</td></tr> }
              </tbody>
            </table>
          </div>
        } @else { <p class="loading">جارٍ التحميل…</p> }
      </section>
    </div>
    @if (creating()) { <app-contact-form (closed)="creating.set(false)" (saved)="router.navigate(['/contacts', $event])" /> }
  `
})
export class ContactsPage implements OnInit {
  private api = inject(Api);
  protected router = inject(Router);
  protected result = signal<Paged<ContactItem> | null>(null);
  protected creating = signal(false);
  protected search = '';
  protected typed$ = new Subject<void>();

  ngOnInit() {
    this.typed$.pipe(debounceTime(300)).subscribe(() => this.load());
    this.load();
  }

  private load() {
    this.api.contacts(this.search, 1).subscribe(r => this.result.set(r));
  }
}

@Component({
  selector: 'app-contact-details',
  imports: [RouterLink, InitialsPipe, ActivityFeed, DealRows, ContactForm, DealForm],
  template: `
    @if (contact(); as c) {
      <div class="page">
        <nav class="crumbs"><a routerLink="/contacts">جهات الاتصال</a> <span>/</span> <span>{{ c.fullName }}</span></nav>
        <header class="hero card">
          <span class="avatar xl">{{ c.fullName | initials }}</span>
          <div class="grow">
            <h1>{{ c.fullName }}</h1>
            <p class="muted">{{ c.jobTitle }}
              @if (c.company) { · <a [routerLink]="['/companies', c.company.id]">{{ c.company.name }}</a> }
            </p>
            <p class="links">
              @if (c.phone) { <a class="ltr" [href]="'tel:' + c.phone">{{ c.phone }}</a> }
              @if (c.email) { <a class="ltr" [href]="'mailto:' + c.email">{{ c.email }}</a> }
            </p>
          </div>
          <div class="row">
            <button type="button" class="btn accent sm" (click)="dealOpen.set(true)">صفقة جديدة</button>
            <button type="button" class="btn ghost sm" (click)="editing.set(true)">تعديل</button>
          </div>
        </header>
        <div class="layout">
          <section><app-activity-feed [items]="c.activities" [target]="{ contactId: c.id }" [showRelated]="true" (changed)="load()" /></section>
          <aside>
            <section class="card"><header class="card-head"><h2>الصفقات</h2></header><app-deal-rows [deals]="c.deals" /></section>
          </aside>
        </div>
      </div>
      @if (editing()) { <app-contact-form [contact]="c" (closed)="editing.set(false)" (saved)="editing.set(false); load()" /> }
      @if (dealOpen()) { <app-deal-form [presetCompanyId]="c.company?.id ?? null" [presetContactId]="c.id" (closed)="dealOpen.set(false)" (saved)="router.navigate(['/deals', $event])" /> }
    } @else { <p class="loading">جارٍ التحميل…</p> }
  `,
  styles: `
    .crumbs { font-size: 13px; color: var(--muted); margin-bottom: 14px; display: flex; gap: 8px; }
    .hero { display: flex; gap: 18px; align-items: center; padding: 20px 24px; margin-bottom: 22px; flex-wrap: wrap; }
    .avatar.xl { width: 56px; height: 56px; font-size: 18px; }
    .hero a { color: var(--primary); }
    .links { display: flex; gap: 16px; font-size: 13px; margin-top: 2px; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 22px; align-items: start; }
    @media (max-width: 980px) { .layout { grid-template-columns: 1fr; } }
  `
})
export class ContactDetailsPage {
  private api = inject(Api);
  protected router = inject(Router);
  readonly id = input.required<string>();
  protected contact = signal<ContactDetails | null>(null);
  protected editing = signal(false);
  protected dealOpen = signal(false);

  constructor() {
    effect(() => { this.id(); untracked(() => { this.contact.set(null); this.load(); }); });
  }

  protected load() {
    this.api.contact(this.id()).subscribe(c => this.contact.set(c));
  }
}

// ---------- Tasks ----------

@Component({
  selector: 'app-tasks',
  imports: [RouterLink, WhenPipe],
  template: `
    <div class="page narrow">
      <div class="page-head"><div><h1>المهام</h1><p class="sub">ما عليك فعله، مرتب حسب الموعد.</p></div></div>
      @if (tasks(); as t) {
        @for (g of groups(t); track g.title) {
          @if (g.items.length || g.title === 'اليوم') {
            <section class="group">
              <h2 [class.late]="g.late">{{ g.title }} <span class="muted">{{ g.items.length }}</span></h2>
              <div class="card">
                @for (a of g.items; track a.id) {
                  <div class="task" [class.done]="!!a.completedAt">
                    <button type="button" class="check" [class.on]="!!a.completedAt" (click)="toggle(a.id, !!a.completedAt)" [attr.aria-label]="a.subject"></button>
                    <div class="grow">
                      <div class="subject">{{ a.subject }}</div>
                      @if (a.related) {
                        <a class="rel" [routerLink]="['/' + (a.related.kind === 'company' ? 'companies' : a.related.kind + 's'), a.related.id]">{{ a.related.name }}</a>
                      }
                    </div>
                    <span class="when" [class.overdue]="a.isOverdue">{{ (a.completedAt ?? a.dueAt) | when: 'when' }}</span>
                  </div>
                } @empty { <p class="empty">لا مهام لليوم.</p> }
              </div>
            </section>
          }
        }
      } @else { <p class="loading">جارٍ التحميل…</p> }
    </div>
  `,
  styles: `
    .narrow { max-width: 860px; }
    .group { margin-bottom: 24px; }
    .group h2 { font-size: 14px; margin-bottom: 10px; display: flex; gap: 6px; }
    .group h2.late { color: var(--lost); }
    .task { display: flex; align-items: center; gap: 14px; padding: 13px 18px; border-bottom: 1px solid var(--line); }
    .task:last-child { border-bottom: 0; }
    .task.done .subject { text-decoration: line-through; color: var(--muted); }
    .check { width: 20px; height: 20px; border-radius: 6px; border: 1.6px solid var(--line-2); background: var(--surface); cursor: pointer; flex-shrink: 0; }
    .check:hover { border-color: var(--won); }
    .check.on { background: var(--won); border-color: var(--won); }
    .subject { font-weight: 500; font-size: 14px; }
    .rel { font-size: 13px; color: var(--primary); }
    .when { font-size: 13px; color: var(--muted); white-space: nowrap; }
  `
})
export class TasksPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected tasks = signal<MyTasks | null>(null);

  ngOnInit() { this.load(); }

  protected groups(t: MyTasks) {
    return [
      { title: 'متأخرة', items: t.overdue, late: true },
      { title: 'اليوم', items: t.today, late: false },
      { title: 'قادمة', items: t.upcoming, late: false },
      { title: 'أُنجزت مؤخرًا', items: t.recentlyDone, late: false }
    ];
  }

  protected toggle(id: string, done: boolean) {
    (done ? this.api.reopenActivity(id) : this.api.completeActivity(id)).subscribe(() => {
      if (!done) this.toasts.ok('تم الإنجاز.');
      this.load();
    });
  }

  private load() { this.api.tasks().subscribe(t => this.tasks.set(t)); }
}
