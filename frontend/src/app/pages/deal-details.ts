import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { errorText } from '../core/auth';
import { InitialsPipe, MoneyPipe, WhenPipe } from '../core/format';
import { DealDetails, Stage } from '../core/models';
import { Dialog, Toasts } from '../core/ui';
import { ActivityFeed } from '../shared/activity-feed';
import { DealForm } from '../shared/forms';

@Component({
  selector: 'app-deal-details',
  imports: [RouterLink, FormsModule, MoneyPipe, WhenPipe, InitialsPipe, ActivityFeed, Dialog, DealForm],
  template: `
    @if (deal(); as d) {
      <div class="page">
        <nav class="crumbs"><a routerLink="/pipeline">الصفقات</a> <span>/</span> <span>{{ d.title }}</span></nav>

        <header class="hero card">
          <div class="hero-main">
            <div class="row wrap">
              @switch (d.status) {
                @case ('Won') { <span class="chip won">رابحة · {{ d.closedAt | when }}</span> }
                @case ('Lost') { <span class="chip lost">خاسرة · {{ d.lostReason }}</span> }
                @default {
                  <span class="chip primary">{{ d.stage.name }} · {{ d.stage.probability }}%</span>
                  @if (d.isStale) { <span class="chip accent">تبرد · آخر نشاط {{ d.lastActivityAt | when: 'ago' }}</span> }
                }
              }
            </div>
            <h1>{{ d.title }}</h1>
            <p class="who">
              @if (d.company) { <a [routerLink]="['/companies', d.company.id]">{{ d.company.name }}</a> · }
              <span class="muted">المسؤول: {{ d.owner.fullName }}</span>
            </p>
          </div>
          <div class="hero-side">
            <div class="value num">{{ d.value | money }}</div>
            <div class="muted small">المتوقع حسب المرحلة {{ d.weightedValue | money }}</div>
            <div class="actions">
              @if (d.status === 'Open') {
                <button type="button" class="btn won sm" [disabled]="busy()" (click)="markWon()">ربحنا</button>
                <button type="button" class="btn lost sm" (click)="lossOpen.set(true)">خسرنا</button>
              } @else {
                <button type="button" class="btn sm" [disabled]="busy()" (click)="reopen()">إعادة فتح</button>
              }
              <button type="button" class="btn ghost sm" (click)="editing.set(true)">تعديل</button>
            </div>
          </div>
        </header>

        <ol class="stepper" aria-label="مراحل الصفقة">
          @for (s of openStages(); track s.id; let i = $index) {
            <li>
              <button type="button" [class.done]="isPast(s)" [class.now]="s.id === d.stage.id" [disabled]="busy() || s.id === d.stage.id"
                (click)="moveTo(s)" [title]="'نقل إلى ' + s.name">
                <span class="dot">{{ isPast(s) ? '✓' : i + 1 }}</span>
                <span>{{ s.name }}</span>
              </button>
            </li>
          }
        </ol>

        <div class="layout">
          <section class="main">
            <app-activity-feed [items]="d.activities" [target]="{ dealId: d.id, contactId: d.contact?.id }" (changed)="load()" />
          </section>

          <aside class="aside">
            <section class="card facts">
              <h2>التفاصيل</h2>
              <dl>
                <div><dt>الإغلاق المتوقع</dt><dd>{{ d.expectedClose ? (d.expectedClose | when: 'full') : '—' }}</dd></div>
                <div><dt>في المرحلة الحالية منذ</dt><dd>{{ d.stageEnteredAt | when: 'ago' }}</dd></div>
                <div><dt>آخر نشاط</dt><dd>{{ d.lastActivityAt | when: 'ago' }}</dd></div>
                <div><dt>أُنشئت</dt><dd>{{ d.createdAt | when: 'full' }}</dd></div>
              </dl>
            </section>

            <section class="card person">
              <h2>جهة الاتصال</h2>
              @if (d.contact; as c) {
                <a class="row" [routerLink]="['/contacts', c.id]">
                  <span class="avatar lg">{{ c.fullName | initials }}</span>
                  <span class="grow">
                    <strong>{{ c.fullName }}</strong>
                    <span class="muted small block">{{ c.jobTitle }}</span>
                  </span>
                </a>
                <div class="links">
                  @if (c.phone) { <a class="ltr" [href]="'tel:' + c.phone">{{ c.phone }}</a> }
                  @if (c.email) { <a class="ltr" [href]="'mailto:' + c.email">{{ c.email }}</a> }
                </div>
              } @else {
                <p class="muted small">لا جهة اتصال. اربط واحدة من «تعديل» قبل تسجيل الصفقة كرابحة.</p>
              }
            </section>

            <section class="card">
              <h2 class="pad">رحلة الصفقة</h2>
              <ol class="journey">
                @for (h of d.history; track $index) {
                  <li>
                    <strong>{{ h.to }}</strong>
                    <span class="muted small">{{ h.at | when }} · {{ h.by }}
                      @if (h.daysInPrevious !== null) { · بعد {{ h.daysInPrevious }} يومًا }
                    </span>
                  </li>
                }
              </ol>
            </section>
          </aside>
        </div>
      </div>

      @if (lossOpen()) {
        <app-dialog heading="لماذا خسرنا الصفقة؟" [hint]="d.title" (closed)="lossOpen.set(false)">
          <label class="field">
            <span>السبب</span>
            <input class="input" [(ngModel)]="lossReason" placeholder="مثال: السعر أعلى من الميزانية" />
          </label>
          <div actions>
            <button type="button" class="btn primary" [disabled]="!lossReason.trim() || busy()" (click)="markLost()">تسجيل الخسارة</button>
            <button type="button" class="btn ghost" (click)="lossOpen.set(false)">إلغاء</button>
          </div>
        </app-dialog>
      }

      @if (editing()) {
        <app-deal-form [deal]="d" (closed)="editing.set(false)" (saved)="editing.set(false); load()" />
      }
    } @else if (missing()) {
      <div class="page"><p class="empty">الصفقة غير موجودة أو ليست ضمن صلاحياتك.</p></div>
    } @else {
      <p class="loading">جارٍ التحميل…</p>
    }
  `,
  styles: `
    .crumbs { font-size: 13px; color: var(--muted); margin-bottom: 14px; display: flex; gap: 8px; }
    .crumbs a:hover { color: var(--primary); }
    .hero { display: flex; justify-content: space-between; gap: 24px; padding: 22px 24px; flex-wrap: wrap; }
    .hero h1 { margin-top: 10px; font-size: 24px; }
    .wrap { flex-wrap: wrap; gap: 8px; }
    .who { margin-top: 4px; font-size: 14px; }
    .who a { color: var(--primary); }
    .who a:hover { text-decoration: underline; }
    .hero-side { text-align: end; display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
    .value { font-size: 30px; font-weight: 500; }
    .actions { display: flex; gap: 8px; margin-top: 12px; }
    .stepper { list-style: none; padding: 0; margin: 18px 0 24px; display: flex; gap: 6px; overflow-x: auto; }
    .stepper li { flex: 1; min-width: 120px; }
    .stepper button { width: 100%; display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-radius: 12px; border: 1px solid var(--line); background: var(--surface); font: inherit; font-size: 13px; color: var(--muted); cursor: pointer; }
    .stepper button:hover:not(:disabled) { border-color: var(--primary); color: var(--primary); }
    .stepper .dot { width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; font-size: 11px; background: var(--sunk); flex-shrink: 0; }
    .stepper .done { color: var(--ink-2); }
    .stepper .done .dot { background: var(--primary); color: #fff; }
    .stepper .now { background: var(--primary); border-color: var(--primary); color: #fff; cursor: default; }
    .stepper .now .dot { background: rgba(255, 255, 255, .2); color: #fff; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 330px; gap: 22px; align-items: start; }
    .aside { display: flex; flex-direction: column; gap: 16px; }
    .aside h2 { font-size: 14px; margin-bottom: 10px; }
    .facts, .person { padding: 16px 18px; }
    dl { margin: 0; display: flex; flex-direction: column; gap: 10px; }
    dl div { display: flex; justify-content: space-between; gap: 10px; font-size: 13px; }
    dt { color: var(--muted); }
    dd { margin: 0; text-align: end; }
    .person strong { font-weight: 500; }
    .block { display: block; }
    .links { display: flex; flex-direction: column; gap: 4px; margin-top: 12px; font-size: 13px; color: var(--primary); }
    .pad { padding: 16px 18px 0; }
    .journey { list-style: none; margin: 0; padding: 4px 18px 16px; }
    .journey li { display: flex; flex-direction: column; padding: 8px 0 8px 0; padding-inline-start: 16px; border-inline-start: 2px solid var(--line); position: relative; font-size: 14px; }
    .journey li::before { content: ''; position: absolute; inset-inline-start: -6px; top: 14px; width: 10px; height: 10px; border-radius: 50%; background: var(--surface); border: 2px solid var(--primary); }
    .journey li:last-child::before { background: var(--primary); }
    .journey strong { font-weight: 500; }
    @media (max-width: 980px) { .layout { grid-template-columns: 1fr; } .hero-side { align-items: flex-start; text-align: start; } }
  `
})
export class DealDetailsPage {
  private api = inject(Api);
  private toasts = inject(Toasts);

  readonly id = input.required<string>();
  protected deal = signal<DealDetails | null>(null);
  protected missing = signal(false);
  protected busy = signal(false);
  protected editing = signal(false);
  protected lossOpen = signal(false);
  protected lossReason = '';

  protected openStages = computed(() => this.deal()?.stages.filter(s => s.kind === 'Open') ?? []);

  constructor() {
    // Re-load when the route switches to another deal (e.g. from search) without recreating the page.
    effect(() => {
      this.id();
      untracked(() => { this.deal.set(null); this.load(); });
    });
  }

  protected load() {
    this.api.deal(this.id()).subscribe({
      next: d => this.deal.set(d),
      error: () => this.missing.set(true)
    });
  }

  protected isPast(s: Stage) {
    const d = this.deal()!;
    return d.status === 'Won' || (d.stage.kind === 'Open' && s.order < d.stage.order);
  }

  protected moveTo(s: Stage) {
    this.run(s.id, null, `انتقلت الصفقة إلى «${s.name}»`);
  }

  protected markWon() {
    const won = this.deal()!.stages.find(s => s.kind === 'Won')!;
    this.run(won.id, null, 'مبروك! تم تسجيل الصفقة كرابحة.');
  }

  protected markLost() {
    const lost = this.deal()!.stages.find(s => s.kind === 'Lost')!;
    this.run(lost.id, this.lossReason.trim(), 'تم تسجيل الخسارة.');
  }

  protected reopen() {
    const last = this.openStages().at(-1)!;
    this.run(last.id, null, 'أُعيد فتح الصفقة.');
  }

  private run(stageId: string, reason: string | null, ok: string) {
    this.busy.set(true);
    this.api.moveDeal(this.id(), stageId, reason).subscribe({
      next: () => {
        this.busy.set(false);
        this.lossOpen.set(false);
        this.toasts.ok(ok);
        this.load();
      },
      error: e => {
        this.busy.set(false);
        this.toasts.error(errorText(e));
      }
    });
  }
}
