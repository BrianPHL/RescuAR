using TMPro;
using UnityEngine;

public class EvacuationCenterLoader : MonoBehaviour
{
    [SerializeField]
    private TMP_Text infoText;

    private void Start()
    {
        string json =
            IntentReceiver.GetEvacuationCenterJson();

        if (string.IsNullOrEmpty(json))
        {
            infoText.text =
                "No evacuation center received.";

            return;
        }

        EvacuationCenter center =
            JsonUtility.FromJson<EvacuationCenter>(
                json);

        infoText.text =
            $"Name: {center.Name}\n" +
            $"Latitude: {center.Latitude}\n" +
            $"Longitude: {center.Longitude}\n" +
            $"Capacity: {center.Capacity}\n" +
            $"Occupancy: {center.Occupancy}\n" +
            $"Status: {center.Status}";
    }
}
