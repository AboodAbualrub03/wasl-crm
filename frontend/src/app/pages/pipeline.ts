import { CdkDrag, CdkDragDrop, CdkDragPlaceholder, CdkDropList, CdkDropListGroup, transferArrayItem } from '@angular/cdk/drag-drop';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { Api } from '../core/api';
import { Auth, errorText } from '../core/auth';
import { DaysPipe, InitialsPipe, MoneyPipe, WhenPipe } from '../core/format';
import { Board, BoardColumn, DealCard } from '../core/models';
import { Dialog, Toasts } from '../core/ui';
import { DealForm } from '../shared/forms';

interface PendingLoss { deal: DealCard; from: BoardColumn; to: BoardColumn; }

@Component({
  selector: 'app-pipeline',
  imports: [CdkDropListGroup, CdkDropList, CdkDrag, CdkDragPlaceholder, FormsModule, MoneyPipe, WhenPipe, InitialsPipe, DaysPipe, Dialog, DealForm],
  templateUrl: './pipeline.html',
  styleUrl: './pipeline.scss'
})
export class PipelinePage implements OnInit {
  private api = inject(Api);
  private toasts = inject(Toasts);
  private router = inject(Router);
  protected auth = inject(Auth);

  protected board = signal<Board | null>(null);
  protected scope = signal<'team' | 'mine'>('team');
  protected search = '';
  protected newDeal = signal(false);
  protected pendingLoss = signal<PendingLoss | null>(null);
  protected lossReason = '';
  protected reasons = ['السعر أعلى من الميزانية', 'اختاروا منافسًا', 'تأجيل المشروع', 'لا يوجد رد من العميل'];
  private search$ = new Subject<void>();
  /** A drop also fires a click on the card; this keeps it from opening the deal. */
  protected dragging = false;

  ngOnInit() {
    if (!this.auth.isManager()) this.scope.set('mine');
    this.search$.pipe(debounceTime(250)).subscribe(() => this.load());
    this.load();
  }

  protected setScope(s: 'team' | 'mine') {
    this.scope.set(s);
    this.load();
  }

  protected onSearch() {
    this.search$.next();
  }

  protected drop(event: CdkDragDrop<DealCard[]>, to: BoardColumn) {
    if (event.previousContainer === event.container) return;
    const from = this.board()!.columns.find(c => c.deals === event.previousContainer.data)!;
    const deal = event.previousContainer.data[event.previousIndex];

    if (to.kind === 'Lost') {
      // Losing needs a reason first; the card only moves once it is given.
      this.lossReason = '';
      this.pendingLoss.set({ deal, from, to });
      return;
    }

    transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    this.recount();
    this.move(deal, to.id, null, to.kind === 'Won' ? `مبروك! ربحت «${deal.title}»` : null);
  }

  protected confirmLoss() {
    const p = this.pendingLoss();
    if (!p || !this.lossReason.trim()) return;
    p.from.deals.splice(p.from.deals.indexOf(p.deal), 1);
    p.to.deals.unshift(p.deal);
    this.recount();
    this.pendingLoss.set(null);
    this.move(p.deal, p.to.id, this.lossReason.trim(), null);
  }

  protected open(d: DealCard) {
    if (this.dragging) return;
    this.router.navigate(['/deals', d.id]);
  }

  protected endDrag() {
    setTimeout(() => (this.dragging = false), 120);
  }

  protected created(id: string) {
    this.newDeal.set(false);
    this.toasts.ok('تمت إضافة الصفقة.');
    this.load();
  }

  private move(deal: DealCard, stageId: string, reason: string | null, success: string | null) {
    this.api.moveDeal(deal.id, stageId, reason).subscribe({
      next: () => {
        if (success) this.toasts.ok(success);
        this.load(true);
      },
      error: e => {
        // The server refused (rules or a concurrent change): put the board back as it really is.
        this.toasts.error(errorText(e));
        this.load(true);
      }
    });
  }

  /** Recomputes column totals locally so the header updates the moment a card lands. */
  private recount() {
    const b = this.board();
    if (!b) return;
    for (const c of b.columns) {
      c.count = c.deals.length;
      c.total = c.deals.reduce((s, d) => s + d.value, 0);
      c.weighted = Math.round(c.total * c.probability / 100);
    }
    const open = b.columns.filter(c => c.kind === 'Open');
    this.board.set({ ...b, openTotal: open.reduce((s, c) => s + c.total, 0), weightedTotal: open.reduce((s, c) => s + c.weighted, 0) });
  }

  private load(quiet = false) {
    if (!quiet) this.board.set(null);
    const owner = this.scope() === 'mine' ? this.auth.user()!.id : null;
    this.api.board(owner, this.search).subscribe(b => this.board.set(b));
  }
}
