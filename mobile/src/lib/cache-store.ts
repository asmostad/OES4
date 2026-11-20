import { Directory, File, Paths } from 'expo-file-system';

/**
 * Saved copies of screens (the last good API answers), so the app still shows something without a signal. One file
 * per screen in the app's cache folder; the newest 150 are kept. Customer data: wiped on sign-out (clearCache).
 */
export type Cached<T> = { data: T; savedAt: string };

const MAX = 150;
const folder = () => {
  const d = new Directory(Paths.cache, 'screens');
  if (!d.exists) d.create();
  return d;
};
const fileFor = (key: string) => new File(folder(), `${key.replace(/[^A-Za-z0-9]/g, '_').slice(0, 180)}.json`);

export async function cacheGet<T>(key: string): Promise<Cached<T> | null> {
  try {
    const f = fileFor(key);
    return f.exists ? (JSON.parse(await f.text()) as Cached<T>) : null;
  } catch {
    return null;
  }
}

export async function cacheSet<T>(key: string, data: T): Promise<string> {
  const savedAt = new Date().toISOString();
  try {
    const f = fileFor(key);
    if (!f.exists) f.create();
    await f.write(JSON.stringify({ data, savedAt }));
    trim();
  } catch {
    /* a full disk just means no saved copy */
  }
  return savedAt;
}

/** Keeps the newest files. */
function trim() {
  try {
    const files = folder().list().filter((x): x is File => x instanceof File);
    if (files.length <= MAX) return;
    files.sort((a, b) => (a.modificationTime ?? 0) - (b.modificationTime ?? 0));
    for (const f of files.slice(0, files.length - MAX)) f.delete();
  } catch {
    /* best effort */
  }
}

export function clearCache(): void {
  try {
    const d = new Directory(Paths.cache, 'screens');
    if (d.exists) d.delete();
  } catch {
    /* already gone */
  }
}
