using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RescuAR.App.Services.Cloud
{
    public class CloudinaryService
    {
        public const string CloudName = "dz15gvgpl";
        public const string UploadPreset = "rescuar_communityreports";

        public static async Task<string?> UploadImageStreamAsync(Stream stream, string fileName)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                if (imageBytes.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Cloudinary upload warning: image stream was 0 bytes.");
                    return null;
                }

                var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
                var mimeType = ext switch
                {
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };

                var base64String = Convert.ToBase64String(imageBytes);
                var dataUri = $"data:{mimeType};base64,{base64String}";

                using var client = new HttpClient();
                var payload = new
                {
                    file = dataUri,
                    upload_preset = UploadPreset
                };

                var jsonPayload = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var uploadUrl = $"https://api.cloudinary.com/v1_1/{CloudName}/image/upload";
                var response = await client.PostAsync(uploadUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var jsonObj = JObject.Parse(jsonString);
                    var secureUrl = jsonObj["secure_url"]?.ToString() ?? jsonObj["url"]?.ToString();
                    System.Diagnostics.Debug.WriteLine($"Cloudinary Upload Success: {secureUrl}");
                    return secureUrl;
                }
                else
                {
                    var errorResp = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Cloudinary Upload HTTP Error {response.StatusCode}: {errorResp}");
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        if (Shell.Current != null)
                        {
                            await Shell.Current.DisplayAlert("Cloudinary Upload Error", $"HTTP {response.StatusCode}: {errorResp}", "OK");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloudinary Exception: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlert("Cloudinary Exception", ex.Message, "OK");
                    }
                });
            }

            return null;
        }

        public static async Task<string?> UploadImageAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    using var stream = File.OpenRead(filePath);
                    return await UploadImageStreamAsync(stream, Path.GetFileName(filePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloudinary File Upload Exception: {ex.Message}");
            }

            return null;
        }
    }
}
