using System;
using System.Collections.Generic;

namespace RescuAR.AR;

/// <summary>Separates visible surface components before estimating any road.
/// Occluding vehicles and unrelated parallel surfaces must not be averaged.</summary>
public static class SemanticRoadBoundaries
{
    public readonly record struct Pixel(int X, int Y, bool Cropped);

    public static IReadOnlyList<IReadOnlyList<Pixel>> Extract(byte[] labels, byte[] confidence,
        int width, int height, int labelStride, int confidenceStride, byte selected)
    {
        var result = new List<IReadOnlyList<Pixel>>();
        if (width < 4 || height < 4 || width > 1024 || height > 2048 || labelStride < width ||
            confidenceStride < width || (long)(height - 1) * labelStride + width > labels.Length ||
            (long)(height - 1) * confidenceStride + width > confidence.Length) return result;
        int columns = (width + 1) / 2, rows = (height + 1) / 2;
        var visited = new bool[columns * rows];
        var queue = new Queue<int>();
        bool Surface(int x, int y) => labels[y * labelStride + x] == selected &&
            confidence[y * confidenceStride + x] >= 230;
        bool Edge(int x, int y) => confidence[y * confidenceStride + x] >= 204 &&
            labels[y * labelStride + x] != selected && labels[y * labelStride + x] is 2 or 4 or 5 or 6 or 7;
        for (int seed = 0; seed < visited.Length; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            if (!Surface(seed % columns * 2, seed / columns * 2)) continue;
            var boundary = new List<Pixel>();
            queue.Enqueue(seed);
            while (queue.TryDequeue(out int pixel))
            {
                int gx = pixel % columns, gy = pixel / columns;
                int x = gx * 2, y = gy * 2;
                bool cropped = x <= 2 || x >= width - 3 || y <= 2 || y >= height - 3;
                // The sampling grid is two pixels wide; a one-pixel lookup
                // can miss every edge on an odd image column.
                if (cropped || Edge(x - 2, y) || Edge(x + 2, y) || Edge(x, y - 2) || Edge(x, y + 2))
                    boundary.Add(new(x, y, cropped));
                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) return;
                    int index = ny * columns + nx;
                    if (visited[index]) return;
                    visited[index] = true;
                    if (Surface(nx * 2, ny * 2)) queue.Enqueue(index);
                }
                Visit(gx - 1, gy); Visit(gx + 1, gy); Visit(gx, gy - 1); Visit(gx, gy + 1);
            }
            if (boundary.Count >= 16) result.Add(boundary);
        }
        return result;
    }
}
