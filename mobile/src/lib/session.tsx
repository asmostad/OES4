import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';

import { Api, clearTokens, loadTokens, Me, saveServer, savedServer, signIn as apiSignIn, TenantOption } from './api';
import { disablePush, enablePush } from './push';

type Session =
  | { status: 'loading' }
  | { status: 'signed-out'; server: string }
  | { status: 'signed-in'; api: Api; me: Me };

type SessionValue = Session & {
  /** Signs in; returns the companies to choose from when the account belongs to several. */
  signIn: (server: string, email: string, password: string, tenantId?: string) => Promise<TenantOption[] | null>;
  signOut: () => Promise<void>;
};

const Ctx = createContext<SessionValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session>({ status: 'loading' });
  const apiRef = useRef<Api | null>(null);

  const signOut = useCallback(async () => {
    // Stop pushes to this phone for this user first, while the sign-in still works.
    if (apiRef.current) await disablePush(apiRef.current);
    apiRef.current = null;
    await clearTokens();
    setSession({ status: 'signed-out', server: await savedServer() });
  }, []);

  const start = useCallback(
    async (server: string, tokens: Awaited<ReturnType<typeof loadTokens>>) => {
      if (!tokens) return setSession({ status: 'signed-out', server });
      const api = new Api(server, tokens, () => void signOut());
      try {
        const me = await api.get<Me>('/api/v1/me');
        apiRef.current = api;
        setSession({ status: 'signed-in', api, me });
        // Keep this phone's push address current if notifications were already allowed (never prompts here).
        enablePush(api, false).catch(() => undefined);
      } catch {
        // Offline or refused: back to sign-in (the refresh token may have ended).
        setSession({ status: 'signed-out', server });
      }
    },
    [signOut],
  );

  // On launch: reuse the saved sign-in if it's still good.
  useEffect(() => {
    (async () => start(await savedServer(), await loadTokens()))();
  }, [start]);

  const signIn = useCallback(
    async (server: string, email: string, password: string, tenantId?: string) => {
      const result = await apiSignIn(server, email, password, tenantId);
      if (result.kind === 'choose') return result.tenants;
      await saveServer(server);
      await start(server, result.tokens);
      return null;
    },
    [start],
  );

  const value = useMemo(() => ({ ...session, signIn, signOut }) as SessionValue, [session, signIn, signOut]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useSession(): SessionValue {
  const v = useContext(Ctx);
  if (!v) throw new Error('useSession outside SessionProvider');
  return v;
}

/** The signed-in API client (screens under the tabs are only shown when signed in). */
export function useApi(): { api: Api; me: Me } {
  const s = useSession();
  if (s.status !== 'signed-in') throw new Error('Not signed in');
  return { api: s.api, me: s.me };
}
