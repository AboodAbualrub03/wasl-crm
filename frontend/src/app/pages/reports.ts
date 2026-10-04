import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { MoneyPipe, formatDate, formatMoney, sourceLabel } from '../core/format';
import { Report } from '../core/models';

interface Tip { x: number; y: number; title: string; lines: string[]; }

@Component({
  selector: 'app-reports',
  imports: [MoneyPipe],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>التقارير</h1>
          <p class="sub">{{ auth.isManager() ? 'أرقام الفريق كاملًا' : 'أرقامك الشخصية' }} · تُحسب من سجل الصفقات الفعلي</p>
        </div>
      </div>

      @if (data(); as r) {
        <section class="kpis card">
          <div class="kpi"><span class="label">ربحنا هذا الربع</span><span class="value num">{{ r.wonThisQuarter | money }}</span></div>
          <div class="kpi"><span class="label">نسبة الفوز (90 يومًا)</span><span class="value num">{{ r.winRate === null ? '—' : r.winRate + '%' }}</span></div>
          <div class="kpi"><span class="label">متوسط الصفقة الرابحة</span><span class="value num">{{ r.avgDealSize | money }}</span></div>
          <div class="kpi"><span class="label">متوسط مدة البيع</span><span class="value num">{{ r.avgCycleDays === null ? '—' : r.avgCycleDays + ' يومًا' }}</span></div>
        </section>

        <div class="grid">
          <section class="card chart-card wide">
            <header class="card-head">
              <h2>المبيعات والتوقعات</h2>
              <div class="legend">
                <span><i class="sw won"></i> ربحنا فعلًا</span>
                <span><i class="sw fc"></i> متوقع حسب الاحتمالية</span>
                <span><i class="sw best"></i> أفضل حالة</span>
              </div>
            </header>
            <div class="chart" (mouseleave)="tip.set(null)">
              <div class="grid-lines">
                @for (g of ticks(); track g) { <div class="gl"><span class="num">{{ g | money: true }}</span></div> }
              </div>
              <div class="cols">
                @for (m of forecast(); track m.key) {
                  <div class="mcol" (mousemove)="hover($event, m)">
                    <div class="bars">
                      @if (m.past) {
                        <span class="bar won" [style.height.%]="pct(m.won)"></span>
                      } @else {
                        <span class="bar-stack">
                          <span class="best-box" [style.height.%]="pct(m.best)"></span>
                          <span class="bar fc" [style.height.%]="pct(m.weighted)"></span>
                        </span>
                        @if (m.won) { <span class="bar won" [style.height.%]="pct(m.won)"></span> }
                      }
                    </div>
                    <span class="mlabel" [class.now]="m.current">{{ m.label }}</span>
                  </div>
                }
              </div>
              @if (tip(); as t) {
                <div class="tip" [style.left.px]="t.x" [style.top.px]="t.y">
                  <strong>{{ t.title }}</strong>
                  @for (l of t.lines; track l) { <span>{{ l }}</span> }
                </div>
              }
            </div>
            <p class="note muted small">الأشهر السابقة: ما ربحناه فعلًا. الحالية والقادمة: قيمة الصفقات المفتوحة حسب تاريخ إغلاقها المتوقع، مضروبة في احتمالية مرحلتها.</p>
          </section>

          <section class="card">
            <header class="card-head"><h2>قمع التحويل</h2><span class="muted small">صفقات آخر 6 أشهر</span></header>
            <div class="funnel">
              @for (f of r.funnel; track f.stage; let last = $last) {
                <div class="f-row">
                  <span class="f-name">{{ f.stage }}</span>
                  <span class="f-track"><span class="f-bar" [class.last]="last" [style.width.%]="f.rate"></span></span>
                  <span class="f-val num">{{ f.reached }} <small>{{ f.rate }}%</small></span>
                </div>
              }
            </div>
          </section>

          <section class="card">
            <header class="card-head"><h2>مصادر العملاء المحتملين</h2><span class="muted small">نسبة التحويل لصفقة</span></header>
            <table class="table">
              <thead><tr><th>المصدر</th><th>العدد</th><th>تحوّل</th><th></th></tr></thead>
              <tbody>
                @for (s of r.sources; track s.source) {
                  <tr>
                    <td>{{ sourceLabel[s.source] }}</td>
                    <td class="num">{{ s.leads }}</td>
                    <td class="num">{{ s.converted }}</td>
                    <td class="rate"><span class="mini"><i [style.width.%]="s.rate"></i></span><span class="num">{{ s.rate }}%</span></td>
                  </tr>
                } @empty { <tr><td colspan="4" class="empty">لا بيانات.</td></tr> }
              </tbody>
            </table>
          </section>

          <section class="card wide">
            <header class="card-head"><h2>أداء الفريق</h2><span class="muted small">هذا الربع</span></header>
            <div class="table-wrap">
              <table class="table">
                <thead><tr><th>الموظف</th><th>ربح</th><th>عدد الصفقات</th><th>خط المبيعات المتوقع</th><th class="hide-sm">أنشطة (30 يومًا)</th><th class="hide-sm">نسبة الفوز</th></tr></thead>
                <tbody>
                  @for (p of r.reps; track p.rep.id; let i = $index) {
                    <tr>
                      <td><span class="rank">{{ i + 1 }}</span> {{ p.rep.fullName }}</td>
                      <td class="num strong">{{ p.wonValue | money }}</td>
                      <td class="num">{{ p.wonCount }}</td>
                      <td class="num">{{ p.pipeline | money }}</td>
                      <td class="num hide-sm">{{ p.activities }}</td>
                      <td class="num hide-sm">{{ p.winRate === null ? '—' : p.winRate + '%' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>

          <section class="card">
            <header class="card-head"><h2>لماذا نخسر؟</h2></header>
            @for (l of r.lostReasons; track l.reason) {
              <div class="reason">
                <span class="grow">{{ l.reason }}</span>
                <span class="mini"><i [style.width.%]="l.count / maxReason() * 100"></i></span>
                <span class="num">{{ l.count }}</span>
              </div>
            } @empty { <p class="empty">لا صفقات خاسرة مسجلة.</p> }
          </section>
        </div>
      } @else {
        <p class="loading">جارٍ التحميل…</p>
      }
    </div>
  `,
  styles: `
    .kpis { display: grid; grid-template-columns: repeat(4, 1fr); margin-bottom: 20px; }
    .kpi { padding: 18px 22px; display: flex; flex-direction: column; gap: 4px; border-inline-start: 1px solid var(--line); }
    .kpi:first-child { border-inline-start: 0; }
    .kpi .label { font-size: 13px; color: var(--muted); }
    .kpi .value { font-size: 24px; font-weight: 500; }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; }
    .wide { grid-column: 1 / -1; }
    .legend { display: flex; gap: 16px; font-size: 12px; color: var(--ink-2); flex-wrap: wrap; }
    .legend span { display: flex; align-items: center; gap: 6px; }
    .sw { width: 10px; height: 10px; border-radius: 3px; display: inline-block; }
    .sw.won { background: #2f9068; }
    .sw.fc { background: #4a5fb5; }
    .sw.best { border: 1.5px dashed #4a5fb5; background: transparent; }
    .chart { position: relative; height: 280px; margin: 20px 22px 8px; padding-inline-start: 56px; }
    .grid-lines { position: absolute; inset: 0 0 26px 0; display: flex; flex-direction: column-reverse; justify-content: space-between; pointer-events: none; }
    .gl { border-top: 1px solid var(--line); position: relative; height: 0; }
    .gl span { position: absolute; inset-inline-start: 0; top: -9px; font-size: 11px; color: var(--muted); background: var(--surface); padding-inline-end: 6px; }
    .cols { position: absolute; top: 0; bottom: 0; inset-inline-start: 56px; inset-inline-end: 0; display: grid; grid-template-columns: repeat(6, 1fr); }
    .mcol { display: flex; flex-direction: column; align-items: center; cursor: default; }
    .mcol:hover .bars { background: rgba(74, 95, 181, .05); }
    .bars { flex: 1; width: 100%; display: flex; align-items: flex-end; justify-content: center; gap: 2px; border-radius: 6px 6px 0 0; }
    .bar { width: 22px; border-radius: 4px 4px 0 0; min-height: 2px; display: block; }
    .bar.won { background: #2f9068; }
    .bar.fc { background: #4a5fb5; position: absolute; bottom: 0; inset-inline: 0; }
    .bar-stack { position: relative; width: 22px; height: 100%; display: flex; align-items: flex-end; }
    .best-box { position: absolute; bottom: 0; inset-inline: -3px; border: 1.5px dashed #4a5fb5; border-bottom: 0; border-radius: 5px 5px 0 0; }
    .mlabel { height: 26px; line-height: 26px; font-size: 12px; color: var(--muted); }
    .mlabel.now { color: var(--ink); font-weight: 500; }
    .tip { position: absolute; z-index: 5; transform: translate(-50%, calc(-100% - 10px)); background: var(--nav); color: #eef0f7; padding: 10px 12px; border-radius: 10px; font-size: 12px; display: flex; flex-direction: column; gap: 2px; pointer-events: none; white-space: nowrap; box-shadow: 0 10px 24px rgba(30, 34, 53, .25); }
    .tip strong { font-weight: 500; font-size: 13px; color: #fff; }
    .note { padding: 6px 22px 18px; }
    .funnel { padding: 16px 18px 20px; display: flex; flex-direction: column; gap: 12px; }
    .f-row { display: grid; grid-template-columns: 110px 1fr 70px; align-items: center; gap: 12px; font-size: 13px; }
    .f-track { height: 22px; background: var(--sunk); border-radius: 6px; overflow: hidden; }
    .f-bar { display: block; height: 100%; background: #4a5fb5; border-radius: 6px; min-width: 2px; }
    .f-bar.last { background: #2f9068; }
    .f-val { text-align: end; }
    .f-val small { color: var(--muted); }
    .rate { display: flex; align-items: center; gap: 8px; }
    .mini { width: 70px; height: 6px; background: var(--sunk); border-radius: 3px; overflow: hidden; display: inline-block; }
    .mini i { display: block; height: 100%; background: #4a5fb5; border-radius: 3px; }
    .reason { display: flex; align-items: center; gap: 12px; padding: 11px 18px; border-bottom: 1px solid var(--line); font-size: 14px; }
    .reason:last-child { border-bottom: 0; }
    .reason .mini i { background: var(--lost); }
    .rank { display: inline-grid; place-items: center; width: 22px; height: 22px; border-radius: 50%; background: var(--sunk); font-size: 11px; margin-inline-end: 6px; }
    .strong { font-weight: 500; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } .kpis { grid-template-columns: 1fr 1fr; } }
  `
})
export class ReportsPage implements OnInit {
  private api = inject(Api);
  protected auth = inject(Auth);
  protected data = signal<Report | null>(null);
  protected tip = signal<Tip | null>(null);
  protected sourceLabel = sourceLabel;

  protected forecast = computed(() => {
    const now = new Date();
    return (this.data()?.forecast ?? []).map(f => {
      const past = f.year < now.getFullYear() || (f.year === now.getFullYear() && f.month < now.getMonth() + 1);
      const current = f.year === now.getFullYear() && f.month === now.getMonth() + 1;
      return {
        key: `${f.year}-${f.month}`, label: formatDate(new Date(f.year, f.month - 1, 1), 'month'),
        past, current, won: f.won, weighted: f.weighted, best: f.bestCase
      };
    });
  });

  /** Round axis maximum so the gridlines land on readable numbers. */
  protected axisMax = computed(() => {
    const max = Math.max(1, ...this.forecast().map(m => Math.max(m.won, m.best, m.weighted)));
    const step = Math.pow(10, Math.floor(Math.log10(max)));
    return Math.ceil(max / step) * step;
  });

  protected ticks = computed(() => [0, 0.25, 0.5, 0.75, 1].map(t => Math.round(this.axisMax() * t)));
  protected maxReason = computed(() => Math.max(1, ...(this.data()?.lostReasons ?? []).map(r => r.count)));

  ngOnInit() {
    this.api.reports().subscribe(r => this.data.set(r));
  }

  protected pct(v: number) {
    return (v / this.axisMax()) * 100;
  }

  protected hover(e: MouseEvent, m: ReturnType<typeof this.forecast>[number]) {
    const host = (e.currentTarget as HTMLElement).closest('.chart')!.getBoundingClientRect();
    const col = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const lines = m.past
      ? [`ربحنا: ${formatMoney(m.won)}`]
      : [`متوقع: ${formatMoney(m.weighted)}`, `أفضل حالة: ${formatMoney(m.best)}`, ...(m.won ? [`ربحنا حتى الآن: ${formatMoney(m.won)}`] : [])];
    this.tip.set({ x: col.left - host.left + col.width / 2, y: e.clientY - host.top, title: m.label, lines });
  }
}
