# Pich field app

The phone app for reps and field crews: today's appointments and tasks, tasks, leads, and on a lead: call, text,
email, directions, notes, photos, documents (saved proposal and work order PDFs) and the timeline. It's an
[Expo](https://expo.dev) app (React Native, SDK 58, Expo Router) that talks to the Pich API (`/api/v1`) with the same
record scope and permissions as the web.

No Mac is needed: develop on Windows, try it on an iPhone or Android phone with the free **Expo Go** app, and build
the store apps in Expo's cloud (EAS Build).

## Run it

```powershell
cd mobile
npm install
npx expo start
```

- **On your phone:** install Expo Go, connect the phone to the same Wi-Fi as this PC, and scan the QR code shown in the
  terminal (iPhone: with the Camera app). On the sign-in screen, the server is this PC's address on your network, e.g.
  `http://192.168.1.20:5169`. The web app must listen on the network for that, not just on localhost:
  `dotnet run --project src/Oes.Web --launch-profile http --urls http://0.0.0.0:5169` (Windows may ask to allow it
  through the firewall).
- **In a browser (quick checks):** `npx expo start --web`, then sign in with server `http://localhost:5169`. The web
  app allows this origin in Development only. Files open in a new tab instead of the phone's viewer.

## Check it

```powershell
npx tsc --noEmit
npx expo lint
```

CI runs both on every push.

## How it's built

- `src/app/`: screens (Expo Router). `sign-in`, the tabs (`index` = Today, `tasks`, `leads`, `account`) and
  `opportunity/[id]`. Signed-out users only see sign-in (`Stack.Protected`).
- `src/lib/api.ts`: the API client and response types. Access tokens last 30 minutes and are refreshed with the
  30-day refresh token; when that's refused too, the app signs out.
- `src/lib/storage.ts`: tokens in the phone's secure storage (Keychain / Keystore). `storage.web.ts` is the browser
  fallback (this tab's session only).
- `src/lib/files.ts`: opens a document by downloading it with the token and showing it in the phone's viewer.
- `src/lib/theme.ts`: Pich colors.

## Not yet

- Store builds: needs an Expo account, an Apple Developer account and a Google Play developer account. The bundle id
  `com.pich.field` in `app.json` is a placeholder until the real one is chosen.

## Offline

Screens keep their last good data on the phone (`src/lib/useCached.ts`, `cache-store.ts`) and show it, marked
"Offline", without a signal; the app also opens offline. Sign-out wipes it.

Notes, photos, new tasks and ticking tasks done go through an outbox (`src/lib/outbox.tsx`): saved on the phone first
(`outbox-store.ts`: a JSON file and copies of queued photos in the app's documents folder), then sent in order. With
no connection they wait, and go when the connection returns, when the app comes back to the front, or every minute.
Each item keeps one `Idempotency-Key` through every retry, so the server never applies it twice. Something the server
refuses (say, the lead was deleted) waits on the Account tab with the reason, for Try again or Discard.

To try it: open a lead, turn on airplane mode, add a note and a photo (the app says "Saved on this phone"), turn
airplane mode off, and watch the "waiting to send" banner empty and the note appear in the timeline.

## Push notifications

New tasks, appointments, online bookings and texts can buzz the phone. The app asks permission only when you tap
**Turn on notifications** on the Account tab, then registers the phone with the server (`src/lib/push.ts`); signing
out unregisters it. Tapping a notification opens the lead, Tasks or Today. The server's worker sends them through
Expo's push service (`PushJob`).

It's built but switched off until these exist (Expo Go can't receive pushes on Android, and the push address needs an
Expo project):

1. An Expo account, then `npx eas-cli init` in `mobile/` (adds the project id to `app.json`).
2. A Firebase project for Android, and an Apple Developer account for the iOS push key (`npx eas-cli credentials`).
3. A development build to install on your phone: `npx eas-cli build --profile development`.
4. On the worker: `Push:Provider=Expo` (optionally `Push:ExpoAccessToken`, from Expo's enhanced push security).
