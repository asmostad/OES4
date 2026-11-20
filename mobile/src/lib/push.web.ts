import type { Href } from 'expo-router';

import type { Api } from './api';

/** In the browser (testing) there are no phone notifications. */
export type PushState = 'on' | 'off' | 'denied' | 'unavailable';

export const unavailableReason = () => 'Notifications work in the phone app.';
export const pushStatus = async (): Promise<PushState> => 'unavailable';
export const enablePush = async (_api: Api, _ask: boolean): Promise<PushState> => 'unavailable';
export const disablePush = async (_api: Api): Promise<void> => {};
export const routeFor = (_url: unknown): Href => '/';
export const usePushTaps = (_enabled: boolean): void => {};
