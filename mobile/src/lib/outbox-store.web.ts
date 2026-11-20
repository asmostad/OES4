import type { OutboxItem } from './outbox';

/** In the browser (testing): the outbox lasts for this tab's session; photos stay as the picker's blob URLs. */
const KEY = 'pich.outbox';
let memory: OutboxItem[] = [];

export async function loadItems(): Promise<OutboxItem[]> {
  try {
    const raw = window.sessionStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as OutboxItem[]) : memory;
  } catch {
    return memory;
  }
}

export async function saveItems(items: OutboxItem[]): Promise<void> {
  memory = items;
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify(items));
  } catch {
    /* storage blocked: memory only */
  }
}

export async function keepFile(uri: string): Promise<string> {
  return uri;
}

export function dropFile(): void {}
