import NetInfo from '@react-native-community/netinfo';
import * as Crypto from 'expo-crypto';
import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState } from 'react-native';

import { Api, ApiError, fileForm } from './api';
import { dropFile, keepFile, loadItems, saveItems } from './outbox-store';
import { useSession } from './session';

/**
 * Changes made in the field (notes, photos, new tasks, ticking tasks done) go through the outbox: saved on the phone
 * first, then sent in order. Without a connection they wait and go when it's back (or when the app is opened again).
 * Each item keeps one Idempotency-Key through every retry, so the server never applies it twice.
 */
export type OutboxItem = {
  key: string;
  owner: { server: string; userId: string; tenantId: string };
  kind: 'note' | 'photo' | 'task' | 'task_done' | 'stage' | 'appointment' | 'lead';
  method: 'POST' | 'PUT';
  path: string;
  json?: unknown;
  file?: { uri: string; name: string; type: string };
  /** What it is, for the waiting list: "Note on OPP-2026-0007". */
  label: string;
  createdAt: string;
  attempts: number;
  /** Set when the server refused it for good (it won't be retried until you say so). */
  error?: string | null;
};

export type NewItem = Pick<OutboxItem, 'kind' | 'method' | 'path' | 'json' | 'file' | 'label'>;
export type Outcome = { status: 'sent'; body?: unknown } | { status: 'queued' } | { status: 'failed'; message: string };

type OutboxValue = {
  /** This user's items, oldest first. */
  items: OutboxItem[];
  waiting: number;
  failed: number;
  sending: boolean;
  enqueue: (item: NewItem) => Promise<Outcome>;
  flush: () => Promise<void>;
  retry: (key: string) => Promise<void>;
  discard: (key: string) => Promise<void>;
};

const Ctx = createContext<OutboxValue | null>(null);

/** Worth trying again later: no connection, the server busy or broken, or the first copy still running. */
const retryable = (e: unknown) => !(e instanceof ApiError) || e.status === 0 || e.status >= 500 || [401, 408, 409, 429].includes(e.status);

/** A reply's JSON, if it has one (a new lead's id, say). */
async function body(res: Response): Promise<unknown> {
  if (res.status === 204) return undefined;
  try {
    return await res.json();
  } catch {
    return undefined;
  }
}

async function send(api: Api, item: OutboxItem): Promise<unknown> {
  const headers: Record<string, string> = { 'Idempotency-Key': item.key };
  if (item.file) {
    return body(await api.send(item.path, { method: item.method, body: await fileForm(item.file.uri, item.file.name, item.file.type), headers }));
  } else {
    return body(await api.send(item.path, { method: item.method, body: JSON.stringify(item.json ?? {}), headers: { ...headers, 'Content-Type': 'application/json' } }));
  }
}

export function OutboxProvider({ children }: { children: ReactNode }) {
  const session = useSession();
  const [all, setAll] = useState<OutboxItem[]>([]);
  const current = useRef<OutboxItem[]>([]);
  const [sending, setSending] = useState(false);
  const busy = useRef(false);
  /** Replies to items sent straight away, for enqueue to hand back. */
  const replies = useRef(new Map<string, unknown>());

  const api = session.status === 'signed-in' ? session.api : null;
  const me = session.status === 'signed-in' ? session.me : null;
  const isMine = useCallback(
    (i: OutboxItem) => !!api && !!me && i.owner.server === api.server && i.owner.userId === me.userId && i.owner.tenantId === me.tenantId,
    [api, me],
  );

  const update = useCallback(async (change: (items: OutboxItem[]) => OutboxItem[]) => {
    current.current = change(current.current);
    setAll(current.current);
    await saveItems(current.current);
  }, []);

  useEffect(() => {
    loadItems().then((items) => {
      current.current = items;
      setAll(items);
    });
  }, []);

  const flush = useCallback(async () => {
    if (!api || busy.current) return;
    busy.current = true;
    setSending(true);
    try {
      for (const item of current.current.filter((i) => isMine(i) && !i.error)) {
        try {
          replies.current.set(item.key, await send(api, item));
          if (item.file) dropFile(item.file.uri);
          await update((list) => list.filter((i) => i.key !== item.key));
        } catch (e) {
          if (retryable(e)) {
            await update((list) => list.map((i) => (i.key === item.key ? { ...i, attempts: i.attempts + 1 } : i)));
            break; // offline or the server's struggling: keep the order and try again later
          }
          const message = e instanceof ApiError ? e.message : 'It was refused.';
          await update((list) => list.map((i) => (i.key === item.key ? { ...i, attempts: i.attempts + 1, error: message } : i)));
        }
      }
    } finally {
      busy.current = false;
      setSending(false);
    }
  }, [api, isMine, update]);

  // Send when the connection comes back, when the app comes back to the front, and every minute while anything waits.
  const mine = useMemo(() => all.filter(isMine), [all, isMine]);
  const waiting = mine.filter((i) => !i.error).length;
  useEffect(() => {
    if (!api) return;
    const net = NetInfo.addEventListener((s) => {
      if (s.isConnected && s.isInternetReachable !== false) flush();
    });
    const app = AppState.addEventListener('change', (s) => {
      if (s === 'active') flush();
    });
    return () => {
      net();
      app.remove();
    };
  }, [api, flush]);
  useEffect(() => {
    if (!api || waiting === 0) return;
    const timer = setInterval(flush, 60_000);
    return () => clearInterval(timer);
  }, [api, waiting, flush]);

  const enqueue = useCallback(
    async (input: NewItem): Promise<Outcome> => {
      if (!api || !me) return { status: 'failed', message: 'Sign in first.' };
      const key = Crypto.randomUUID().replace(/-/g, '');
      const file = input.file ? { ...input.file, uri: await keepFile(input.file.uri, key, input.file.name) } : undefined;
      const item: OutboxItem = {
        ...input, file, key, owner: { server: api.server, userId: me.userId, tenantId: me.tenantId },
        createdAt: new Date().toISOString(), attempts: 0, error: null,
      };
      await update((list) => [...list, item]);
      await flush();
      const after = current.current.find((i) => i.key === key);
      if (!after) { const reply = replies.current.get(key); replies.current.delete(key); return { status: 'sent', body: reply }; }
      return after.error ? { status: 'failed', message: after.error } : { status: 'queued' };
    },
    [api, me, update, flush],
  );

  const retry = useCallback(async (key: string) => {
    await update((list) => list.map((i) => (i.key === key ? { ...i, error: null } : i)));
    await flush();
  }, [update, flush]);

  const discard = useCallback(async (key: string) => {
    const item = current.current.find((i) => i.key === key);
    if (item?.file) dropFile(item.file.uri);
    await update((list) => list.filter((i) => i.key !== key));
  }, [update]);

  const value = useMemo<OutboxValue>(
    () => ({ items: mine, waiting, failed: mine.length - waiting, sending, enqueue, flush, retry, discard }),
    [mine, waiting, sending, enqueue, flush, retry, discard],
  );
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useOutbox(): OutboxValue {
  const v = useContext(Ctx);
  if (!v) throw new Error('useOutbox outside OutboxProvider');
  return v;
}

/** The message to show after queueing something. */
export function outcomeMessage(o: Outcome, sentText: string): { text: string; kind: 'success' | 'danger' } {
  if (o.status === 'sent') return { text: sentText, kind: 'success' };
  if (o.status === 'queued') return { text: 'Saved on this phone. It will send when you’re back online.', kind: 'success' };
  return { text: o.message, kind: 'danger' };
}
