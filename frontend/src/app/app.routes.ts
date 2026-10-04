import { Routes } from '@angular/router';
import { adminOnly, signedIn } from './core/auth';
import { Shell } from './layout/shell';
import { LoginPage } from './pages/login';

const page = (title: string) => `${title} · وصل`;

export const routes: Routes = [
  { path: 'login', component: LoginPage, title: page('تسجيل الدخول') },
  {
    path: '',
    component: Shell,
    canActivate: [signedIn],
    children: [
      { path: '', title: page('اليوم'), loadComponent: () => import('./pages/today').then(m => m.TodayPage) },
      { path: 'pipeline', title: page('الصفقات'), loadComponent: () => import('./pages/pipeline').then(m => m.PipelinePage) },
      { path: 'deals/:id', title: page('صفقة'), loadComponent: () => import('./pages/deal-details').then(m => m.DealDetailsPage) },
      { path: 'leads', title: page('العملاء المحتملون'), loadComponent: () => import('./pages/leads').then(m => m.LeadsPage) },
      { path: 'leads/:id', title: page('عميل محتمل'), loadComponent: () => import('./pages/lead-details').then(m => m.LeadDetailsPage) },
      { path: 'companies', title: page('الشركات'), loadComponent: () => import('./pages/directory').then(m => m.CompaniesPage) },
      { path: 'companies/:id', title: page('شركة'), loadComponent: () => import('./pages/directory').then(m => m.CompanyDetailsPage) },
      { path: 'contacts', title: page('جهات الاتصال'), loadComponent: () => import('./pages/directory').then(m => m.ContactsPage) },
      { path: 'contacts/:id', title: page('جهة اتصال'), loadComponent: () => import('./pages/directory').then(m => m.ContactDetailsPage) },
      { path: 'tasks', title: page('المهام'), loadComponent: () => import('./pages/directory').then(m => m.TasksPage) },
      { path: 'reports', title: page('التقارير'), loadComponent: () => import('./pages/reports').then(m => m.ReportsPage) },
      { path: 'settings', title: page('الإعدادات'), canActivate: [adminOnly], loadComponent: () => import('./pages/settings').then(m => m.SettingsPage) }
    ]
  },
  { path: '**', redirectTo: '' }
];
