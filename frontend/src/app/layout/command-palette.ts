import { AfterViewInit, Component, ElementRef, HostListener, computed, inject, output, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of } from 'rxjs';
import { Api } from '../core/api';
import { SearchHit } from '../core/models';

const kindLabel: Record<SearchHit['kind'], string> = { deal: 'صفقة', company: 'شركة', contact: 'جهة اتصال', lead: 'عميل محتمل' };
const routeOf: Record<SearchHit['kind'], string> = { deal: '/deals', company: '/companies', contact: '/contacts', lead: '/leads' };

interface Shortcut { label: string; hint: string; route: string; }

/** Ctrl+K: jump anywhere by typing. Arrow keys to move, Enter to open, Esc to close. */
@Component({
  selector: 'app-command-palette',
  template: `
    <div class="scrim" (mousedown)="closed.emit()">
      <div class="palette" role="dialog" aria-label="بحث" (mousedown)="$event.stopPropagation()">
        <input #box class="q" placeholder="ابحث عن صفقة، شركة، شخص، أو عميل محتمل…" [value]="query()" (input)="type($any($event.target).value)" />
        <ul>
          @for (item of rows(); track $index; let i = $index) {
            <li>
              <button type="button" [class.on]="i === cursor()" (mouseenter)="cursor.set(i)" (click)="go(i)">
                <span class="title">{{ item.title }}</span>
                @if (item.sub) { <span class="sub">{{ item.sub }}</span> }
                <span class="kind">{{ item.kind }}</span>
              </button>
            </li>
          } @empty {
            <li class="none">{{ query().length < 2 ? 'اكتب حرفين على الأقل' : searching() ? 'جارٍ البحث…' : 'لا نتائج' }}</li>
          }
        </ul>
      </div>
    </div>
  `,
  styles: `
    .scrim { position: fixed; inset: 0; z-index: 120; background: rgba(30, 34, 53, .4); display: flex; justify-content: center; padding: 12vh 16px 16px; }
    .palette { width: 620px; max-width: 100%; height: fit-content; background: var(--surface); border-radius: 18px; box-shadow: 0 30px 70px rgba(30, 34, 53, .35); overflow: hidden; }
    .q { width: 100%; border: 0; border-bottom: 1px solid var(--line); padding: 20px 22px; font: inherit; font-size: 17px; background: transparent; color: var(--ink); }
    .q:focus { outline: none; }
    ul { list-style: none; margin: 0; padding: 8px; max-height: 420px; overflow-y: auto; }
    button { width: 100%; display: flex; align-items: center; gap: 10px; border: 0; background: none; font: inherit; text-align: start; padding: 11px 14px; border-radius: 10px; cursor: pointer; color: var(--ink); }
    button.on { background: var(--primary-soft); }
    .title { font-weight: 500; }
    .sub { color: var(--muted); font-size: 13px; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .kind { margin-inline-start: auto; font-size: 12px; color: var(--muted); background: var(--sunk); padding: 2px 8px; border-radius: 6px; }
    .none { padding: 26px; text-align: center; color: var(--muted); font-size: 14px; }
  `
})
export class CommandPalette implements AfterViewInit {
  private api = inject(Api);
  private router = inject(Router);
  readonly closed = output<void>();
  private box = viewChild<ElementRef<HTMLInputElement>>('box');

  protected query = signal('');
  protected hits = signal<SearchHit[]>([]);
  protected searching = signal(false);
  protected cursor = signal(0);
  private input$ = new Subject<string>();

  private shortcuts: Shortcut[] = [
    { label: 'لوحة الصفقات', hint: 'اذهب إلى', route: '/pipeline' },
    { label: 'مهامي', hint: 'اذهب إلى', route: '/tasks' },
    { label: 'العملاء المحتملون', hint: 'اذهب إلى', route: '/leads' },
    { label: 'التقارير', hint: 'اذهب إلى', route: '/reports' }
  ];

  protected rows = computed(() => {
    if (this.query().trim().length < 2) {
      return this.shortcuts.map(s => ({ title: s.label, sub: null as string | null, kind: s.hint, route: s.route }));
    }
    return this.hits().map(h => ({ title: h.title, sub: h.subtitle, kind: kindLabel[h.kind], route: `${routeOf[h.kind]}/${h.id}` }));
  });

  constructor() {
    this.input$.pipe(
      debounceTime(180),
      distinctUntilChanged(),
      switchMap(q => {
        if (q.trim().length < 2) return of([]);
        this.searching.set(true);
        return this.api.search(q);
      })
    ).subscribe(h => {
      this.hits.set(h);
      this.cursor.set(0);
      this.searching.set(false);
    });
  }

  ngAfterViewInit() {
    this.box()?.nativeElement.focus();
  }

  protected type(v: string) {
    this.query.set(v);
    this.input$.next(v);
  }

  protected go(i: number) {
    const row = this.rows()[i];
    if (!row) return;
    this.closed.emit();
    this.router.navigateByUrl(row.route);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent) {
    const n = this.rows().length;
    if (e.key === 'Escape') this.closed.emit();
    else if (e.key === 'ArrowDown') { e.preventDefault(); this.cursor.set((this.cursor() + 1) % Math.max(n, 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); this.cursor.set((this.cursor() - 1 + n) % Math.max(n, 1)); }
    else if (e.key === 'Enter') { e.preventDefault(); this.go(this.cursor()); }
  }
}
