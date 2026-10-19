plugins { id("com.android.application"); id("org.jetbrains.kotlin.android") }
android { namespace = "org.rescuar.smsgateway"; compileSdk = 35
  defaultConfig { applicationId = "org.rescuar.smsgateway"; minSdk = 26; targetSdk = 35; versionCode = 1; versionName = "1.0" }
}
