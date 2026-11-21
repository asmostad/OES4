/**
 * In the browser (used for testing only) there's no secure storage: keep values for this tab's session, so
 * nothing outlives closing it. The phone build uses storage.ts (SecureStore).
 */
const memory = new Map<string, string>();

function session(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

export const storage = {
  get: async (key: string) => {
    try {
      return session()?.getItem(key) ?? memory.get(key) ?? null;
    } catch {
      return memory.get(key) ?? null;
    }
  },
  set: async (key: string, value: string) => {
    memory.set(key, value);
    try {
      session()?.setItem(key, value);
    } catch {
      /* storage blocked: memory only */
    }
  },
  remove: async (key: string) => {
    memory.delete(key);
    try {
      session()?.removeItem(key);
    } catch {
      /* storage blocked */
    }
  },
};
