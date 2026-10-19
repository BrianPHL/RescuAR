# RescuAR outbound SMS — Android SIM gateway (implementation patch)

## Files modified
- src/App.jsx: replaces SMS Parser placeholder with outbound SMS composer
- src/components/SmsOutboundConsole.jsx + CSS: admin composer, registered-user picker, queue history
- database/migrations/004_sms_outbound.sql: protected outbox and atomic queue claim RPC
- supabase/functions/sms-gateway/index.ts: administrator-verified enqueue and device-verified claim/report
- android-sms-gateway/: Android Studio source for Android phone SIM sender

## Set up backend FIRST
1. Run `database/migrations/004_sms_outbound.sql` in your existing Supabase SQL Editor.
2. Confirm your existing `public.admin_roles` and `public.get_all_users_with_roles()` function are present. This implementation assumes its rows include `id, phone_number, status, first_name, last_name`; the RPC is already used by `UserManagement.jsx`. Back-end strictly limits recipients to active registered phone numbers; verify your real onboarding consent policy before production.
3. Generate a high-entropy gateway secret (at least 32 random bytes). Never place it in a `VITE_*` variable, browser, repository, or published docs.
4. Set Supabase Edge Function secrets `SMS_GATEWAY_KEY` and `SMS_ADMIN_ORIGIN=https://your-admin-site.example` (no trailing slash). Deploy the `sms-gateway` function with *JWT verification disabled*, because the function performs separate device-key authorization and uses `auth.getUser()` for real admin JWT validation.
   CLI example: `supabase secrets set SMS_GATEWAY_KEY=<secret> SMS_ADMIN_ORIGIN=https://your-admin-site.example`
   `supabase functions deploy sms-gateway --no-verify-jwt`
   The SMS_GATEWAY_KEY is **not** the Supabase service role key. Supabase injects its own service role key into the Edge Function environment. Do not expose either to the frontend.
5. Check existing website `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY` are valid. Deploy the website changes.

## Set up Android phone
1. Open `android-sms-gateway/` in Android Studio with JDK 17 and Android SDK 35. Sync Gradle and build/install the debug APK on a SIM-equipped Android device.
2. Grant SEND_SMS permission and notification permission. Device must have an active Philippine SIM, SMS load/plan, cellular connectivity, and Internet for queue polling.
3. Enter URL `https://<your-project-ref>.supabase.co/functions/v1/sms-gateway`, generated `SMS_GATEWAY_KEY`, and device ID `rescuar-gateway-01`. Press Start SMS gateway.
4. Keep the phone on charge, exclude the app from battery optimization, and monitor the foreground notification. Android OS may still throttle or stop the gateway; on newer Android versions foreground data-sync services have execution limits. This is a prototype gateway, not guaranteed 24x7 delivery.
5. In RescuAR.Admin > Community Management > SMS Parser (now outbound), pick an active registered user, enter a **one-segment** SMS, confirm, and queue. Refresh history; it should move queued -> claimed -> sent/failed/unknown.

## Important limitations and safety
- `sent` means Android reports a successful SMS submission to the network, **not confirmed recipient delivery**. The gateway does not request carrier delivery receipts.
- This first version only supports **single-segment** SMS. The backend rejects messages above conservative single-segment character limits; some GSM-7 extension characters still require two units, so Android checks the actual carrier segmentation before sending. Keep texts short (typically <=160 GSM-7 characters or <=70 Unicode characters; actual segmentation depends on encoding).
- A `claimed` message stuck because of device shutdown/network failure is deliberately NOT automatically retried; blindly retrying can duplicate urgent alerts. Reconcile those manually after inspecting the phone and carrier logs.
- Sends sequentially at low speed. Carrier/Android SMS rate limits and anti-spam policies apply; test with approved recipients first. Bulk emergency deployment requires agreement with carrier, documented consent or applicable lawful emergency authority, and formal operational testing.
- Current module exposes queue history to all `admin_roles`; add role-specific permissions before wider staff access. Add audit and opt-in/opt-out policy as appropriate.
- This system sends native SMS to registered phone numbers, **not an in-app push notification**. The mobile app need not be running.
- Do not use this as the sole life-safety alert channel. Maintain redundant official channels and human verification.
- Never store the device secret in Git. Device key is stored in Android SharedPreferences (not encrypted): use a dedicated locked-down gateway handset and rotate it if compromised.
- The existing `SmsParserConsole.jsx` is retained but no longer routed; remove later after validation if unused.

## Verification checklist
- [ ] Admin login can load SMS composer and active user picker
- [ ] Non-admin JWT cannot enqueue
- [ ] Anonymous requests cannot enqueue
- [ ] Wrong gateway key cannot claim or report
- [ ] Nonregistered phone numbers rejected server-side
- [ ] Valid phone number accepted, queued row created
- [ ] Gateway online -> claim one job -> network-send result returned
- [ ] Offline gateway -> job stays queued
- [ ] Carrier failure -> failed row with reason
- [ ] Device killed mid-send -> claimed remains for operator reconciliation
- [ ] Two devices cannot claim same queued job simultaneously
- [ ] Test real Android 13/14/15 permissions, notifications, and background restrictions

## Deployment notes
- Current patch has not been verified by a full Vite build or Android APK compilation in this environment.
- Only authorized staff should hold admin credentials. Limit access to the handset and its stored gateway key.
- The SMS gateway uses a separate Android project, not RescuAR.MAUI. It must be installed on the SIM-equipped device.
