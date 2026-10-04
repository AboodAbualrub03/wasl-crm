import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  ActivityItem, Board, CompanyDetails, CompanyItem, ContactDetails, ContactItem, DealDetails, Duplicate,
  Home, LeadDetails, LeadItem, LoginResponse, MyTasks, NotificationItem, Paged, Report, SearchHit,
  Settings, Stage, TeamMember
} from './models';

/** Every HTTP call in one place; pages never build URLs. */
@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  login(workspace: string, email: string, password: string) {
    return this.http.post<LoginResponse>('/api/auth/login', { workspace, email, password });
  }

  home() { return this.http.get<Home>('/api/home'); }
  reports() { return this.http.get<Report>('/api/reports'); }
  search(q: string) { return this.http.get<SearchHit[]>('/api/search', { params: { q } }); }

  // Deals
  board(ownerId: string | null, search: string) { return this.http.get<Board>('/api/deals/board', { params: params({ ownerId, search }) }); }
  stages() { return this.http.get<Stage[]>('/api/deals/stages'); }
  deal(id: string) { return this.http.get<DealDetails>(`/api/deals/${id}`); }
  createDeal(body: object) { return this.http.post<{ id: string }>('/api/deals', body); }
  updateDeal(id: string, body: object) { return this.http.put<void>(`/api/deals/${id}`, body); }
  moveDeal(id: string, stageId: string, lostReason: string | null = null) {
    return this.http.post<void>(`/api/deals/${id}/move`, { stageId, lostReason });
  }

  // Leads
  leads(q: Record<string, unknown>) { return this.http.get<Paged<LeadItem>>('/api/leads', { params: params(q) }); }
  lead(id: string) { return this.http.get<LeadDetails>(`/api/leads/${id}`); }
  duplicates(email: string | null, phone: string | null, excludeId?: string) {
    return this.http.get<Duplicate[]>('/api/leads/duplicates', { params: params({ email, phone, excludeId }) });
  }
  createLead(body: object) { return this.http.post<{ id: string }>('/api/leads', body); }
  updateLead(id: string, body: object) { return this.http.put<void>(`/api/leads/${id}`, body); }
  leadAction(id: string, action: 'qualify' | 'reopen' | 'claim') { return this.http.post<void>(`/api/leads/${id}/${action}`, {}); }
  disqualify(id: string, reason: string) { return this.http.post<void>(`/api/leads/${id}/disqualify`, { reason }); }
  assignLead(id: string, ownerId: string) { return this.http.post<void>(`/api/leads/${id}/assign`, { ownerId }); }
  convertLead(id: string, body: object) { return this.http.post<{ dealId: string }>(`/api/leads/${id}/convert`, body); }

  // Activities
  createActivity(body: object) { return this.http.post<ActivityItem>('/api/activities', body); }
  completeActivity(id: string) { return this.http.post<void>(`/api/activities/${id}/complete`, {}); }
  reopenActivity(id: string) { return this.http.post<void>(`/api/activities/${id}/reopen`, {}); }
  tasks() { return this.http.get<MyTasks>('/api/activities/tasks'); }

  // Directory
  companies(search: string, page: number) { return this.http.get<Paged<CompanyItem>>('/api/companies', { params: params({ search, page }) }); }
  company(id: string) { return this.http.get<CompanyDetails>(`/api/companies/${id}`); }
  createCompany(body: object) { return this.http.post<{ id: string }>('/api/companies', body); }
  updateCompany(id: string, body: object) { return this.http.put<void>(`/api/companies/${id}`, body); }
  contacts(search: string, page: number, companyId?: string) {
    return this.http.get<Paged<ContactItem>>('/api/contacts', { params: params({ search, page, companyId, pageSize: companyId ? 100 : 25 }) });
  }
  contact(id: string) { return this.http.get<ContactDetails>(`/api/contacts/${id}`); }
  createContact(body: object) { return this.http.post<{ id: string }>('/api/contacts', body); }
  updateContact(id: string, body: object) { return this.http.put<void>(`/api/contacts/${id}`, body); }

  // Workspace
  team() { return this.http.get<TeamMember[]>('/api/team'); }
  addMember(body: object) { return this.http.post<{ id: string }>('/api/team', body); }
  updateMember(id: string, body: object) { return this.http.put<void>(`/api/team/${id}`, body); }
  settings() { return this.http.get<Settings>('/api/settings'); }
  setStaleDays(staleAfterDays: number) { return this.http.put<void>('/api/settings/stale-days', { staleAfterDays }); }
  rotateKey() { return this.http.post<{ key: string }>('/api/settings/intake-key', {}); }
  savePipeline(body: object) { return this.http.put<Stage[]>('/api/settings/pipeline', body); }

  notifications() { return this.http.get<{ unread: number; items: NotificationItem[] }>('/api/notifications'); }
  readNotification(id: string) { return this.http.post<void>(`/api/notifications/${id}/read`, {}); }
  readAllNotifications() { return this.http.post<void>('/api/notifications/read-all', {}); }
}

function params(values: Record<string, unknown>): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(values)) {
    if (v === undefined || v === null || v === '') continue;
    p = p.set(k, String(v));
  }
  return p;
}
