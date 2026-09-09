using UnityEngine;

public static class IntentReceiver
{
    public static string GetEvacuationCenterJson()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        using (var unityPlayer =
            new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            var activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");

            var intent =
                activity.Call<AndroidJavaObject>(
                    "getIntent");

            return intent.Call<string>(
                "getStringExtra",
                "evacuation_center");
        }

#else

        return null;

#endif
    }
}
