package org.rescuar.smsgateway

import android.app.*
import android.content.*
import android.os.*
import android.telephony.SmsManager
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class GatewayService : Service() {
  private val worker=Executors.newSingleThreadExecutor()
  @Volatile private var running=false
  private val handler=Handler(Looper.getMainLooper())
  private val channel="rescuar_sms_gateway"
  override fun onBind(intent:Intent?) = null
  override fun onCreate(){ super.onCreate()
    getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel(channel,"RescuAR Gateway",NotificationManager.IMPORTANCE_LOW))
    val notification=Notification.Builder(this,channel).setContentTitle("RescuAR SMS Gateway")
      .setContentText("Gateway active — polling queued SMS messages").setSmallIcon(android.R.drawable.ic_dialog_email).build()
    startForeground(100,notification)
  }
  override fun onStartCommand(intent:Intent?,flags:Int,startId:Int):Int {
    if(!running){running=true;worker.execute { loop() }}
    return START_NOT_STICKY
  }
  private fun api(action:String,values:JSONObject):JSONObject {
    val store=getSharedPreferences("gateway",MODE_PRIVATE)
    val connection=(URL(store.getString("url","")!!).openConnection() as HttpURLConnection).apply {
      requestMethod="POST";connectTimeout=12000;readTimeout=12000;doOutput=true
      setRequestProperty("Content-Type","application/json");setRequestProperty("X-Gateway-Key",store.getString("key",""))
    }
    values.put("action",action).put("device_id",store.getString("device",""))
    try {connection.outputStream.use { it.write(values.toString().toByteArray(Charsets.UTF_8)) }
      val stream=if(connection.responseCode in 200..299)connection.inputStream else connection.errorStream
      val text=stream.bufferedReader().use{it.readText()}
      if(connection.responseCode !in 200..299)throw IllegalStateException("Server ${connection.responseCode}: $text")
      return JSONObject(text)
    } finally {connection.disconnect()}
  }
  private fun loop(){ while(running){
    try {
      val job=api("claim",JSONObject()).optJSONObject("job")
      if(job!=null) sendSms(job) else Thread.sleep(5000)
    }catch(e:Exception){android.util.Log.e("RescuARSms","Gateway polling failure",e);Thread.sleep(15000)}
  }}
  private fun sendSms(job:JSONObject){
    val id=job.getString("id"); val token=job.getString("lease_token")
    val recipient=job.getString("recipient");val body=job.getString("message")
    val action="org.rescuar.smsgateway.SENT.${UUID.randomUUID()}"
    val latch=CountDownLatch(1)
    var result="unknown";var detail="No sent callback before timeout"
    val receiver=object:BroadcastReceiver(){ override fun onReceive(c:Context?,i:Intent?) {
      if(resultCode==Activity.RESULT_OK){result="sent";detail=""}
      else {result="failed";detail="Android SMS result code $resultCode"}
      latch.countDown()
    }}
    try {
      @Suppress("DEPRECATION") val manager=SmsManager.getDefault()
      val parts=manager.divideMessage(body)
      if(parts.size!=1) {result="failed";detail="Message exceeds one SMS segment; shorten text before sending"}
      else {
        handler.post {
          try {
            if(Build.VERSION.SDK_INT>=33)registerReceiver(receiver,IntentFilter(action),RECEIVER_NOT_EXPORTED)
            else @Suppress("DEPRECATION") registerReceiver(receiver,IntentFilter(action))
            val sent=PendingIntent.getBroadcast(this,0,Intent(action).setPackage(packageName),PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            manager.sendTextMessage(recipient,null,body,sent,null)
          }catch(e:Exception){result="failed";detail=e.message?:"SMS submission failure";latch.countDown()}
        }
        latch.await(45,TimeUnit.SECONDS)
      }
    } catch(e:Exception){result="failed";detail=e.message?:"SMS error"}
    finally {handler.post {try{unregisterReceiver(receiver)}catch(_:Exception){} }}
    try {api("report",JSONObject().put("id",id).put("lease_token",token).put("status",result).put("error",detail))}
    catch(e:Exception){android.util.Log.e("RescuARSms","Report failed; leave claimed job for manual reconciliation",e)}
  }
  override fun onDestroy(){running=false;worker.shutdownNow();super.onDestroy()}
}
