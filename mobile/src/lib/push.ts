import Constants, { ExecutionEnvironment } from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import { router, type Href } from 'expo-router';
import { useEffect, useRef } from 'react';
import { Platform } from 'react-native';

import type { Api } from './api';
import { storage } from './storage';

/**
 * Phone notifications. The app asks permission only when you tap "Turn on notifications" (Account), then registers
 * this phone's Expo push address with the server, which pushes new tasks, appointments, bookings and texts.
 * Tapping one opens the matching screen.
 */
export type PushState = 'on' | 'off' | 'denied' | 'unavailable';

const KEY = 'pich.pushToken';
const projectId = (): string | undefined => Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;

// Shown while the app is open, too.
Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldPlaySound: true, shouldSetBadge: true, shouldShowBanner: true, shouldShowList: true }),
});

/** Why notifications can't work here, or null when they can. */
export function unavailableReason(): string | null {
  if (!Device.isDevice) return 'Notifications need a real phone, not a simulator.';
  if (Platform.OS === 'android' && Constants.executionEnvironment === ExecutionEnvironment.StoreClient)
    return 'Notifications work in the installed Pich app, not in Expo Go.';
  if (!projectId()) return 'Notifications need the installed Pich app (its Expo project isn’t set up yet).';
  return null;
}

export async function pushStatus(): Promise<PushState> {
  if (unavailableReason()) return 'unavailable';
  const p = await Notifications.getPermissionsAsync();
  return p.granted ? 'on' : p.canAskAgain ? 'off' : 'denied';
}

/**
 * Registers this phone. With ask=false (on every launch) it only refreshes an existing permission; with ask=true
 * (the button) it shows the system prompt when it still can.
 */
export async function enablePush(api: Api, ask: boolean): Promise<PushState> {
  if (unavailableReason()) return 'unavailable';
  if (Platform.OS === 'android') {
    // Android 13+ shows the permission prompt only once a channel exists.
    await Notifications.setNotificationChannelAsync('default', { name: 'Pich', importance: AndroidImportance.HIGH, lightColor: '#FF7900' });
  }
  let p = await Notifications.getPermissionsAsync();
  if (!p.granted && ask && p.canAskAgain) p = await Notifications.requestPermissionsAsync();
  if (!p.granted) return p.canAskAgain ? 'off' : 'denied';
  const token = (await Notifications.getExpoPushTokenAsync({ projectId: projectId() })).data;
  await api.post('/api/v1/devices', { token, platform: Platform.OS });
  await storage.set(KEY, token);
  return 'on';
}

/** On sign-out: the server stops pushing to this phone for this user. Best effort (offline sign-out still signs out). */
export async function disablePush(api: Api): Promise<void> {
  const token = await storage.get(KEY);
  if (!token) return;
  try {
    await api.post('/api/v1/devices/unregister', { token });
  } catch {
    /* offline: the server switches it off when Expo reports it gone, or when someone else registers the phone */
  }
  await storage.remove(KEY);
}

/** The app screen for a notification's web link: a lead opens its lead, tasks open Tasks, anything else Today. */
export function routeFor(url: unknown): Href {
  const u = typeof url === 'string' ? url : '';
  const lead = /^\/Opportunities\/View\/([0-9a-f-]{36})/i.exec(u);
  if (lead) return `/opportunity/${lead[1]}`;
  if (/^\/Tasks\b/i.test(u)) return '/tasks';
  return '/';
}

/** Opens the matching screen when a notification is tapped (including the one that launched the app). */
export function usePushTaps(enabled: boolean): void {
  const last = Notifications.useLastNotificationResponse();
  const handled = useRef<string | null>(null);
  useEffect(() => {
    if (!enabled || !last || last.actionIdentifier !== Notifications.DEFAULT_ACTION_IDENTIFIER) return;
    const id = last.notification.request.identifier;
    if (handled.current === id) return;
    handled.current = id;
    router.push(routeFor(last.notification.request.content.data?.url));
    Notifications.clearLastNotificationResponseAsync();
  }, [enabled, last]);
}
