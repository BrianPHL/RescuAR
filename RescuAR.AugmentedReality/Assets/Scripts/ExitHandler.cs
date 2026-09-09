using UnityEngine;

public class ExitUnity : MonoBehaviour
{
    public void Exit()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("finish");
        }
#else
        Application.Quit();
#endif
    }
}
