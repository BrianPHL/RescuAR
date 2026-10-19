# RescuAR SMS Gateway — Build APK with GitHub Actions

This is an **add-on** to `RescuAR_Android_SIM_SMS_Gateway_Patch.zip`, not a replacement.

## Setup in Antigravity IDE

1. Apply the earlier SMS Gateway patch to your RescuAR.Admin repository, preserving the `android-sms-gateway/` directory.
2. Copy `.github/workflows/build-sms-gateway.yml` from this package into the **repository root** `.github/workflows/` directory. The workflow assumes the repository root contains `android-sms-gateway/`. If your Git repository is rooted one folder above RescuAR.Admin, adjust the `paths`, working-directory, and artifact path accordingly.
3. Commit and push the code to GitHub using Antigravity's Git interface or terminal:
   ```sh
   git add android-sms-gateway .github/workflows/build-sms-gateway.yml
   git commit -m "ci: build Android SMS gateway APK with GitHub Actions"
   git push
   ```
4. On GitHub open your repository > **Actions** > **Build RescuAR SMS Gateway APK** > **Run workflow**. The workflow will also trigger when its files or `android-sms-gateway/` change.
5. Open a successful run and download `RescuAR-SMS-Gateway-debug-APK` from **Artifacts**. Extract the downloaded artifact ZIP to get `app-debug.apk`.
6. Install the debug APK on your *dedicated* Android phone with an active SIM. Allow installation from your chosen file manager if prompted. Review permissions and follow `SMS_GATEWAY_SETUP.md` from the earlier patch for Supabase gateway configuration. Do not share the APK or device secret publicly.

## Notes

- You do **not** need Android Studio, local Java/Gradle, or an Android SDK on your laptop.
- GitHub Actions needs permission to run in the repository, and the build needs internet access.
- This generates a **debug** APK for testing only. A signed production release, secure secrets storage, device management, delivery testing, and compliant SMS sending require further work.
- Workflow has not been run on your GitHub repository. Build or runtime errors may still need correction.
- If workflow does not appear, check repository Actions settings and confirm it exists on the default branch.
- Do not commit `local.properties`, `.env`, API keys, Supabase service keys, device gateway secrets or keystore files.
