using RescuAR.AR;

internal static class ARDepthImageDecoderChecks
{
    public static void Run(Action<bool, string> check)
    {
        byte[] packed = [0x00, 0x00, 0xB3, 0x00, 0xB4, 0x00, 0x40, 0x1F,
            0x41, 0x1F, 0xFF, 0xFF];
        ushort[] decoded = new ushort[6];
        int valid = ARDepthImageDecoder.Decode(packed, 3, 2, 6, 2, decoded);
        check(decoded.SequenceEqual(new ushort[] { 0, 179, 180, 8000, 8001, 65535 }),
            "D_16 decoding preserves unsigned little-endian values across both rows");
        check(valid == 2, "depth coverage includes the existing 180 mm and 8000 mm boundaries");

        // Two 2-pixel rows with a 4-byte pixel stride, 10-byte row stride,
        // and no trailing padding beyond the final depth sample.
        byte[] padded = [0xE8, 0x03, 0xFF, 0xFF, 0xD0, 0x07, 0xFF, 0xFF, 0xFF, 0xFF,
            0xB8, 0x0B, 0xFF, 0xFF, 0xA0, 0x0F];
        ushort[] paddedResult = new ushort[5];
        paddedResult[4] = 123;
        check(ARDepthImageDecoder.GetRequiredByteCount(2, 2, 10, 4) == padded.Length,
            "the depth bulk copy excludes optional final-row padding");
        check(ARDepthImageDecoder.Decode(padded, 2, 2, 10, 4, paddedResult) == 4 &&
            paddedResult.SequenceEqual(new ushort[] { 1000, 2000, 3000, 4000, 123 }),
            "depth decoding ignores row and pixel padding and leaves extra destination space untouched");

        byte[] single = [0x34, 0x12];
        ushort[] singleResult = new ushort[1];
        check(ARDepthImageDecoder.GetRequiredByteCount(1, 1, 128, 4) == 2 &&
            ARDepthImageDecoder.Decode(single, 1, 1, 128, 4, singleResult) == 1 &&
            singleResult[0] == 0x1234,
            "a one-pixel depth image needs no row padding in its buffer");

        ushort[] unchanged = [42, 42, 42, 42];
        check(Throws<ArgumentException>(() => ARDepthImageDecoder.Decode(
            padded.AsSpan(0, padded.Length - 1), 2, 2, 10, 4, unchanged)) &&
            unchanged.All(value => value == 42),
            "a truncated depth plane is rejected before writing partial output");
        check(Throws<ArgumentException>(() => ARDepthImageDecoder.Decode(
            padded, 2, 2, 10, 4, new ushort[3])),
            "a short depth destination is rejected before decoding");
        check(Throws<ArgumentOutOfRangeException>(() =>
            ARDepthImageDecoder.GetRequiredByteCount(2, 2, 3, 2)) &&
            Throws<ArgumentOutOfRangeException>(() =>
                ARDepthImageDecoder.GetRequiredByteCount(2, 2, 4, 1)),
            "overlapping depth rows and undersized pixel strides are rejected");
        check(Throws<ArgumentOutOfRangeException>(() =>
            ARDepthImageDecoder.GetRequiredByteCount(0, 1, 2, 2)) &&
            Throws<ArgumentOutOfRangeException>(() =>
                ARDepthImageDecoder.GetRequiredByteCount(1, -1, 2, 2)),
            "empty and negative depth dimensions are rejected");
        check(Throws<ArgumentOutOfRangeException>(() =>
            ARDepthImageDecoder.GetRequiredByteCount(int.MaxValue, 2, int.MaxValue, 2)) &&
            Throws<ArgumentOutOfRangeException>(() =>
                ARDepthImageDecoder.GetRequiredByteCount(1, int.MaxValue, int.MaxValue, 2)),
            "depth byte-count arithmetic rejects oversized layouts without integer wraparound");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }
}
