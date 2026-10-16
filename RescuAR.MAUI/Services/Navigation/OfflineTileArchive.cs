using System.IO.Compression;
using System.Text.Json;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Reads an indexed PNG tile package directly, with no network or tile extraction.</summary>
public sealed class OfflineTileArchive : IDisposable
{
    private readonly ZipArchive archive;
    private readonly object sync = new();
    public OfflineMapManifest Manifest { get; }

    public OfflineTileArchive(Stream stream)
    {
        archive = new ZipArchive(stream, ZipArchiveMode.Read);
        try
        {
            var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing offline map manifest.");
            if (entry.Length > 16_384) throw new InvalidDataException("Invalid offline map manifest.");
            using var metadata = entry.Open();
            Manifest = JsonSerializer.Deserialize<OfflineMapManifest>(metadata) ?? throw new InvalidDataException("Invalid offline map manifest.");
            if (Manifest.Version != 1 || Manifest.Format != "png" || Manifest.Scheme != "xyz" ||
                Manifest.MinZoom < 0 || Manifest.MaxZoom > 22 || Manifest.MinZoom > Manifest.MaxZoom || Manifest.TileCount < 1)
                throw new InvalidDataException("Unsupported offline map format.");
        }
        catch { archive.Dispose(); throw; }
    }

    public byte[]? ReadTile(int zoom, int column, int row)
    {
        if (zoom < Manifest.MinZoom || zoom > Manifest.MaxZoom || column < 0 || row < 0 ||
            column >= (1 << zoom) || row >= (1 << zoom)) return null;
        lock (sync)
        {
            var entry = archive.GetEntry($"tiles/{zoom}/{column}/{row}.png");
            if (entry is null) return null; // Outside coverage: no fabricated tile or remote fallback.
            if (entry.Length is < 8 or > 2_097_152) throw new InvalidDataException("Invalid offline tile size.");
            using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            input.CopyTo(output);
            var bytes = output.ToArray();
            if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new InvalidDataException("Offline tile is not PNG.");
            return bytes;
        }
    }

    public void Dispose() { lock (sync) archive.Dispose(); }
}

public sealed record OfflineMapManifest(int Version, string Format, string Scheme, int MinZoom,
    int MaxZoom, int TileCount, double[] Bounds, string Source);
