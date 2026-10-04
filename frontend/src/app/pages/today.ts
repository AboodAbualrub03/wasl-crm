import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { errorText } from '../core/auth';
import { MoneyPipe, WhenPipe, formatDate, sourceLabel } from '../core/format';
import { ActivityItem, Home } from '../core/models';
import { Toasts } from '../core/ui';
import { DealForm } from '../shared/forms';

@Component({
  selector: 'app-today',
  imports: [RouterLink, MoneyPipe, WhenPipe, DealForm],
  template: `
    <div class="page">
      @if (data(); as d) {
        <div class="page-head">
          <div>
            <h1>{{ greeting }}، {{ d.firstName }}</h1>
            <p class="sub">{{ today }}</p>
          </div>
          <button type="button" class="btn accent" (click)="newDeal.set(true)">صفقة جديدة</button>
        </div>

        <section class="kpis card">
          <a routerLink="/pipeline" class="kpi">
            <span class="label">صفقاتي المفتوحة</span>
            <span class="value num">{{ d.openDeals }}</span>
          </a>
          <a routerLink="/pipeline" class="kpi">
            <span class="label">قيمة خط مبيعاتي</span>
            <span class="value num">{{ d.openValue | money }}</span>
          </a>
          <a routerLink="/reports" class="kpi">
            <span class="label">المتوقع حسب الاحتمالية</span>
            <span class="value num">{{ d.weighted | money }}</span>
          </a>
          <a routerLink="/reports" class="kpi won">
            <span class="label">ربحته هذا الشهر</span>
            <span class="value num">{{ d.wonThisMonth | money }}</span>
          </a>
        </section>

        <div class="cols">
          <section class="card">
            <header class="card-head">
              <h2>جدول اليوم
                @if (d.overdueCount) { <span class="chip lost">{{ d.overdueCount }} متأخرة</span> }
              </h2>
              <a routerLink="/tasks">كل المهام</a>
            </header>
            @for (t of d.agenda; track t.id) {
              <div class="agenda-row" [class.late]="t.isOverdue">
                <button type="button" class="check" (click)="complete(t)" [attr.aria-label]="'إنجاز ' + t.subject"></button>
                <div class="grow">
                  <div class="t-subject">{{ t.subject }}</div>
                  @if (t.related) {
                    <a class="t-rel" [routerLink]="['/' + (t.related.kind === 'company' ? 'companies' : t.related.kind + 's'), t.related.id]">{{ t.related.name }}</a>
                  }
                </div>
                <span class="t-when" [class.overdue]="t.isOverdue">{{ t.dueAt | when: 'when' }}</span>
              </div>
            } @empty {
              <p class="empty">لا مهام لليوم. يوم مناسب للتواصل مع عميل جديد.</p>
            }
          </section>

          <div class="side">
            <section class="card">
              <header class="card-head">
                <h2>صفقات تبرد</h2>
                <span class="muted small">بلا نشاط مؤخرًا</span>
              </header>
              @for (s of d.staleDeals; track s.id) {
                <a class="list-row" [routerLink]="['/deals', s.id]">
                  <span class="cold-bar"></span>
                  <div class="grow">
                    <div class="t-subject">{{ s.title }}</div>
                    <div class="muted small">{{ s.companyName }} · صامتة منذ {{ s.daysSilent }} يومًا</div>
                  </div>
                  <span class="num strong">{{ s.value | money: true }}</span>
                </a>
              } @empty {
                <p class="empty">كل صفقاتك نشطة.</p>
              }
            </section>

            <section class="card">
              <header class="card-head">
                <h2>أعلى العملاء المحتملين تقييمًا</h2>
                <a routerLink="/leads">الكل</a>
              </header>
              @for (l of d.hotLeads; track l.id) {
                <a class="list-row" [routerLink]="['/leads', l.id]">
                  <div class="grow">
                    <div class="t-subject">{{ l.fullName }}</div>
                    <div class="muted small">{{ l.companyName ?? sourceLabel[l.source] }}</div>
                  </div>
                  <span class="score" [class.hot]="l.score >= 70" [class.warm]="l.score >= 45 && l.score < 70">
                    <span class="bar"><i [style.width.%]="l.score"></i></span>{{ l.score }}
                  </span>
                </a>
              } @empty {
                <p class="empty">لا عملاء محتملين مفتوحين.</p>
              }
            </section>
          </div>
        </div>
      } @else {
        <p class="loading">جارٍ التحميل…</p>
      }
    </div>

    @if (newDeal()) {
      <app-deal-form (closed)="newDeal.set(false)" (saved)="openDeal($event)" />
    }
  `,
  styles: `
    .kpis { display: grid; grid-template-columns: repeat(4, 1fr); margin-bottom: 22px; overflow: hidden; }
    .kpi { padding: 18px 22px; display: flex; flex-direction: column; gap: 4px; border-inline-start: 1px solid var(--line); }
    .kpi:first-child { border-inline-start: 0; }
    .kpi:hover { background: #fbf8f1; }
    .kpi .label { font-size: 13px; color: var(--muted); }
    .kpi .value { font-size: 26px; font-weight: 500; }
    .kpi.won .value { color: var(--won); }
    .cols { display: grid; grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr); gap: 20px; align-items: start; }
    .side { display: flex; flex-direction: column; gap: 20px; }
    h2 { display: flex; align-items: center; gap: 8px; }
    .agenda-row { display: flex; align-items: center; gap: 14px; padding: 13px 18px; border-bottom: 1px solid var(--line); }
    .agenda-row:last-child { border-bottom: 0; }
    .agenda-row.late { background: #fdf6f4; }
    .check { width: 20px; height: 20px; border-radius: 6px; border: 1.6px solid var(--line-2); background: var(--surface); cursor: pointer; flex-shrink: 0; }
    .check:hover { border-color: var(--won); background: var(--won-soft); }
    .t-subject { font-weight: 500; font-size: 14px; }
    .t-rel { font-size: 13px; color: var(--primary); }
    .t-rel:hover { text-decoration: underline; }
    .t-when { font-size: 13px; color: var(--muted); white-space: nowrap; }
    .list-row { display: flex; align-items: center; gap: 12px; padding: 12px 18px; border-bottom: 1px solid var(--line); }
    .list-row:last-child { border-bottom: 0; }
    .list-row:hover { background: #fbf8f1; }
    .cold-bar { width: 3px; align-self: stretch; border-radius: 3px; background: var(--accent); }
    .strong { font-weight: 500; }
    @media (max-width: 960px) { .cols { grid-template-columns: 1fr; } .kpis { grid-template-columns: 1fr 1fr; } .kpi:nth-child(3) { border-inline-start: 0; } .kpi:nth-child(n+3) { border-top: 1px solid var(--line); } }
  `
})
export class TodayPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private router = inject(Router);

  protected data = signal<Home | null>(null);
  protected newDeal = signal(false);
  protected sourceLabel = sourceLabel;
  protected greeting = new Date().getHours() < 12 ? 'صباح الخير' : 'مساء الخير';
  protected today = formatDate(new Date(), 'full');

  ngOnInit() {
    this.load();
  }

  protected complete(t: ActivityItem) {
    this.api.completeActivity(t.id).subscribe({
      next: () => { this.toasts.ok('أحسنت، تم إنجاز المهمة.'); this.load(); },
      error: e => this.toasts.error(errorText(e))
    });
  }

  protected openDeal(id: string) {
    this.router.navigate(['/deals', id]);
  }

  private load() {
    this.api.home().subscribe(d => this.data.set(d));
  }
}
