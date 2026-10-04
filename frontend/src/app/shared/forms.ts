import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, Subject, debounceTime, switchMap } from 'rxjs';
import { Api } from '../core/api';
import { Auth, errorText } from '../core/auth';
import { sourceLabel, sources } from '../core/format';
import { CompanyDetails, ContactDetails, ContactItem, DealDetails, Duplicate, LeadDetails, LeadSource, Ref, Stage, TeamMember } from '../core/models';
import { Dialog } from '../core/ui';

/** Small helper so every form reports errors and busy state the same way. */
abstract class FormBase {
  protected busy = signal(false);
  protected error = signal('');

  protected submitWith<T>(call: Observable<T>, done: (r: T) => void) {
    this.busy.set(true);
    this.error.set('');
    call.subscribe({
      next: r => { this.busy.set(false); done(r); },
      error: e => { this.busy.set(false); this.error.set(errorText(e)); }
    });
  }
}

/** The company pickers need the whole directory; it is small, so page through it once. */
function allCompanies(api: Api, done: (list: Ref[]) => void) {
  const load = (page: number, acc: Ref[]) => api.companies('', page).subscribe(r => {
    const all = [...acc, ...r.items.map(c => ({ id: c.id, name: c.name }))];
    if (all.length < r.total && r.items.length) load(page + 1, all);
    else done(all);
  });
  load(1, []);
}

function dateInput(v: string | null | undefined) {
  return v ? v.slice(0, 10) : '';
}

// ---------- Deal ----------

@Component({
  selector: 'app-deal-form',
  imports: [FormsModule, Dialog],
  template: `
    <app-dialog [heading]="deal() ? 'تعديل الصفقة' : 'صفقة جديدة'" [width]="560" (closed)="closed.emit()">
      <div class="grid-2">
        <label class="field full">
          <span>عنوان الصفقة</span>
          <input class="input" [(ngModel)]="m.title" placeholder="مثال: نظام نقاط بيع لثلاثة فروع" />
        </label>
        <label class="field">
          <span>القيمة ($)</span>
          <input class="input" type="number" min="0" step="100" [(ngModel)]="m.value" />
        </label>
        <label class="field">
          <span>تاريخ الإغلاق المتوقع</span>
          <input class="input" type="date" [(ngModel)]="m.expectedClose" />
        </label>
        <label class="field">
          <span>الشركة</span>
          <select class="select" [(ngModel)]="m.companyId" (ngModelChange)="loadContacts()">
            <option value="">بدون شركة</option>
            @for (c of companies(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
          </select>
        </label>
        <label class="field">
          <span>جهة الاتصال</span>
          <select class="select" [(ngModel)]="m.contactId">
            <option value="">بدون</option>
            @for (c of contacts(); track c.id) { <option [value]="c.id">{{ c.fullName }}{{ c.jobTitle ? ' · ' + c.jobTitle : '' }}</option> }
          </select>
        </label>
        @if (!deal()) {
          <label class="field">
            <span>المرحلة</span>
            <select class="select" [(ngModel)]="m.stageId">
              @for (s of stages(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
            </select>
          </label>
        }
        @if (auth.isManager()) {
          <label class="field">
            <span>المسؤول</span>
            <select class="select" [(ngModel)]="m.ownerId">
              @for (u of team(); track u.id) { <option [value]="u.id">{{ u.fullName }}</option> }
            </select>
          </label>
        }
      </div>
      @if (error()) { <p class="err-line">{{ error() }}</p> }
      <div actions>
        <button type="button" class="btn primary" [disabled]="busy() || !m.title.trim()" (click)="save()">حفظ</button>
        <button type="button" class="btn ghost" (click)="closed.emit()">إلغاء</button>
      </div>
    </app-dialog>
  `,
  styles: `.err-line { color: var(--lost); font-size: 14px; }`
})
export class DealForm extends FormBase implements OnInit {
  private api = inject(Api);
  protected auth = inject(Auth);

  readonly deal = input<DealDetails | null>(null);
  readonly presetCompanyId = input<string | null>(null);
  readonly presetContactId = input<string | null>(null);
  readonly closed = output<void>();
  readonly saved = output<string>();

  protected companies = signal<Ref[]>([]);
  protected contacts = signal<ContactItem[]>([]);
  protected stages = signal<Stage[]>([]);
  protected team = signal<TeamMember[]>([]);
  protected m = { title: '', value: 0, expectedClose: '', companyId: '', contactId: '', stageId: '', ownerId: '' };

  ngOnInit() {
    const d = this.deal();
    this.m = d
      ? { title: d.title, value: d.value, expectedClose: dateInput(d.expectedClose), companyId: d.company?.id ?? '', contactId: d.contact?.id ?? '', stageId: d.stage.id, ownerId: d.owner.id }
      : { ...this.m, companyId: this.presetCompanyId() ?? '', contactId: this.presetContactId() ?? '', ownerId: this.auth.user()!.id };

    allCompanies(this.api, list => this.companies.set(list));
    this.loadContacts();
    if (!d) this.api.stages().subscribe(s => {
      const open = s.filter(x => x.kind === 'Open');
      this.stages.set(open);
      this.m.stageId = open[0]?.id ?? '';
    });
    if (this.auth.isManager()) this.api.team().subscribe(t => this.team.set(t.filter(u => u.isActive)));
  }

  protected loadContacts() {
    if (!this.m.companyId) {
      this.contacts.set([]);
      if (!this.presetContactId()) this.m.contactId = '';
      return;
    }
    this.api.contacts('', 1, this.m.companyId).subscribe(r => {
      this.contacts.set(r.items);
      if (!r.items.some(c => c.id === this.m.contactId)) this.m.contactId = r.items[0]?.id ?? '';
    });
  }

  protected save() {
    const body = {
      title: this.m.title, value: Number(this.m.value) || 0,
      expectedClose: this.m.expectedClose ? new Date(this.m.expectedClose).toISOString() : null,
      companyId: this.m.companyId || null, contactId: this.m.contactId || null,
      ownerId: this.m.ownerId || null, stageId: this.m.stageId || null
    };
    const d = this.deal();
    if (d) this.submitWith(this.api.updateDeal(d.id, body), () => this.saved.emit(d.id));
    else this.submitWith(this.api.createDeal(body), r => this.saved.emit(r.id));
  }
}

// ---------- Lead ----------

@Component({
  selector: 'app-lead-form',
  imports: [FormsModule, Dialog, RouterLink],
  template: `
    <app-dialog [heading]="lead() ? 'تعديل العميل المحتمل' : 'عميل محتمل جديد'" [width]="560" (closed)="closed.emit()">
      <div class="grid-2">
        <label class="field">
          <span>الاسم</span>
          <input class="input" [(ngModel)]="m.fullName" />
        </label>
        <label class="field">
          <span>الشركة</span>
          <input class="input" [(ngModel)]="m.companyName" />
        </label>
        <label class="field">
          <span>البريد</span>
          <input class="input ltr-in" type="email" [(ngModel)]="m.email" (ngModelChange)="check()" />
        </label>
        <label class="field">
          <span>الهاتف</span>
          <input class="input ltr-in" [(ngModel)]="m.phone" (ngModelChange)="check()" />
        </label>
        @if (dups().length) {
          <div class="dups full">
            <strong>قد يكون موجودًا مسبقًا:</strong>
            @for (d of dups(); track d.id) {
              <a [routerLink]="[d.kind === 'lead' ? '/leads' : '/contacts', d.id]" target="_blank">
                {{ d.name }}{{ d.detail ? ' · ' + d.detail : '' }} <span>({{ d.reason }})</span>
              </a>
            }
          </div>
        }
        <label class="field">
          <span>المصدر</span>
          <select class="select" [(ngModel)]="m.source">
            @for (s of sources; track s) { <option [value]="s">{{ sourceLabel[s] }}</option> }
          </select>
        </label>
        <label class="field">
          <span>القيمة المتوقعة ($)</span>
          <input class="input" type="number" min="0" step="500" [(ngModel)]="m.estimatedValue" />
        </label>
        <label class="field full">
          <span>مهتم بـ</span>
          <input class="input" [(ngModel)]="m.interest" placeholder="مثال: نظام حجوزات لعيادتين" />
        </label>
        @if (!lead() && auth.isManager()) {
          <label class="field full">
            <span>الإسناد</span>
            <select class="select" [(ngModel)]="m.ownerId">
              <option value="">توزيع تلقائي على الأقل ضغطًا</option>
              @for (u of team(); track u.id) { <option [value]="u.id">{{ u.fullName }} ({{ u.openLeads }} مفتوح)</option> }
            </select>
          </label>
        }
      </div>
      @if (error()) { <p class="err-line">{{ error() }}</p> }
      <div actions>
        <button type="button" class="btn primary" [disabled]="busy() || !m.fullName.trim()" (click)="save()">حفظ</button>
        <button type="button" class="btn ghost" (click)="closed.emit()">إلغاء</button>
      </div>
    </app-dialog>
  `,
  styles: `
    .err-line { color: var(--lost); font-size: 14px; }
    .ltr-in { direction: ltr; text-align: left; }
    .dups { background: var(--warn-soft); border-radius: 10px; padding: 10px 14px; font-size: 13px; display: flex; flex-direction: column; gap: 4px; color: var(--warn); }
    .dups strong { font-weight: 500; }
    .dups a { color: var(--ink); }
    .dups a:hover { text-decoration: underline; }
    .dups span { color: var(--muted); }
  `
})
export class LeadForm extends FormBase implements OnInit {
  private api = inject(Api);
  protected auth = inject(Auth);

  readonly lead = input<LeadDetails | null>(null);
  readonly closed = output<void>();
  readonly saved = output<string>();

  protected sources = sources;
  protected sourceLabel = sourceLabel;
  protected team = signal<TeamMember[]>([]);
  protected dups = signal<Duplicate[]>([]);
  private check$ = new Subject<void>();
  protected m = { fullName: '', companyName: '', email: '', phone: '', source: 'Website' as LeadSource, estimatedValue: null as number | null, interest: '', ownerId: '' };

  ngOnInit() {
    const l = this.lead();
    if (l) this.m = { fullName: l.fullName, companyName: l.companyName ?? '', email: l.email ?? '', phone: l.phone ?? '', source: l.source, estimatedValue: l.estimatedValue, interest: l.interest ?? '', ownerId: '' };
    if (this.auth.isManager()) this.api.team().subscribe(t => this.team.set(t.filter(u => u.isActive)));

    this.check$.pipe(
      debounceTime(400),
      switchMap(() => this.api.duplicates(this.m.email || null, this.m.phone || null, l?.id))
    ).subscribe(d => this.dups.set(d));
  }

  protected check() {
    this.check$.next();
  }

  protected save() {
    const body = {
      fullName: this.m.fullName, companyName: this.m.companyName || null, email: this.m.email || null,
      phone: this.m.phone || null, source: this.m.source, estimatedValue: this.m.estimatedValue || null,
      interest: this.m.interest || null, ownerId: this.m.ownerId || null
    };
    const l = this.lead();
    if (l) this.submitWith(this.api.updateLead(l.id, body), () => this.saved.emit(l.id));
    else this.submitWith(this.api.createLead(body), r => this.saved.emit(r.id));
  }
}

// ---------- Company ----------

@Component({
  selector: 'app-company-form',
  imports: [FormsModule, Dialog],
  template: `
    <app-dialog [heading]="company() ? 'تعديل الشركة' : 'شركة جديدة'" (closed)="closed.emit()">
      <div class="grid-2">
        <label class="field full"><span>الاسم</span><input class="input" [(ngModel)]="m.name" /></label>
        <label class="field"><span>القطاع</span><input class="input" [(ngModel)]="m.industry" /></label>
        <label class="field"><span>المدينة</span><input class="input" [(ngModel)]="m.city" /></label>
        <label class="field"><span>الموقع الإلكتروني</span><input class="input ltr-in" [(ngModel)]="m.website" /></label>
        <label class="field"><span>الهاتف</span><input class="input ltr-in" [(ngModel)]="m.phone" /></label>
      </div>
      @if (error()) { <p class="err-line">{{ error() }}</p> }
      <div actions>
        <button type="button" class="btn primary" [disabled]="busy() || !m.name.trim()" (click)="save()">حفظ</button>
        <button type="button" class="btn ghost" (click)="closed.emit()">إلغاء</button>
      </div>
    </app-dialog>
  `,
  styles: `.err-line { color: var(--lost); font-size: 14px; } .ltr-in { direction: ltr; text-align: left; }`
})
export class CompanyForm extends FormBase implements OnInit {
  private api = inject(Api);
  readonly company = input<CompanyDetails | null>(null);
  readonly closed = output<void>();
  readonly saved = output<string>();
  protected m = { name: '', industry: '', city: '', website: '', phone: '' };

  ngOnInit() {
    const c = this.company();
    if (c) this.m = { name: c.name, industry: c.industry ?? '', city: c.city ?? '', website: c.website ?? '', phone: c.phone ?? '' };
  }

  protected save() {
    const c = this.company();
    const body = { ...this.m, ownerId: c?.owner.id ?? null };
    if (c) this.submitWith(this.api.updateCompany(c.id, body), () => this.saved.emit(c.id));
    else this.submitWith(this.api.createCompany(body), r => this.saved.emit(r.id));
  }
}

// ---------- Contact ----------

@Component({
  selector: 'app-contact-form',
  imports: [FormsModule, Dialog],
  template: `
    <app-dialog [heading]="contact() ? 'تعديل جهة الاتصال' : 'جهة اتصال جديدة'" (closed)="closed.emit()">
      <div class="grid-2">
        <label class="field full"><span>الاسم</span><input class="input" [(ngModel)]="m.fullName" /></label>
        <label class="field"><span>المسمى الوظيفي</span><input class="input" [(ngModel)]="m.jobTitle" /></label>
        <label class="field">
          <span>الشركة</span>
          <select class="select" [(ngModel)]="m.companyId">
            <option value="">بدون</option>
            @for (c of companies(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
          </select>
        </label>
        <label class="field"><span>البريد</span><input class="input ltr-in" type="email" [(ngModel)]="m.email" /></label>
        <label class="field"><span>الهاتف</span><input class="input ltr-in" [(ngModel)]="m.phone" /></label>
      </div>
      @if (error()) { <p class="err-line">{{ error() }}</p> }
      <div actions>
        <button type="button" class="btn primary" [disabled]="busy() || !m.fullName.trim()" (click)="save()">حفظ</button>
        <button type="button" class="btn ghost" (click)="closed.emit()">إلغاء</button>
      </div>
    </app-dialog>
  `,
  styles: `.err-line { color: var(--lost); font-size: 14px; } .ltr-in { direction: ltr; text-align: left; }`
})
export class ContactForm extends FormBase implements OnInit {
  private api = inject(Api);
  readonly contact = input<ContactDetails | null>(null);
  readonly presetCompanyId = input<string | null>(null);
  readonly closed = output<void>();
  readonly saved = output<string>();
  protected companies = signal<Ref[]>([]);
  protected m = { fullName: '', jobTitle: '', companyId: '', email: '', phone: '' };

  ngOnInit() {
    const c = this.contact();
    this.m = c
      ? { fullName: c.fullName, jobTitle: c.jobTitle ?? '', companyId: c.company?.id ?? '', email: c.email ?? '', phone: c.phone ?? '' }
      : { ...this.m, companyId: this.presetCompanyId() ?? '' };
    allCompanies(this.api, list => this.companies.set(list));
  }

  protected save() {
    const c = this.contact();
    const body = { ...this.m, companyId: this.m.companyId || null, email: this.m.email || null, phone: this.m.phone || null, jobTitle: this.m.jobTitle || null, ownerId: c?.owner.id ?? null };
    if (c) this.submitWith(this.api.updateContact(c.id, body), () => this.saved.emit(c.id));
    else this.submitWith(this.api.createContact(body), r => this.saved.emit(r.id));
  }
}
