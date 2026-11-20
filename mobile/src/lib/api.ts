import { Platform } from 'react-native';

import { storage } from './storage';

// ---------- Types: the server's JSON (camelCase), from src/Oes.Web/Controllers/Api ----------

export type Tokens = { accessToken: string; refreshToken: string; expiresIn: number };
export type TenantOption = { id: string; name: string };
export type Me = { userId: string; firstName: string; lastName: string; email: string; tenantId: string; tenantName: string; permissions: string[] };

/** Dates from the server's "local" fields are wall-clock times in the company's time zone, with no offset. */
export type TaskRow = {
  id: string; title: string; notes: string | null; dueLocal: string | null; hasDueTime: boolean; priority: string;
  assigneeName: string; completedAt: string | null; entityType: string | null; entityId: string | null;
  isOverdue: boolean; isDueToday: boolean; canEdit: boolean;
};
export type AppointmentRow = {
  id: string; title: string; typeKey: string; startLocal: string; endLocal: string; location: string | null; notes: string | null;
  assigneeName: string; status: string; entityType: string | null; entityId: string | null; isAllDay: boolean;
};
export type MyDay = { overdue: number; dueToday: number; tasks: TaskRow[]; appointments: AppointmentRow[] };
export type OpportunityRow = {
  id: string; number: string; name: string; stageName: string; stageColor: string; outcome: string;
  contactName: string; address: string | null; ownerName: string | null; expectedValue: number | null; isStale: boolean;
};
export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
export type ContactMethod = { kind: string; value: string; isPrimary: boolean };
export type Opportunity = {
  id: string; number: string; name: string; stageName: string; stageColor: string; outcome: string; expectedValue: number | null;
  ownerName: string | null; contactId: string; contactName: string; contactMethods: ContactMethod[];
  propertyId: string | null; address: string | null; description: string | null; canEdit: boolean;
};
export type TimelineItem = {
  itemType: string; id: string; at: string; authorName: string | null; text: string; entityType: string; entityId: string;
  fileName: string | null; subject: string | null; direction: string | null;
};
export type DocumentItem = { id: string; fileName: string; contentType: string; sizeBytes: number; category: string | null; createdAt: string };

// ---------- Errors ----------

/** A refusal from the server, with the message to show (ProblemDetails "detail"). */
export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: unknown) {
    super(message);
  }
}

async function problem(res: Response): Promise<ApiError> {
  let body: unknown;
  try {
    body = await res.json();
  } catch {
    body = undefined;
  }
  const b = body as { detail?: string; title?: string } | undefined;
  const fallback = res.status === 404 ? "That wasn't found, or you don't have access to it." : `The server said ${res.status}.`;
  return new ApiError(res.status, b?.detail || b?.title || fallback, body);
}

// ---------- Client ----------

const KEY_SERVER = 'pich.server';
const KEY_TOKENS = 'pich.tokens';

/** The server address without a trailing slash, e.g. https://app.pich.example or http://192.168.1.20:5169 in development. */
export function normalizeServer(raw: string): string {
  let s = raw.trim().replace(/\/+$/, '');
  if (s && !/^https?:\/\//i.test(s)) s = `https://${s}`;
  return s;
}

export const savedServer = async () => (await storage.get(KEY_SERVER)) ?? process.env.EXPO_PUBLIC_API_URL ?? '';
export const saveServer = (server: string) => storage.set(KEY_SERVER, server);

export async function loadTokens(): Promise<Tokens | null> {
  const raw = await storage.get(KEY_TOKENS);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Tokens;
  } catch {
    return null;
  }
}
const saveTokens = (t: Tokens) => storage.set(KEY_TOKENS, JSON.stringify(t));
export const clearTokens = () => storage.remove(KEY_TOKENS);

type SignInResult = { kind: 'ok'; tokens: Tokens } | { kind: 'choose'; tenants: TenantOption[] };

/** Email and password; with several companies, the server asks which one (409 choose_tenant). */
export async function signIn(server: string, email: string, password: string, tenantId?: string): Promise<SignInResult> {
  let res: Response;
  try {
    res = await fetch(`${server}/api/v1/auth/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ email, password, tenantId: tenantId ?? null }),
    });
  } catch {
    throw new ApiError(0, `Couldn't reach ${server}. Check the server address and that you're online.`);
  }
  if (res.status === 409) {
    const body = (await res.json()) as { tenants: TenantOption[] };
    return { kind: 'choose', tenants: body.tenants };
  }
  if (!res.ok) throw await problem(res);
  const tokens = (await res.json()) as Tokens;
  await saveTokens(tokens);
  return { kind: 'ok', tokens };
}

/** A multipart body with one file, for photo uploads. */
export async function fileForm(uri: string, fileName: string, mimeType: string): Promise<FormData> {
  const form = new FormData();
  if (Platform.OS === 'web') {
    const blob = await (await fetch(uri)).blob();
    form.append('file', blob, fileName);
  } else {
    // React Native's FormData takes a file reference ({ uri, name, type }) instead of a Blob.
    form.append('file', { uri, name: fileName, type: mimeType } as unknown as Blob);
  }
  return form;
}

/**
 * Calls the API as the signed-in user. An expired access token is refreshed once (refresh tokens last 30 days);
 * if that's refused too, onSignedOut runs and the call fails.
 */
export class Api {
  private refreshing: Promise<boolean> | null = null;

  constructor(public server: string, private tokens: Tokens, private onSignedOut: () => void) {}

  async json<T>(path: string, init: RequestInit = {}): Promise<T> {
    const res = await this.send(path, init);
    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
  }

  get = <T>(path: string) => this.json<T>(path);
  post = <T>(path: string, body: unknown) =>
    this.json<T>(path, { method: 'POST', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } });
  put = <T>(path: string, body: unknown) =>
    this.json<T>(path, { method: 'PUT', body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } });

  /** The current access token, for downloads that send their own request (file previews). */
  authHeader = () => ({ Authorization: `Bearer ${this.tokens.accessToken}` });

  async send(path: string, init: RequestInit, retried = false): Promise<Response> {
    let res: Response;
    try {
      res = await fetch(`${this.server}${path}`, {
        ...init,
        headers: { Accept: 'application/json', ...(init.headers ?? {}), ...this.authHeader() },
      });
    } catch {
      throw new ApiError(0, "Couldn't reach the server. Check your connection and try again.");
    }
    if (res.status === 401 && !retried) {
      if (await this.refresh()) return this.send(path, init, true);
      this.onSignedOut();
      throw new ApiError(401, 'Please sign in again.');
    }
    if (!res.ok) throw await problem(res);
    return res;
  }

  /** One refresh at a time, shared by every call that hit the expiry. */
  refresh(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const res = await fetch(`${this.server}/api/v1/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({ refreshToken: this.tokens.refreshToken }),
        });
        if (!res.ok) return false;
        this.tokens = (await res.json()) as Tokens;
        await saveTokens(this.tokens);
        return true;
      } catch {
        return false;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }
}

export type Option = { value: string; label: string };
export type StageOption = { id: string; name: string; color: string; outcome: string };
/** GET /api/v1/options: what the app's forms choose from. */
export type Options = { stages: StageOption[]; lostReasons: Option[]; appointmentTypes: Option[]; leadSources: Option[] };
