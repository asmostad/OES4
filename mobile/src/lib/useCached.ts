import { useFocusEffect } from 'expo-router';
import { useCallback, useRef, useState } from 'react';

import { ApiError } from './api';
import { cacheGet, cacheSet } from './cache-store';
import { useApi } from './session';

export type CachedState<T> = {
  data: T | null;
  /** When the data shown was fetched (the saved copy's time when offline). */
  savedAt: string | null;
  /** True when the server couldn't be reached and this is the saved copy. */
  offline: boolean;
  error: string | null;
  refreshing: boolean;
  reload: () => Promise<void>;
};

/**
 * A GET whose last good answer is kept on the phone. The saved copy shows at once while the screen refreshes;
 * without a signal it stays, marked offline, instead of an error. Refreshes whenever the screen comes into view.
 * Saved per server, company and user, so another account never sees it.
 */
export function useCached<T>(path: string, fallbackError: string): CachedState<T> {
  const { api, me } = useApi();
  const key = `${api.server}|${me.tenantId}|${me.userId}|${path}`;
  const [data, setData] = useState<T | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [offline, setOffline] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const shownKey = useRef<string | null>(null);

  const reload = useCallback(async () => {
    setRefreshing(true);
    // Show the saved copy first when this screen has nothing on it yet (opening it, or a new search).
    if (shownKey.current !== key) {
      shownKey.current = key;
      const saved = await cacheGet<T>(key);
      setData(saved?.data ?? null);
      setSavedAt(saved?.savedAt ?? null);
    }
    try {
      const fresh = await api.get<T>(path);
      setData(fresh);
      setSavedAt(await cacheSet(key, fresh));
      setOffline(false);
      setError(null);
    } catch (e) {
      const unreachable = e instanceof ApiError && e.status === 0;
      const saved = unreachable ? await cacheGet<T>(key) : null;
      if (saved) {
        setData(saved.data);
        setSavedAt(saved.savedAt);
        setOffline(true);
        setError(null);
      } else {
        setOffline(unreachable);
        setError(e instanceof ApiError ? e.message : fallbackError);
      }
    } finally {
      setRefreshing(false);
    }
  }, [api, key, path, fallbackError]);

  useFocusEffect(useCallback(() => { reload(); }, [reload]));

  return { data, savedAt, offline, error, refreshing, reload };
}
