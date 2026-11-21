/**
 * The server's "local" times are the company's wall-clock time with no offset ("2026-11-20T09:30:00"). They're shown
 * as written, never converted through the phone's time zone.
 */
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

function parts(local: string) {
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/.exec(local);
  if (!m) return null;
  return { y: +m[1], mo: +m[2], d: +m[3], h: m[4] ? +m[4] : 0, mi: m[5] ? +m[5] : 0 };
}

export function time(local: string): string {
  const p = parts(local);
  if (!p) return '';
  const h12 = p.h % 12 === 0 ? 12 : p.h % 12;
  return `${h12}:${String(p.mi).padStart(2, '0')} ${p.h < 12 ? 'am' : 'pm'}`;
}

export function day(local: string): string {
  const p = parts(local);
  if (!p) return '';
  const weekday = DAYS[new Date(Date.UTC(p.y, p.mo - 1, p.d)).getUTCDay()];
  return `${weekday} ${MONTHS[p.mo - 1]} ${p.d}`;
}

/** A UTC timestamp from the server (timeline, files) in the phone's own time. */
export function when(utc: string): string {
  const d = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(utc) ? utc : `${utc}Z`);
  if (Number.isNaN(d.getTime())) return '';
  return `${MONTHS[d.getMonth()]} ${d.getDate()}, ${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
}

export const money = (n: number | null | undefined) =>
  n == null ? '' : `$${Math.round(n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',')}`;

/** "8", "8:30", "8:30am", "5 pm", "17:00" → "HH:mm" (24-hour); null when it isn't a time. */
export function parseTime(raw: string): string | null {
  const m = /^\s*(\d{1,2})(?::?(\d{2}))?\s*([ap])?\.?\s*m?\.?\s*$/i.exec(raw);
  if (!m) return null;
  let h = +m[1];
  const mi = m[2] ? +m[2] : 0;
  const ap = m[3]?.toLowerCase();
  if (mi > 59 || h > 23 || (ap && (h < 1 || h > 12))) return null;
  if (ap === 'p' && h < 12) h += 12;
  if (ap === 'a' && h === 12) h = 0;
  return `${String(h).padStart(2, '0')}:${String(mi).padStart(2, '0')}`;
}

/** "13:30" → "1:30 pm". */
export const clock = (hhmm: string) => time(`2000-01-01T${hhmm}`);

/** Dollars and cents, for prices and line amounts. */
export const cents = (n: number | null | undefined) =>
  n == null ? '' : `${n < 0 ? '-' : ''}$${Math.abs(n).toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, ',')}`;

export function size(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** yyyy-MM-dd for the phone's today (due dates are days, not times). */
export function todayIso(offsetDays = 0): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export const label = (key: string | null | undefined) => (key ? key.charAt(0).toUpperCase() + key.slice(1).replace(/_/g, ' ') : '');
