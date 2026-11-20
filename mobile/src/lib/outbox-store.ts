import { Directory, File, Paths } from 'expo-file-system';

import type { OutboxItem } from './outbox';

/**
 * The outbox on the phone: a JSON file in the app's documents folder, and copies of queued photos next to it, so
 * both survive the app closing (the camera's own temporary file can be cleaned up by the phone).
 */
function folder(...parts: string[]): Directory {
  let d = new Directory(Paths.document, 'outbox');
  if (!d.exists) d.create();
  for (const p of parts) {
    d = new Directory(d, p);
    if (!d.exists) d.create();
  }
  return d;
}

export async function loadItems(): Promise<OutboxItem[]> {
  const f = new File(folder(), 'outbox.json');
  if (!f.exists) return [];
  try {
    return JSON.parse(await f.text()) as OutboxItem[];
  } catch {
    return [];
  }
}

export async function saveItems(items: OutboxItem[]): Promise<void> {
  const f = new File(folder(), 'outbox.json');
  if (!f.exists) f.create();
  await f.write(JSON.stringify(items));
}

/** Copies a picked photo into the outbox and returns the copy's uri. */
export async function keepFile(uri: string, key: string, name: string): Promise<string> {
  const target = new File(folder('files'), `${key}-${name.replace(/[^A-Za-z0-9._-]/g, '_')}`);
  await new File(uri).copy(target);
  return target.uri;
}

export function dropFile(uri: string): void {
  try {
    const f = new File(uri);
    if (f.exists) f.delete();
  } catch {
    /* already gone */
  }
}
