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
        <Stack.Screen name="compose" options={{ title: 'Send', presentation: 'modal' }} />
        <Stack.Screen name="message/[channel]/[id]" options={{ title: 'Message' }} />
        <Stack.Screen name="estimate/[id]" options={{ title: 'Estimate' }} />
        <Stack.Screen name="work-order/[id]" options={{ title: 'Work order' }} />
        <Stack.Screen name="claim/[id]" options={{ title: 'Claim' }} />
        <Stack.Screen name="canvass" options={{ title: 'Canvass' }} />
        <Stack.Screen name="hours" options={{ title: 'Booking hours' }} />
        <Stack.Screen name="appointment/[id]" options={{ title: 'Appointment' }} />
        <Stack.Screen name="edit-contact" options={{ title: 'Edit contact', presentation: 'modal' }} />
        <Stack.Screen name="texts" options={{ title: 'Unknown numbers' }} />
        <Stack.Screen name="change-order/[id]" options={{ title: 'Change order' }} />
        <Stack.Screen name="new-change-order" options={{ title: 'Change order', presentation: 'modal' }} />
        <Stack.Screen name="assistant/index" options={{ title: 'Assistant' }} />
        <Stack.Screen name="assistant/[id]" options={{ title: 'Assistant' }} />
        <Stack.Screen name="measure"options={{ title: 'Measure', presentation: 'modal' }} />
        <Stack.Screen name="measurements/[id]" options={{ title: 'Measurements' }} />
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
