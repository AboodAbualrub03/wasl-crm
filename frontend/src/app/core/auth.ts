import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, tap, throwError } from 'rxjs';
import { Api } from './api';
import { Profile, Role } from './models';

const KEY = 'wasl.session';

interface Session { token: string; expiresAt: string; user: Profile; }

@Injectable({ providedIn: 'root' })
export class Auth {
  private api = inject(Api);
  private router = inject(Router);
  private session = signal<Session | null>(read());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly signedIn = computed(() => this.session() !== null);
  readonly isManager = computed(() => this.user()?.role === 'Manager' || this.user()?.role === 'Admin');
  readonly isAdmin = computed(() => this.user()?.role === 'Admin');

  login(workspace: string, email: string, password: string) {
    return this.api.login(workspace, email, password).pipe(tap(r => this.store({ token: r.token, expiresAt: r.expiresAt, user: r.user })));
  }

  logout() {
    this.store(null);
    this.router.navigate(['/login']);
  }

  is(...roles: Role[]) {
    const u = this.user();
    return !!u && roles.includes(u.role);
  }

  private store(s: Session | null) {
    this.session.set(s);
    try {
      if (s) localStorage.setItem(KEY, JSON.stringify(s));
      else localStorage.removeItem(KEY);
    } catch { /* storage unavailable: session lives in memory */ }
  }
}

function read(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    const s = raw ? (JSON.parse(raw) as Session) : null;
    return s && new Date(s.expiresAt) > new Date() ? s : null;
  } catch {
    return null;
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.token();
  return next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req).pipe(
    catchError((e: HttpErrorResponse) => {
      if (e.status === 401 && token) auth.logout();
      return throwError(() => e);
    })
  );
};

export const signedIn: CanActivateFn = () => inject(Auth).signedIn() || inject(Router).createUrlTree(['/login']);
export const adminOnly: CanActivateFn = () => inject(Auth).isAdmin() || inject(Router).createUrlTree(['/']);

export function errorText(e: unknown): string {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0) return 'تعذر الاتصال بالخادم.';
    if (e.status === 429) return 'محاولات كثيرة، انتظر قليلًا.';
    const first = e.error?.errors ? Object.values(e.error.errors as Record<string, string[]>)[0]?.[0] : null;
    return first ?? e.error?.title ?? 'حدث خطأ غير متوقع.';
  }
  return 'حدث خطأ غير متوقع.';
}
