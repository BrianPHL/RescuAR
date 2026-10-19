package org.rescuar.smsgateway

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.*

class MainActivity : Activity() {
  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    val store=getSharedPreferences("gateway", MODE_PRIVATE)
    val root=LinearLayout(this).apply { orientation=LinearLayout.VERTICAL; setPadding(35,45,35,30) }
    val url=EditText(this).apply { hint="Supabase function URL (https://…/functions/v1/sms-gateway)"; setSingleLine();setText(store.getString("url","")) }
    val key=EditText(this).apply { hint="SMS_GATEWAY_KEY (device secret)";setSingleLine();inputType=129;setText(store.getString("key","")) }
    val device=EditText(this).apply { hint="Device ID";setSingleLine();setText(store.getString("device","rescuar-gateway-01")) }
    val status=TextView(this).apply { text="Keep the app running during gateway use. A dedicated SIM and stable cellular signal are required." }
    val start=Button(this).apply { text="Start SMS gateway" }
    val stop=Button(this).apply { text="Stop gateway" }
    listOf(url,key,device,status,start,stop).forEach { root.addView(it) }; setContentView(root)
    start.setOnClickListener {
      if(checkSelfPermission(Manifest.permission.SEND_SMS)!=PackageManager.PERMISSION_GRANTED){ requestPermissions(arrayOf(Manifest.permission.SEND_SMS),8);status.text="Grant SMS permission, then press Start again.";return@setOnClickListener }
      if(Build.VERSION.SDK_INT>=33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS),9)
      val u=url.text.toString().trim();val k=key.text.toString().trim();val d=device.text.toString().trim()
      if(!u.startsWith("https://")||k.length<24||!d.matches(Regex("[a-zA-Z0-9_-]{3,64}"))){status.text="Enter a valid HTTPS URL, secret (24+ characters) and device ID.";return@setOnClickListener}
      store.edit().putString("url",u).putString("key",k).putString("device",d).apply()
      try { startForegroundService(Intent(this,GatewayService::class.java)); status.text="Started; inspect the persistent notification." }
      catch(e:Exception){status.text=e.message}
    }
    stop.setOnClickListener { stopService(Intent(this,GatewayService::class.java)); status.text="Gateway stopped" }
  }
}
