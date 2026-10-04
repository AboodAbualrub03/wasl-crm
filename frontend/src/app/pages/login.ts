import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Auth, errorText } from '../core/auth';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="split">
      <aside class="story">
        <div class="brand"><img src="logo.svg" alt="" width="40" height="40" /> وصل</div>
        <div class="pitch">
          <h1>كل علاقة مع عميل،<br />من أول اتصال حتى توقيع العقد.</h1>
          <ul>
            <li><span class="n">01</span> العملاء المحتملون يتوزعون على الفريق بعدل، ومعهم تقييم يشرح نفسه.</li>
            <li><span class="n">02</span> لوحة صفقات تسحب فيها الصفقة بين المراحل، وتنبهك قبل أن تبرد.</li>
            <li><span class="n">03</span> توقعات مبيعات مبنية على احتمالية كل مرحلة، لا على التخمين.</li>
          </ul>
        </div>
        <p class="foot">مشروع تجريبي · بيانات وهمية</p>
      </aside>

      <main class="form-side">
        <form class="form" (ngSubmit)="submit()">
          <h2>تسجيل الدخول</h2>
          <label class="field">
            <span>مساحة العمل</span>
            <input class="input ltr-in" name="ws" [(ngModel)]="workspace" required />
          </label>
          <label class="field">
            <span>البريد الإلكتروني</span>
            <input class="input ltr-in" name="email" type="email" [(ngModel)]="email" autocomplete="username" required />
          </label>
          <label class="field">
            <span>كلمة المرور</span>
            <input class="input ltr-in" name="password" type="password" [(ngModel)]="password" autocomplete="current-password" required />
          </label>
          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
          <button type="submit" class="btn primary big" [disabled]="busy()">{{ busy() ? 'جارٍ الدخول…' : 'دخول' }}</button>

          <div class="demo">
            <p class="muted small">جرّب بحساب جاهز (كلمة المرور <span class="ltr">Demo&#64;12345</span>):</p>
            <div class="demo-list">
              @for (a of demo; track a.email) {
                <button type="button" (click)="quick(a.email)" [disabled]="busy()">
                  <strong>{{ a.name }}</strong>
                  <span>{{ a.role }}</span>
                </button>
              }
            </div>
          </div>
        </form>
      </main>
    </div>
  `,
  styles: `
    .split { min-height: 100vh; display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr); }
    .story { background: var(--nav); color: #e8e6f0; padding: 40px 56px; display: flex; flex-direction: column; justify-content: space-between; position: relative; overflow: hidden; }
    .story::after { content: ''; position: absolute; width: 420px; height: 420px; border-radius: 50%; background: var(--accent); opacity: .08; inset-block-end: -160px; inset-inline-end: -120px; }
    .brand { display: flex; align-items: center; gap: 12px; font-size: 24px; font-weight: 600; color: #fff; }
    .brand img { border-radius: 11px; box-shadow: 0 0 0 1px rgba(255, 255, 255, .12); }
    .pitch h1 { font-size: 34px; line-height: 1.45; font-weight: 500; color: #fff; max-width: 520px; }
    .pitch ul { list-style: none; padding: 0; margin: 32px 0 0; display: flex; flex-direction: column; gap: 16px; max-width: 480px; }
    .pitch li { display: flex; gap: 14px; color: #b9bcd0; line-height: 1.8; }
    .n { color: var(--accent); font-weight: 500; font-size: 13px; padding-top: 3px; }
    .foot { font-size: 12px; color: #7b7f99; }
    .form-side { display: grid; place-items: center; padding: 40px 24px; }
    .form { width: 100%; max-width: 380px; display: flex; flex-direction: column; gap: 16px; }
    .form h2 { font-size: 22px; margin-bottom: 6px; }
    .ltr-in { direction: ltr; text-align: left; }
    .big { height: 46px; font-size: 15px; margin-top: 4px; }
    .error { color: var(--lost); font-size: 14px; }
    .demo { margin-top: 18px; padding-top: 20px; border-top: 1px solid var(--line); display: flex; flex-direction: column; gap: 10px; }
    .demo-list { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .demo-list button { display: flex; flex-direction: column; align-items: flex-start; gap: 0; padding: 10px 12px; border: 1px solid var(--line); background: var(--surface); border-radius: 12px; font: inherit; cursor: pointer; text-align: start; }
    .demo-list button:hover:not(:disabled) { border-color: var(--accent); }
    .demo-list strong { font-weight: 500; font-size: 14px; }
    .demo-list span { font-size: 12px; color: var(--muted); }
    @media (max-width: 900px) { .split { grid-template-columns: 1fr; } .story { padding: 28px 24px; gap: 28px; } .pitch h1 { font-size: 24px; } }
  `
})
export class LoginPage {
  private auth = inject(Auth);
  private router = inject(Router);

  protected workspace = 'nour';
  protected email = '';
  protected password = '';
  protected busy = signal(false);
  protected error = signal('');

  protected demo = [
    { email: 'karim@nour.test', name: 'كريم داود', role: 'مندوب مبيعات' },
    { email: 'salma@nour.test', name: 'سلمى عيسى', role: 'مندوبة مبيعات' },
    { email: 'manager@nour.test', name: 'ليان منصور', role: 'مديرة المبيعات' },
    { email: 'admin@nour.test', name: 'علاء ربيع', role: 'مدير النظام' }
  ];

  constructor() {
    if (this.auth.signedIn()) this.router.navigate(['/']);
  }

  protected quick(email: string) {
    this.workspace = 'nour';
    this.email = email;
    this.password = 'Demo@12345';
    this.submit();
  }

  protected submit() {
    if (!this.workspace || !this.email || !this.password) {
      this.error.set('أكمل جميع الحقول.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.workspace, this.email, this.password).subscribe({
      next: () => this.router.navigate(['/']),
      error: e => {
        this.error.set(errorText(e));
        this.busy.set(false);
      }
    });
  }
}
