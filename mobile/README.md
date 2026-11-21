# Pich field app

The phone app for reps, project managers and field crews. It's an [Expo](https://expo.dev) app (React Native, SDK 58,
Expo Router) that talks to the Pich API (`/api/v1`) with the same record scope and permissions as the web. What it does:

- **Today, Calendar, Tasks:** appointments (open one to get directions, mark it done, move it or cancel it), overdue
  and due tasks (tick, add, edit), and texts from unknown numbers for whoever handles them.
- **Leads:** search, New lead, the call list, and storm **canvassing** (houses in a storm's ZIP codes with no lead yet,
  nearest first). A lead has call, text, email and directions, stage changes, tasks, appointments, notes, photos,
  documents, the timeline, its claim, its estimates (open the proposal PDF, issue, record signed or declined) and
  "Ask the assistant".
- **Jobs:** the schedule of trades and projects; move stages, work orders (mark done), change orders (add one on site,
  get the customer's approval, open the PDF).
- **Contacts and properties:** edit a name, phone or email; a property's people, leads and **measurements** (take a
  new version on site).
- **Texts and email** from the company's number and mailbox, so they land on the record; the **assistant**; your
  **booking hours**; notifications; search.

Building estimates, itemizing change orders, issuing work orders, settings and reports stay on the web.

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

- `src/app/`: screens (Expo Router). `sign-in`, the tabs (`index` = Today, `calendar`, `tasks`, `leads`, `jobs`,
  `account`) and a screen per record (`opportunity/[id]`, `contact/[id]`, `project/[id]`, `estimate/[id]`, …), with
  forms as modals (`compose`, `measure`, `new-lead`, `edit-*`). Every screen is registered in `_layout.tsx`.
  Signed-out users only see sign-in (`Stack.Protected`).
- `src/components/`: shared pieces: `ui.tsx` (cards, buttons, fields, chips), `RecordFeed.tsx` (notes, photos,
  documents and timeline for any record), `Records.tsx` (lists of leads, estimates, claims, work orders),
  `StageMover.tsx`.
- `src/lib/routes.ts`: maps a web link (a notification's) to the app screen.
- `src/lib/api.ts`: the API client and response types. Access tokens last 30 minutes and are refreshed with the
  30-day refresh token; when that's refused too, the app signs out.
- `src/lib/storage.ts`: tokens in the phone's secure storage (Keychain / Keystore). `storage.web.ts` is the browser
  fallback (this tab's session only).
- `src/lib/files.ts`: opens a document by downloading it with the token and showing it in the phone's viewer.
- `src/lib/theme.ts`: Pich colors.

## Building for phones and the stores

`eas.json` has three build profiles, all built in Expo's cloud (no Mac needed):

- `development`: a build of this app with the developer menu (`expo-dev-client`), installed on your own phones. Needed
  for push notifications and anything Expo Go can't run.
- `preview`: a normal build shared with testers (internal distribution, or TestFlight).
- `production`: the store build; the build number goes up by itself.

What has to exist first (your accounts; nothing here creates them):

1. An Expo account, then `npx eas-cli login` and `npx eas-cli init` in `mobile/` (adds the project id to `app.json`).
2. The real bundle id in place of the placeholder `com.pich.field` in `app.json` (iOS `bundleIdentifier`, Android
   `package`). It can't change after the first store release.
3. An Apple Developer account (for iOS) and a Google Play developer account (for Android).

Then: `npx eas-cli build --profile development --platform ios` (or `preview`, `production`), and
`npx eas-cli submit --platform ios` to send a production build to App Store Connect.

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
