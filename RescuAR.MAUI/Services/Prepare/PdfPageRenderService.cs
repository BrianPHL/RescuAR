using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

#if ANDROID
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
#endif

namespace RescuAR.App.Services.Prepare
{
    public static class PdfPageRenderService
    {
        private static List<byte[]>? _cachedPageBytes;

        public static async Task<List<ImageSource>> RenderPdfPagesAsync(string pdfPath)
        {
            var result = new List<ImageSource>();

            // Return cached rendered pages immediately if available
            if (_cachedPageBytes != null && _cachedPageBytes.Count > 0)
            {
                foreach (var bytes in _cachedPageBytes)
                {
                    var copy = bytes;
                    result.Add(ImageSource.FromStream(() => new MemoryStream(copy)));
                }
                return result;
            }

#if ANDROID
            try
            {
                await Task.Run(async () =>
                {
                    var file = new Java.IO.File(pdfPath);
                    if (!file.Exists()) return;

                    using var fileDescriptor = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
                    if (fileDescriptor != null)
                    {
                        using var renderer = new PdfRenderer(fileDescriptor);
                        int pageCount = renderer.PageCount;
                        var cache = new List<byte[]>();

                        for (int i = 0; i < pageCount; i++)
                        {
                            using var page = renderer.OpenPage(i);
                            
                            // Render page at 2.5x scale for sharp text readability + ultra-fast hardware-accelerated processing
                            int width = Math.Max(1, (int)(page.Width * 2.5));
                            int height = Math.Max(1, (int)(page.Height * 2.5));

                            using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
                            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

                            using var memoryStream = new MemoryStream();
                            // JPEG 95% quality provides high text crispness with 15x faster compression than PNG
                            await bitmap.CompressAsync(Bitmap.CompressFormat.Jpeg!, 95, memoryStream);
                            var bytes = memoryStream.ToArray();

                            cache.Add(bytes);
                            result.Add(ImageSource.FromStream(() => new MemoryStream(bytes)));
                        }

                        _cachedPageBytes = cache;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PdfRenderer native error: {ex.Message}");
            }
#endif

            return result;
        }
    }
}
