using System;

namespace RescuAR.AR;

/// <summary>Maps raw semantic texture pixels into the unzoomed camera viewport.</summary>
public static class SemanticImageCoordinates
{
    public readonly record struct Raster(byte[] Labels, byte[] Confidence, int Width, int Height);

    // UV corners come from ARCore ViewNormalized -> TextureNormalized, in
    // TL, TR, BL, BR order, exactly as for the camera/semantic reference renderer.
    public static bool TryReproject(byte[] labels, byte[] confidence, int width, int height,
        int labelStride, int confidenceStride, int viewWidth, int viewHeight,
        ReadOnlySpan<float> uv, out Raster raster)
    {
        raster = default;
        if (width <= 4 || height <= 4 || width > 2048 || height > 2048 ||
            viewWidth <= 0 || viewHeight <= 0 || labelStride < width || confidenceStride < width ||
            (long)(height - 1) * labelStride + width > labels.Length ||
            (long)(height - 1) * confidenceStride + width > confidence.Length || uv.Length != 8) return false;
        foreach (float value in uv) if (!float.IsFinite(value)) return false;
        const int outputHeight = 256;
        int outputWidth = (int)Math.Round(outputHeight * (double)viewWidth / viewHeight);
        if (outputWidth is < 8 or > 1024) return false;
        byte[] mappedLabels = new byte[outputWidth * outputHeight];
        byte[] mappedConfidence = new byte[mappedLabels.Length];
        for (int y = 0; y < outputHeight; y++)
        for (int x = 0; x < outputWidth; x++)
        {
            float u = (x + .5f) / outputWidth, v = (y + .5f) / outputHeight;
            float tx = (uv[0] * (1 - u) + uv[2] * u) * (1 - v) +
                (uv[4] * (1 - u) + uv[6] * u) * v;
            float ty = (uv[1] * (1 - u) + uv[3] * u) * (1 - v) +
                (uv[5] * (1 - u) + uv[7] * u) * v;
            if (tx < 0 || tx >= 1 || ty < 0 || ty >= 1) continue;
            int sx = (int)(tx * width), sy = (int)(ty * height), target = y * outputWidth + x;
            mappedLabels[target] = labels[sy * labelStride + sx];
            mappedConfidence[target] = confidence[sy * confidenceStride + sx];
        }
        raster = new(mappedLabels, mappedConfidence, outputWidth, outputHeight);
        return true;
    }
}
