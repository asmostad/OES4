/** In the browser (testing): saved copies last for this tab only. */
export type Cached<T> = { data: T; savedAt: string };

const memory = new Map<string, Cached<unknown>>();

export async function cacheGet<T>(key: string): Promise<Cached<T> | null> {
  return (memory.get(key) as Cached<T> | undefined) ?? null;
}

export async function cacheSet<T>(key: string, data: T): Promise<string> {
  const savedAt = new Date().toISOString();
  memory.set(key, { data, savedAt });
  return savedAt;
}

export function clearCache(): void {
  memory.clear();
}
