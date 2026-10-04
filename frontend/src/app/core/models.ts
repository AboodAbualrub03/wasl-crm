export type Role = 'Rep' | 'Manager' | 'Admin';
export type LeadSource = 'Website' | 'Referral' | 'Social' | 'Event' | 'ColdCall' | 'Other';
export type LeadStatus = 'New' | 'Contacted' | 'Qualified' | 'Disqualified' | 'Converted';
export type StageKind = 'Open' | 'Won' | 'Lost';
export type DealStatus = 'Open' | 'Won' | 'Lost';
export type ActivityType = 'Call' | 'Meeting' | 'Email' | 'Task' | 'Note' | 'System';

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number; }
export interface PersonRef { id: string; fullName: string; }
export interface Ref { id: string; name: string; }

export interface Profile { id: string; fullName: string; email: string; role: Role; workspaceName: string; }
export interface LoginResponse { token: string; expiresAt: string; user: Profile; }

export interface Stage { id: string; name: string; order: number; probability: number; kind: StageKind; }

export interface DealCard {
  id: string; title: string; value: number; companyName: string | null; contactName: string | null;
  owner: PersonRef; expectedClose: string | null; daysInStage: number; isStale: boolean;
  nextTask: { subject: string; dueAt: string; isOverdue: boolean } | null;
}

export interface BoardColumn {
  id: string; name: string; probability: number; kind: StageKind;
  count: number; total: number; weighted: number; deals: DealCard[];
}

export interface Board { columns: BoardColumn[]; openTotal: number; weightedTotal: number; staleCount: number; staleAfterDays: number; }

export interface RelatedRef { kind: 'deal' | 'lead' | 'contact' | 'company'; id: string; name: string; }

export interface ActivityItem {
  id: string; type: ActivityType; subject: string; notes: string | null; dueAt: string | null;
  completedAt: string | null; owner: PersonRef; createdAt: string; isOverdue: boolean; related: RelatedRef | null;
}

export interface StageChange { from: string | null; to: string; by: string; at: string; daysInPrevious: number | null; }

export interface DealDetails {
  id: string; title: string; value: number; status: DealStatus; stage: Stage; stages: Stage[];
  owner: PersonRef; company: Ref | null;
  contact: { id: string; fullName: string; jobTitle: string | null; email: string | null; phone: string | null } | null;
  expectedClose: string | null; lostReason: string | null; createdAt: string; closedAt: string | null;
  stageEnteredAt: string; lastActivityAt: string; isStale: boolean; weightedValue: number;
  history: StageChange[]; activities: ActivityItem[];
}

export interface DealRow { id: string; title: string; value: number; status: DealStatus; stageName: string; owner: PersonRef; expectedClose: string | null; }

export interface LeadItem {
  id: string; fullName: string; companyName: string | null; email: string | null; phone: string | null;
  source: LeadSource; status: LeadStatus; score: number; owner: PersonRef | null;
  estimatedValue: number | null; createdAt: string; lastContactedAt: string | null;
}

export interface Duplicate { kind: 'lead' | 'contact'; id: string; name: string; detail: string | null; reason: string; }

export interface LeadDetails extends Omit<LeadItem, 'score'> {
  interest: string | null; disqualifyReason: string | null; convertedDealId: string | null;
  score: { total: number; factors: { label: string; points: number }[] };
  duplicates: Duplicate[]; activities: ActivityItem[];
}

export interface CompanyItem {
  id: string; name: string; industry: string | null; city: string | null; owner: PersonRef;
  contactCount: number; openDeals: number; openValue: number; wonValue: number;
}

export interface ContactRow { id: string; fullName: string; jobTitle: string | null; email: string | null; phone: string | null; }

export interface CompanyDetails {
  id: string; name: string; industry: string | null; city: string | null; website: string | null; phone: string | null;
  owner: PersonRef; createdAt: string; openValue: number; wonValue: number;
  contacts: ContactRow[]; deals: DealRow[]; activities: ActivityItem[];
}

export interface ContactItem { id: string; fullName: string; jobTitle: string | null; email: string | null; phone: string | null; company: Ref | null; owner: PersonRef; }

export interface ContactDetails extends ContactItem { createdAt: string; deals: DealRow[]; activities: ActivityItem[]; }

export interface MyTasks { overdue: ActivityItem[]; today: ActivityItem[]; upcoming: ActivityItem[]; recentlyDone: ActivityItem[]; }

export interface Home {
  firstName: string; openDeals: number; openValue: number; weighted: number; wonThisMonth: number; overdueCount: number;
  agenda: ActivityItem[];
  staleDeals: { id: string; title: string; value: number; companyName: string | null; daysSilent: number }[];
  hotLeads: LeadItem[];
}

export interface Report {
  wonThisQuarter: number; winRate: number | null; avgDealSize: number; avgCycleDays: number | null;
  forecast: { year: number; month: number; won: number; weighted: number; bestCase: number }[];
  funnel: { stage: string; reached: number; rate: number }[];
  reps: { rep: PersonRef; wonValue: number; wonCount: number; pipeline: number; activities: number; winRate: number | null }[];
  sources: { source: LeadSource; leads: number; converted: number; rate: number }[];
  lostReasons: { reason: string; count: number }[];
}

export interface TeamMember { id: string; fullName: string; email: string; role: Role; isActive: boolean; receivesLeads: boolean; openLeads: number; openDeals: number; }

export interface Settings { name: string; code: string; staleAfterDays: number; intakeKey: string | null; }

export interface SearchHit { kind: 'deal' | 'company' | 'contact' | 'lead'; id: string; title: string; subtitle: string | null; }

export interface NotificationItem { id: string; title: string; link: string | null; isRead: boolean; createdAt: string; }
