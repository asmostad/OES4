import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';

import { OutboxProvider } from '@/lib/outbox';
import { usePushTaps } from '@/lib/push';
import { SessionProvider, useSession } from '@/lib/session';
import { colors } from '@/lib/theme';

SplashScreen.preventAutoHideAsync();

function Navigator() {
  const session = useSession();
  const signedIn = session.status === 'signed-in';
  usePushTaps(signedIn); // a tapped notification opens its screen once signed in

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
        <Stack.Screen name="book/[id]" options={{ title: 'Book appointment', presentation: 'modal' }} />
        <Stack.Screen name="new-lead" options={{ title: 'New lead', presentation: 'modal' }} />
        <Stack.Screen name="search" options={{ title: 'Search' }} />
        <Stack.Screen name="notifications" options={{ title: 'Notifications' }} />
        <Stack.Screen name="calls" options={{ title: 'Call list' }} />
        <Stack.Screen name="project/[id]" options={{ title: 'Project' }} />
        <Stack.Screen name="trade/[id]" options={{ title: 'Trade' }} />
        <Stack.Screen name="contact/[id]" options={{ title: 'Contact' }} />
        <Stack.Screen name="property/[id]" options={{ title: 'Property' }} />
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
