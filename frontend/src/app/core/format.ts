import { Pipe, PipeTransform } from '@angular/core';
import { ActivityType, LeadSource, LeadStatus, Role } from './models';

// Arabic words with Latin digits: money and dates stay easy to scan.
const money = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const compact = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });
const day = new Intl.DateTimeFormat('ar-u-nu-latn', { day: 'numeric', month: 'short' });
const full = new Intl.DateTimeFormat('ar-u-nu-latn', { day: 'numeric', month: 'long', year: 'numeric' });
const time = new Intl.DateTimeFormat('ar-u-nu-latn', { hour: 'numeric', minute: '2-digit' });
const month = new Intl.DateTimeFormat('ar-u-nu-latn', { month: 'short' });

export function formatMoney(v: number | null | undefined, short = false) {
  if (v === null || v === undefined) return '—';
  return '$' + (short && Math.abs(v) >= 10_000 ? compact.format(v) : money.format(v));
}

export function formatDate(v: string | Date | null | undefined, style: 'day' | 'full' | 'time' | 'month' | 'ago' | 'when' = 'day') {
  if (!v) return '—';
  const d = typeof v === 'string' ? new Date(v) : v;
  switch (style) {
    case 'full': return full.format(d);
    case 'time': return time.format(d);
    case 'month': return month.format(d);
    case 'ago': return ago(d);
    case 'when': return when(d);
    default: return d.getFullYear() === new Date().getFullYear() ? day.format(d) : full.format(d);
  }
}

function ago(d: Date) {
  const mins = Math.round((Date.now() - d.getTime()) / 60000);
  if (mins < 1) return 'الآن';
  if (mins < 60) return `قبل ${mins} د`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `قبل ${hrs} س`;
  const days = Math.round(hrs / 24);
  if (days === 1) return 'أمس';
  if (days < 30) return `قبل ${days} يومًا`;
  return day.format(d);
}

/** For due dates: "اليوم ٢:٠٠", "غدًا", "متأخر ٣ أيام". */
function when(d: Date) {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  const target = new Date(d); target.setHours(0, 0, 0, 0);
  const diff = Math.round((target.getTime() - today.getTime()) / 86400000);
  if (diff === 0) return `اليوم ${time.format(d)}`;
  if (diff === 1) return 'غدًا';
  if (diff === -1) return 'أمس';
  if (diff < 0) return `قبل ${-diff} أيام`;
  if (diff < 7) return `بعد ${diff} أيام`;
  return day.format(d);
}

export function initials(name: string) {
  return name.replace(/^(د\.|م\.|أ\.|المحامي)\s*/, '').split(' ').filter(Boolean).slice(0, 2).map(p => p[0]).join('‌'); // zero-width non-joiner: Arabic letters must not join into a word
}

/** "اليوم", "يوم", "يومان", "3 أيام", "15 يومًا" */
export function daysText(n: number) {
  if (n <= 0) return 'اليوم';
  if (n === 1) return 'يوم';
  if (n === 2) return 'يومان';
  return n <= 10 ? `${n} أيام` : `${n} يومًا`;
}

@Pipe({ name: 'days' })
export class DaysPipe implements PipeTransform {
  transform(n: number) { return daysText(n); }
}

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(v: number | null | undefined, short = false) { return formatMoney(v, short); }
}

@Pipe({ name: 'when' })
export class WhenPipe implements PipeTransform {
  transform(v: string | Date | null | undefined, style: 'day' | 'full' | 'time' | 'month' | 'ago' | 'when' = 'day') { return formatDate(v, style); }
}

@Pipe({ name: 'initials' })
export class InitialsPipe implements PipeTransform {
  transform(v: string) { return initials(v); }
}

export const sourceLabel: Record<LeadSource, string> = {
  Website: 'الموقع', Referral: 'توصية', Social: 'تواصل اجتماعي', Event: 'فعالية', ColdCall: 'اتصال بارد', Other: 'أخرى'
};

export const leadStatusLabel: Record<LeadStatus, string> = {
  New: 'جديد', Contacted: 'تم التواصل', Qualified: 'مؤهَّل', Disqualified: 'مستبعد', Converted: 'تحوّل لصفقة'
};

export const activityLabel: Record<ActivityType, string> = {
  Call: 'مكالمة', Meeting: 'اجتماع', Email: 'بريد', Task: 'مهمة', Note: 'ملاحظة', System: 'النظام'
};

export const roleLabel: Record<Role, string> = { Rep: 'مندوب مبيعات', Manager: 'مدير مبيعات', Admin: 'مدير النظام' };

export const sources: LeadSource[] = ['Website', 'Referral', 'Social', 'Event', 'ColdCall', 'Other'];
