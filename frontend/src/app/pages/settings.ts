import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../core/api';
import { errorText } from '../core/auth';
import { roleLabel } from '../core/format';
import { Role, Settings, Stage, TeamMember } from '../core/models';
import { Dialog, Toasts } from '../core/ui';

interface EditableStage { id: string | null; name: string; probability: number; }

@Component({
  selector: 'app-settings',
  imports: [FormsModule, Dialog],
  template: `
    <div class="page narrow">
      <div class="page-head"><div><h1>الإعدادات</h1><p class="sub">{{ settings()?.name }} · رمز مساحة العمل <span class="ltr">{{ settings()?.code }}</span></p></div></div>

      <section class="card block">
        <header class="card-head"><h2>مراحل البيع</h2><span class="muted small">الاحتمالية تحدد التوقعات</span></header>
        <div class="stages">
          @for (s of stages(); track $index; let i = $index; let first = $first; let last = $last) {
            <div class="stage-row">
              <span class="idx">{{ i + 1 }}</span>
              <input class="input" [(ngModel)]="s.name" />
              <label class="prob"><input class="input num" type="number" min="0" max="100" [(ngModel)]="s.probability" /><span>%</span></label>
              <div class="row">
                <button type="button" class="btn ghost sm" [disabled]="first" (click)="move(i, -1)" aria-label="أعلى">↑</button>
                <button type="button" class="btn ghost sm" [disabled]="last" (click)="move(i, 1)" aria-label="أسفل">↓</button>
                <button type="button" class="btn ghost sm lost" [disabled]="stages().length === 1" (click)="remove(i)">حذف</button>
              </div>
            </div>
          }
          <button type="button" class="btn sm add" (click)="add()">إضافة مرحلة</button>
          <div class="closing grid-2">
            <label class="field"><span>اسم مرحلة الربح</span><input class="input" [(ngModel)]="wonName" /></label>
            <label class="field"><span>اسم مرحلة الخسارة</span><input class="input" [(ngModel)]="lostName" /></label>
          </div>
        </div>
        <footer class="foot"><button type="button" class="btn primary" [disabled]="busy()" (click)="savePipeline()">حفظ المراحل</button></footer>
      </section>

      <section class="card block">
        <header class="card-head"><h2>الصفقات التي تبرد</h2></header>
        <div class="pad row">
          <span>نبّه المسؤول عن الصفقة إذا مرّ عليها</span>
          <input class="input num tiny" type="number" min="1" max="90" [(ngModel)]="staleDays" />
          <span>أيام بلا أي نشاط.</span>
          <span class="grow"></span>
          <button type="button" class="btn sm" (click)="saveStale()">حفظ</button>
        </div>
      </section>

      @if (settings()?.intakeKey; as key) {
        <section class="card block">
          <header class="card-head"><h2>نموذج موقعك الإلكتروني</h2><span class="muted small">كل من يعبّئه يصبح عميلًا محتملًا ويُوزَّع تلقائيًا</span></header>
          <div class="pad">
            <p class="muted small">ضع هذا الكود في أي صفحة على موقعك:</p>
            <pre class="snippet ltr">{{ snippet() }}</pre>
            <div class="row">
              <button type="button" class="btn sm" (click)="copy()">نسخ الكود</button>
              <button type="button" class="btn ghost sm lost" (click)="rotate()">تغيير المفتاح (يوقف النماذج القديمة)</button>
            </div>
          </div>
        </section>
      }

      <section class="card block">
        <header class="card-head"><h2>الفريق</h2><button type="button" class="btn sm" (click)="adding.set(true)">إضافة عضو</button></header>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>الاسم</th><th>الدور</th><th>عملاء مفتوحون</th><th>صفقات مفتوحة</th><th>يستقبل عملاء جددًا</th><th>فعّال</th></tr></thead>
            <tbody>
              @for (m of team(); track m.id) {
                <tr [class.off]="!m.isActive">
                  <td><div class="title">{{ m.fullName }}</div><div class="sub ltr">{{ m.email }}</div></td>
                  <td>
                    <select class="select mini-select" [ngModel]="m.role" (ngModelChange)="update(m, { role: $event })">
                      @for (r of roles; track r) { <option [value]="r">{{ roleLabel[r] }}</option> }
                    </select>
                  </td>
                  <td class="num">{{ m.openLeads }}</td>
                  <td class="num">{{ m.openDeals }}</td>
                  <td><input type="checkbox" class="toggle" [checked]="m.receivesLeads" (change)="update(m, { receivesLeads: !m.receivesLeads })" /></td>
                  <td><input type="checkbox" class="toggle" [checked]="m.isActive" (change)="update(m, { isActive: !m.isActive })" /></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    </div>

    @if (adding()) {
      <app-dialog heading="عضو جديد" (closed)="adding.set(false)">
        <div class="grid-2">
          <label class="field full"><span>الاسم</span><input class="input" [(ngModel)]="member.fullName" /></label>
          <label class="field"><span>البريد</span><input class="input ltr-in" type="email" [(ngModel)]="member.email" /></label>
          <label class="field"><span>كلمة المرور</span><input class="input ltr-in" type="password" [(ngModel)]="member.password" /><small class="hint">8 أحرف على الأقل</small></label>
          <label class="field">
            <span>الدور</span>
            <select class="select" [(ngModel)]="member.role">
              @for (r of roles; track r) { <option [value]="r">{{ roleLabel[r] }}</option> }
            </select>
          </label>
        </div>
        <div actions>
          <button type="button" class="btn primary" [disabled]="busy()" (click)="addMember()">إضافة</button>
          <button type="button" class="btn ghost" (click)="adding.set(false)">إلغاء</button>
        </div>
      </app-dialog>
    }
  `,
  styles: `
    .narrow { max-width: 920px; }
    .block { margin-bottom: 20px; }
    .pad { padding: 16px 18px; display: flex; flex-direction: column; gap: 12px; }
    .pad.row { flex-direction: row; flex-wrap: wrap; font-size: 14px; }
    .stages { padding: 14px 18px; display: flex; flex-direction: column; gap: 8px; }
    .stage-row { display: grid; grid-template-columns: 26px 1fr 100px auto; gap: 10px; align-items: center; }
    .idx { width: 26px; height: 26px; border-radius: 50%; background: var(--primary-soft); color: var(--primary); display: grid; place-items: center; font-size: 12px; }
    .prob { display: flex; align-items: center; gap: 4px; }
    .prob .input { text-align: center; }
    .add { align-self: flex-start; margin-top: 4px; }
    .closing { margin-top: 12px; padding-top: 14px; border-top: 1px dashed var(--line); }
    .foot { padding: 0 18px 16px; }
    .tiny { width: 70px; height: 34px; text-align: center; }
    .snippet { background: var(--nav); color: #dfe2f2; padding: 14px 16px; border-radius: 12px; font-size: 12px; overflow-x: auto; white-space: pre; margin: 0; font-family: ui-monospace, monospace; text-align: left; }
    .mini-select { height: 32px; width: auto; font-size: 13px; }
    .toggle { width: 18px; height: 18px; accent-color: var(--primary); }
    tr.off td { opacity: .5; }
    .ltr-in { direction: ltr; text-align: left; }
    @media (max-width: 700px) { .stage-row { grid-template-columns: 26px 1fr 80px; } .stage-row .row { grid-column: 2 / -1; } }
  `
})
export class SettingsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);

  protected settings = signal<Settings | null>(null);
  protected stages = signal<EditableStage[]>([]);
  protected team = signal<TeamMember[]>([]);
  protected busy = signal(false);
  protected adding = signal(false);
  protected wonName = '';
  protected lostName = '';
  protected staleDays = 10;
  protected roles: Role[] = ['Rep', 'Manager', 'Admin'];
  protected roleLabel = roleLabel;
  protected member = { fullName: '', email: '', password: '', role: 'Rep' as Role };

  protected snippet = computed(() => {
    const key = this.settings()?.intakeKey;
    return `<form onsubmit="event.preventDefault(); fetch('${location.origin.replace('4210', '5021')}/api/public/leads/${key}', {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(Object.fromEntries(new FormData(this)))
}).then(() => this.innerHTML = 'شكرًا، سنتواصل معك قريبًا');">
  <input name="fullName" placeholder="الاسم" required>
  <input name="companyName" placeholder="الشركة">
  <input name="email" type="email" placeholder="البريد">
  <input name="phone" placeholder="الهاتف">
  <textarea name="message" placeholder="كيف نساعدك؟"></textarea>
  <button>أرسل</button>
</form>`;
  });

  ngOnInit() {
    this.api.settings().subscribe(s => { this.settings.set(s); this.staleDays = s.staleAfterDays; });
    this.loadStages();
    this.loadTeam();
  }

  protected move(i: number, dir: number) {
    const list = [...this.stages()];
    [list[i], list[i + dir]] = [list[i + dir], list[i]];
    this.stages.set(list);
  }

  protected remove(i: number) {
    this.stages.update(l => l.filter((_, j) => j !== i));
  }

  protected add() {
    this.stages.update(l => [...l, { id: null, name: 'مرحلة جديدة', probability: 50 }]);
  }

  protected savePipeline() {
    this.busy.set(true);
    this.api.savePipeline({
      openStages: this.stages().map(s => ({ id: s.id, name: s.name, probability: Number(s.probability) })),
      wonName: this.wonName, lostName: this.lostName
    }).subscribe({
      next: s => { this.busy.set(false); this.apply(s); this.toasts.ok('تم حفظ المراحل.'); },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); this.loadStages(); }
    });
  }

  protected saveStale() {
    this.api.setStaleDays(Number(this.staleDays)).subscribe({
      next: () => this.toasts.ok('تم الحفظ.'),
      error: e => this.toasts.error(errorText(e))
    });
  }

  protected copy() {
    navigator.clipboard?.writeText(this.snippet()).then(() => this.toasts.ok('تم نسخ الكود.'));
  }

  protected rotate() {
    this.api.rotateKey().subscribe(r => {
      this.settings.update(s => s && { ...s, intakeKey: r.key });
      this.toasts.ok('تم تغيير المفتاح. حدّث الكود على موقعك.');
    });
  }

  protected update(m: TeamMember, change: Partial<TeamMember>) {
    const next = { ...m, ...change };
    this.api.updateMember(m.id, { fullName: next.fullName, role: next.role, isActive: next.isActive, receivesLeads: next.receivesLeads }).subscribe({
      next: () => { this.toasts.ok('تم التحديث.'); this.loadTeam(); },
      error: e => { this.toasts.error(errorText(e)); this.loadTeam(); }
    });
  }

  protected addMember() {
    this.busy.set(true);
    this.api.addMember({ ...this.member, receivesLeads: this.member.role === 'Rep' }).subscribe({
      next: () => {
        this.busy.set(false);
        this.adding.set(false);
        this.member = { fullName: '', email: '', password: '', role: 'Rep' };
        this.toasts.ok('تمت الإضافة.');
        this.loadTeam();
      },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); }
    });
  }

  private loadStages() {
    this.api.stages().subscribe(s => this.apply(s));
  }

  private apply(s: Stage[]) {
    this.stages.set(s.filter(x => x.kind === 'Open').map(x => ({ id: x.id, name: x.name, probability: x.probability })));
    this.wonName = s.find(x => x.kind === 'Won')?.name ?? '';
    this.lostName = s.find(x => x.kind === 'Lost')?.name ?? '';
  }

  private loadTeam() {
    this.api.team().subscribe(t => this.team.set(t));
  }
}
