import { Component, HostListener, Injectable, inject, input, output, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { Api } from './api';
import { NotificationItem } from './models';

// ---------- Toasts ----------

@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<{ id: number; text: string; bad: boolean }[]>([]);
  private seq = 0;

  ok(text: string) { this.push(text, false); }
  error(text: string) { this.push(text, true); }

  dismiss(id: number) { this.items.update(l => l.filter(t => t.id !== id)); }

  private push(text: string, bad: boolean) {
    const id = ++this.seq;
    this.items.update(l => [...l, { id, text, bad }]);
    setTimeout(() => this.dismiss(id), bad ? 6000 : 3200);
  }
}

@Component({
  selector: 'app-toasts',
  template: `
    <div class="toasts" aria-live="polite">
      @for (t of toasts.items(); track t.id) {
        <button type="button" class="toast" [class.bad]="t.bad" (click)="toasts.dismiss(t.id)">{{ t.text }}</button>
      }
    </div>
  `,
  styles: `
    .toasts { position: fixed; inset-block-end: 24px; inset-inline-end: 24px; display: flex; flex-direction: column; gap: 8px; z-index: 200; }
    .toast { border: 0; font: inherit; font-size: 14px; text-align: start; cursor: pointer; padding: 12px 18px; border-radius: 12px;
      background: var(--nav); color: #f4f1ea; box-shadow: 0 10px 30px rgba(30, 34, 53, .25); max-width: min(420px, calc(100vw - 48px)); }
    .toast.bad { background: var(--lost); }
  `
})
export class ToastHost {
  protected toasts = inject(Toasts);
}

// ---------- Dialog ----------

@Component({
  selector: 'app-dialog',
  template: `
    <div class="scrim" (mousedown)="closed.emit()">
      <section class="sheet" role="dialog" aria-modal="true" [attr.aria-label]="heading()" [style.width.px]="width()" (mousedown)="$event.stopPropagation()">
        <header>
          <h2>{{ heading() }}</h2>
          @if (hint()) { <p>{{ hint() }}</p> }
        </header>
        <div class="body"><ng-content /></div>
        <footer><ng-content select="[actions]" /></footer>
      </section>
    </div>
  `,
  styles: `
    .scrim { position: fixed; inset: 0; z-index: 100; background: rgba(30, 34, 53, .38); display: grid; place-items: center; padding: 16px; animation: in .14s ease-out; }
    .sheet { background: var(--surface); border-radius: 18px; max-width: 100%; max-height: calc(100vh - 32px); overflow: auto; box-shadow: 0 24px 60px rgba(30, 34, 53, .28); }
    header { padding: 24px 26px 0; }
    h2 { font-size: 18px; }
    header p { color: var(--muted); font-size: 14px; margin-top: 4px; }
    .body { padding: 20px 26px; display: flex; flex-direction: column; gap: 16px; }
    footer { padding: 0 26px 24px; display: flex; gap: 10px; }
    @keyframes in { from { opacity: 0; } }
  `
})
export class Dialog {
  readonly heading = input.required<string>();
  readonly hint = input('');
  readonly width = input(480);
  readonly closed = output<void>();

  @HostListener('document:keydown.escape') esc() { this.closed.emit(); }
}

// ---------- Shared shell state ----------

@Injectable({ providedIn: 'root' })
export class Bell {
  private api = inject(Api);
  readonly unread = signal(0);
  readonly items = signal<NotificationItem[]>([]);

  refresh() {
    forkJoin([this.api.notifications()]).subscribe({
      next: ([feed]) => { this.unread.set(feed.unread); this.items.set(feed.items); },
      error: () => {}
    });
  }

  read(n: NotificationItem) {
    if (n.isRead) return;
    this.items.update(l => l.map(x => x.id === n.id ? { ...x, isRead: true } : x));
    this.unread.update(c => Math.max(0, c - 1));
    this.api.readNotification(n.id).subscribe();
  }

  readAll() {
    this.items.update(l => l.map(x => ({ ...x, isRead: true })));
    this.unread.set(0);
    this.api.readAllNotifications().subscribe();
  }
}
