import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Auth } from '../core/auth';
import { InitialsPipe, WhenPipe, roleLabel } from '../core/format';
import { NotificationItem } from '../core/models';
import { Bell } from '../core/ui';
import { CommandPalette } from './command-palette';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, InitialsPipe, WhenPipe, CommandPalette],
  templateUrl: './shell.html',
  styleUrl: './shell.scss'
})
export class Shell implements OnInit {
  protected auth = inject(Auth);
  protected bell = inject(Bell);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  protected roleLabel = roleLabel;
  protected bellOpen = signal(false);
  protected meOpen = signal(false);
  protected menuOpen = signal(false);
  protected paletteOpen = signal(false);

  ngOnInit() {
    this.bell.refresh();
    const t = setInterval(() => this.bell.refresh(), 45_000);
    this.destroyRef.onDestroy(() => clearInterval(t));
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      this.bellOpen.set(false);
      this.meOpen.set(false);
      this.menuOpen.set(false);
    });
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      this.paletteOpen.set(true);
    }
  }

  protected open(n: NotificationItem) {
    this.bell.read(n);
    this.bellOpen.set(false);
    if (n.link) this.router.navigateByUrl(n.link);
  }
}
