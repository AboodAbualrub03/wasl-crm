import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { errorText } from '../core/auth';
import { WhenPipe, activityLabel } from '../core/format';
import { ActivityItem, ActivityType } from '../core/models';
import { Toasts } from '../core/ui';

export interface FeedTarget { dealId?: string; leadId?: string; contactId?: string; companyId?: string; }

const kinds: ActivityType[] = ['Note', 'Call', 'Meeting', 'Email', 'Task'];

@Component({
  selector: 'app-activity-icon',
  template: `
    <span class="ic {{ type() }}">
      @switch (type()) {
        @case ('Call') { <svg viewBox="0 0 24 24"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1.9.4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.9.6 2.8.7a2 2 0 0 1 1.7 2z"/></svg> }
        @case ('Meeting') { <svg viewBox="0 0 24 24"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8"/></svg> }
        @case ('Email') { <svg viewBox="0 0 24 24"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-10 6L2 7"/></svg> }
        @case ('Task') { <svg viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="4"/><path d="m8 12 3 3 5-6"/></svg> }
        @case ('System') { <svg viewBox="0 0 24 24"><path d="M5 12h14M13 6l6 6-6 6"/></svg> }
        @default { <svg viewBox="0 0 24 24"><path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M8 13h8M8 17h5"/></svg> }
      }
    </span>
  `,
  styles: `
    .ic { width: 30px; height: 30px; border-radius: 9px; display: grid; place-items: center; background: var(--sunk); color: var(--ink-2); flex-shrink: 0; }
    svg { width: 15px; height: 15px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
    .Call { background: #e3eef0; color: #2f6f7a; }
    .Meeting { background: var(--primary-soft); color: var(--primary); }
    .Email { background: #efe7f3; color: #6b4a85; }
    .Task { background: var(--accent-soft); color: #9a4f27; }
    .System { background: transparent; color: var(--muted); }
  `
})
export class ActivityIcon {
  readonly type = input.required<ActivityType>();
}

@Component({
  selector: 'app-activity-feed',
  imports: [FormsModule, RouterLink, WhenPipe, ActivityIcon],
  templateUrl: './activity-feed.html',
  styleUrl: './activity-feed.scss'
})
export class ActivityFeed {
  private api = inject(Api);
  private toasts = inject(Toasts);

  readonly items = input.required<ActivityItem[]>();
  readonly target = input.required<FeedTarget>();
  readonly showRelated = input(false);
  readonly readonly = input(false);
  readonly changed = output<void>();

  protected kinds = kinds;
  protected label = activityLabel;
  protected kind = signal<ActivityType>('Note');
  protected subject = '';
  protected notes = '';
  protected due = '';
  protected busy = signal(false);

  protected open = computed(() => this.items().filter(a => a.type === 'Task' && !a.completedAt));
  protected done = computed(() => this.items().filter(a => !(a.type === 'Task' && !a.completedAt)));

  protected placeholder = computed(() => ({
    Note: 'اكتب ملاحظة…', Call: 'عن ماذا كانت المكالمة؟', Meeting: 'عنوان الاجتماع', Email: 'موضوع البريد', Task: 'ما الذي يجب فعله؟', System: ''
  })[this.kind()]);

  protected pick(k: ActivityType) {
    this.kind.set(k);
    if (k === 'Task' && !this.due) {
      const t = new Date(Date.now() + 86400000);
      t.setHours(10, 0, 0, 0);
      this.due = toLocalInput(t);
    }
  }

  protected save() {
    const subject = (this.kind() === 'Note' && !this.subject.trim() ? this.notes.trim().slice(0, 80) : this.subject.trim());
    if (!subject) return;
    this.busy.set(true);
    this.api.createActivity({
      type: this.kind(),
      subject,
      notes: this.kind() === 'Note' && !this.subject.trim() ? null : this.notes || null,
      dueAt: this.kind() === 'Task' && this.due ? new Date(this.due).toISOString() : null,
      ...this.target()
    }).subscribe({
      next: () => {
        this.subject = '';
        this.notes = '';
        this.busy.set(false);
        this.changed.emit();
      },
      error: e => {
        this.busy.set(false);
        this.toasts.error(errorText(e));
      }
    });
  }

  protected toggle(a: ActivityItem) {
    const call = a.completedAt ? this.api.reopenActivity(a.id) : this.api.completeActivity(a.id);
    call.subscribe({
      next: () => this.changed.emit(),
      error: e => this.toasts.error(errorText(e))
    });
  }

  protected link(a: ActivityItem) {
    const r = a.related!;
    return [`/${r.kind === 'company' ? 'companies' : r.kind + 's'}`, r.id];
  }
}

function toLocalInput(d: Date) {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
