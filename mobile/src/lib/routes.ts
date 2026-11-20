import type { Href } from 'expo-router';

const ID = '([0-9a-f-]{36})';

/** The app screen for a web link (a notification's): leads, contacts and properties open their screen; tasks and the calendar their tab. */
export function routeFor(url: unknown): Href | null {
  const u = typeof url === 'string' ? url : '';
  const m = (page: string) => new RegExp(`^/${page}/View/${ID}`, 'i').exec(u)?.[1];
  const lead = m('Opportunities');
  if (lead) return `/opportunity/${lead}`;
  const contact = m('Contacts');
  if (contact) return `/contact/${contact}`;
  const property = m('Properties');
  if (property) return `/property/${property}`;
  if (/^\/Tasks\b/i.test(u)) return '/tasks';
  if (/^\/Calendar\b/i.test(u)) return '/calendar';
  return null;
}
