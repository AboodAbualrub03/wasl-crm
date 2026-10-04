import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { Api } from '../core/api';
import { Auth, errorText } from '../core/auth';
import { MoneyPipe, WhenPipe, leadStatusLabel, sourceLabel, sources } from '../core/format';
import { LeadItem, LeadStatus, Paged, TeamMember } from '../core/models';
import { Toasts } from '../core/ui';
import { LeadForm } from '../shared/forms';

@Component({
  selector: 'app-leads',
  imports: [FormsModule, MoneyPipe, WhenPipe, LeadForm],
  template: `
    <div class="page">
      <div class="page-head">
        <div>
          <h1>العملاء المحتملون</h1>
          <p class="sub">مرتبون حسب التقييم: ابدأ من الأعلى.</p>
        </div>
        <button type="button" class="btn accent" (click)="creating.set(true)">عميل محتمل جديد</button>
      </div>

      <div class="toolbar">
        <div class="pills">
          @for (s of statuses; track s.key) {
            <button type="button" [class.on]="status === s.key" (click)="status = s.key; reload()">{{ s.label }}</button>
          }
        </div>
        <select class="select" [(ngModel)]="source" (ngModelChange)="reload()">
          <option value="">كل المصادر</option>
          @for (s of sourceList; track s) { <option [value]="s">{{ sourceLabel[s] }}</option> }
        </select>
        @if (auth.isManager()) {
          <select class="select" [(ngModel)]="owner" (ngModelChange)="reload()">
            <option value="">كل الفريق</option>
            @for (u of team(); track u.id) { <option [value]="u.id">{{ u.fullName }}</option> }
          </select>
        }
        <input class="input" type="search" placeholder="اسم، شركة، بريد، هاتف" [(ngModel)]="search" (ngModelChange)="typed$.next()" />
        <span class="grow"></span>
        <div class="pills">
          <button type="button" [class.on]="sort === 'score'" (click)="sort = 'score'; reload()">الأعلى تقييمًا</button>
          <button type="button" [class.on]="sort === 'recent'" (click)="sort = 'recent'; reload()">الأحدث</button>
        </div>
      </div>

      <section class="card">
        @if (result(); as r) {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>العميل</th>
                  <th>التقييم</th>
                  <th class="hide-sm">المصدر</th>
                  <th>الحالة</th>
                  <th class="hide-sm">المسؤول</th>
                  <th class="hide-sm">القيمة المتوقعة</th>
                  <th class="hide-sm">وصل</th>
                </tr>
              </thead>
              <tbody>
                @for (l of r.items; track l.id) {
                  <tr class="link" (click)="router.navigate(['/leads', l.id])">
                    <td>
                      <div class="title">{{ l.fullName }}</div>
                      <div class="sub">{{ l.companyName ?? l.email ?? l.phone }}</div>
                    </td>
                    <td>
                      <span class="score" [class.hot]="l.score >= 70" [class.warm]="l.score >= 45 && l.score < 70">
                        <span class="bar"><i [style.width.%]="l.score"></i></span>{{ l.score }}
                      </span>
                    </td>
                    <td class="hide-sm">{{ sourceLabel[l.source] }}</td>
                    <td><span class="chip" [class]="statusTone(l.status)">{{ statusLabel[l.status] }}</span></td>
                    <td class="hide-sm">
                      @if (l.owner) { {{ l.owner.fullName }} }
                      @else {
                        <button type="button" class="btn sm" (click)="claim(l, $event)">خذه</button>
                      }
                    </td>
                    <td class="hide-sm num">{{ l.estimatedValue | money }}</td>
                    <td class="hide-sm muted small nowrap">{{ l.createdAt | when: 'ago' }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="7" class="empty">لا نتائج.</td></tr>
                }
              </tbody>
            </table>
          </div>
          @if (r.total > r.pageSize) {
            <footer class="pager">
              <span class="muted small">{{ (r.page - 1) * r.pageSize + 1 }}–{{ Math.min(r.page * r.pageSize, r.total) }} من {{ r.total }}</span>
              <div class="row">
                <button type="button" class="btn sm" [disabled]="r.page === 1" (click)="go(r.page - 1)">السابق</button>
                <button type="button" class="btn sm" [disabled]="r.page * r.pageSize >= r.total" (click)="go(r.page + 1)">التالي</button>
              </div>
            </footer>
          }
        } @else {
          <p class="loading">جارٍ التحميل…</p>
        }
      </section>
    </div>

    @if (creating()) {
      <app-lead-form (closed)="creating.set(false)" (saved)="router.navigate(['/leads', $event])" />
    }
  `,
  styles: `
    .pager { display: flex; justify-content: space-between; align-items: center; padding: 12px 18px; border-top: 1px solid var(--line); }
  `
})
export class LeadsPage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  protected router = inject(Router);
  protected auth = inject(Auth);
  protected Math = Math;

  protected result = signal<Paged<LeadItem> | null>(null);
  protected team = signal<TeamMember[]>([]);
  protected creating = signal(false);
  protected status: '' | LeadStatus = '';
  protected source = '';
  protected owner = '';
  protected search = '';
  protected sort: 'score' | 'recent' = 'score';
  private page = 1;
  protected typed$ = new Subject<void>();

  protected sourceList = sources;
  protected sourceLabel = sourceLabel;
  protected statusLabel = leadStatusLabel;
  protected statuses: { key: '' | LeadStatus; label: string }[] = [
    { key: '', label: 'المفتوحون' },
    { key: 'New', label: 'جديد' },
    { key: 'Contacted', label: 'تم التواصل' },
    { key: 'Qualified', label: 'مؤهَّل' },
    { key: 'Converted', label: 'تحوّل' },
    { key: 'Disqualified', label: 'مستبعد' }
  ];

  ngOnInit() {
    if (this.auth.isManager()) this.api.team().subscribe(t => this.team.set(t));
    this.typed$.pipe(debounceTime(300)).subscribe(() => this.reload());
    this.load();
  }

  protected statusTone(s: LeadStatus) {
    return { New: 'chip primary', Contacted: 'chip', Qualified: 'chip won', Disqualified: 'chip lost', Converted: 'chip accent' }[s];
  }

  protected reload() {
    this.page = 1;
    this.load();
  }

  protected go(page: number) {
    this.page = page;
    this.load();
  }

  protected claim(l: LeadItem, e: Event) {
    e.stopPropagation();
    this.api.leadAction(l.id, 'claim').subscribe({
      next: () => { this.toasts.ok(`أصبح ${l.fullName} من عملائك.`); this.load(); },
      error: err => this.toasts.error(errorText(err))
    });
  }

  private load() {
    this.api.leads({
      status: this.status || null, source: this.source || null, ownerId: this.owner || null,
      search: this.search, sort: this.sort, page: this.page, openOnly: true
    }).subscribe(r => this.result.set(r));
  }
}
