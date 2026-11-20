import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';

import { OutboxProvider } from '@/lib/outbox';
import { SessionProvider, useSession } from '@/lib/session';
import { colors } from '@/lib/theme';

SplashScreen.preventAutoHideAsync();

function Navigator() {
  const session = useSession();
  const signedIn = session.status === 'signed-in';

  useEffect(() => {
    if (session.status !== 'loading') SplashScreen.hideAsync();
  }, [session.status]);

  if (session.status === 'loading') return null; // the splash screen stays up

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.navy },
        headerTintColor: colors.white,
        headerTitleStyle: { fontWeight: '600' },
        contentStyle: { backgroundColor: colors.cloud },
      }}>
      <Stack.Protected guard={signedIn}>
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="opportunity/[id]" options={{ title: 'Lead' }} />
      </Stack.Protected>
      <Stack.Protected guard={!signedIn}>
        <Stack.Screen name="sign-in" options={{ headerShown: false }} />
      </Stack.Protected>
    </Stack>
  );
}

export default function RootLayout() {
  return (
    <SessionProvider>
      <StatusBar style="light" />
      <OutboxProvider>
        <Navigator />
      </OutboxProvider>
    </SessionProvider>
  );
}
