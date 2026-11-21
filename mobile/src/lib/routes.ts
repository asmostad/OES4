import type { Href } from 'expo-router';

const ID = '([0-9a-f-]{36})';

/** The app screen for a web link (a notification's): leads, contacts, properties, projects and trades open their screen; tasks and the calendar their tab. */
export function routeFor(url: unknown): Href | null {
  const u = typeof url === 'string' ? url : '';
  const m = (page: string) => new RegExp(`^/${page}/View/${ID}`, 'i').exec(u)?.[1];
  const lead = m('Opportunities');
  if (lead) return `/opportunity/${lead}`;
  const contact = m('Contacts');
  if (contact) return `/contact/${contact}`;
  const project = m('Projects');
  if (project) return `/project/${project}`;
  const changeOrder = new RegExp(`^/Projects/ChangeOrder/${ID}`, 'i').exec(u)?.[1];
  if (changeOrder) return `/change-order/${changeOrder}`;
  const trade = new RegExp(`^/Projects/Trade/${ID}`, 'i').exec(u)?.[1];
  if (trade) return `/trade/${trade}`;
  const workOrder = m('WorkOrders');
  if (workOrder) return `/work-order/${workOrder}`;
  const claim = m('Claims');
  if (claim) return `/claim/${claim}`;
  const estimate = m('Estimates');
  if (estimate) return `/estimate/${estimate}`;
  const property = m('Properties');
  if (property) return `/property/${property}`;
  if (/^\/Messages\/Unmatched\b/i.test(u)) return '/texts';
  if (/^\/Tasks\b/i.test(u)) return '/tasks';
  if (/^\/Calendar\b/i.test(u)) return '/calendar';
  return null;
}
