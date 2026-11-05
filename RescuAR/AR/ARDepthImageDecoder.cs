using System;

namespace RescuAR.AR;

/// <summary>Decodes an Android D_16 plane without per-pixel Java calls.</summary>
public static class ARDepthImageDecoder
{
    public static int GetRequiredByteCount(int width, int height, int rowStride, int pixelStride)
    {
        if (width <= 0 || height <= 0 || pixelStride < sizeof(ushort))
            throw new ArgumentOutOfRangeException(nameof(width), "Depth dimensions and pixel stride must be valid.");

        long rowBytes = (long)(width - 1) * pixelStride + sizeof(ushort);
        long requiredBytes = (long)(height - 1) * rowStride + rowBytes;
        if (rowStride < rowBytes || requiredBytes > int.MaxValue || (long)width * height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(rowStride), "Depth strides or dimensions exceed the supported plane size.");

        // Android may omit padding after the last row.
        return (int)requiredBytes;
    }

    public static int Decode(ReadOnlySpan<byte> source, int width, int height,
        int rowStride, int pixelStride, Span<ushort> destination)
    {
        int requiredBytes = GetRequiredByteCount(width, height, rowStride, pixelStride);
        if (source.Length < requiredBytes)
            throw new ArgumentException("Depth plane is truncated.", nameof(source));
        if (destination.Length < width * height)
            throw new ArgumentException("Depth destination is too small.", nameof(destination));

        int validDepthPixelCount = 0;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowStride;
            int destinationRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int byteIndex = rowOffset + x * pixelStride;
                ushort depth = (ushort)(source[byteIndex] | (source[byteIndex + 1] << 8));
                destination[destinationRow + x] = depth;
                if (depth is >= 180 and <= 8_000)
                    validDepthPixelCount++;
            }
        }

        return validDepthPixelCount;
    }
}
