namespace AltevaCrawler.Shared.Utilities;

using System;

/// <summary>
/// Provides time-ordered sequential GUID generation (UUIDv7 layout)
/// to prevent B-tree index fragmentation in relational databases.
/// </summary>
public static class SequentialGuid
{
    private static readonly object Lock = new();
    private static long _lastTimestampMs;
    private static ushort _sequence;

    /// <summary>
    /// Generates a new sequential (time-ordered) GUID.
    /// </summary>
    public static Guid NewSequentialGuid()
    {
        byte[] randomBytes = new byte[8];
        Random.Shared.NextBytes(randomBytes);

        long timestampMs;
        ushort sequence;

        // Timestamp and sequence are read inside the lock so concurrent callers never share a pair.
        // The 12-bit sequence is exhausted after 4095 ids in one millisecond; roll the clock forward then.
        lock (Lock)
        {
            timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timestampMs <= _lastTimestampMs)
            {
                timestampMs = _lastTimestampMs;
                _sequence++;
                if (_sequence > 0x0FFF)
                {
                    timestampMs = ++_lastTimestampMs;
                    _sequence = 0;
                }
            }
            else
            {
                _lastTimestampMs = timestampMs;
                _sequence = 0;
            }
            sequence = _sequence;
        }

        byte[] guidBytes = new byte[16];

        // 48-bit timestamp in big-endian (bytes 0..5)
        guidBytes[0] = (byte)(timestampMs >> 40);
        guidBytes[1] = (byte)(timestampMs >> 32);
        guidBytes[2] = (byte)(timestampMs >> 24);
        guidBytes[3] = (byte)(timestampMs >> 16);
        guidBytes[4] = (byte)(timestampMs >> 8);
        guidBytes[5] = (byte)timestampMs;

        // Version 7 (0x70) + 12-bit sequence
        guidBytes[6] = (byte)(0x70 | ((sequence >> 8) & 0x0F));
        guidBytes[7] = (byte)(sequence & 0xFF);

        // Variant (10xx xxxx)
        guidBytes[8] = (byte)((randomBytes[0] & 0x3F) | 0x80);
        guidBytes[9] = randomBytes[1];
        guidBytes[10] = randomBytes[2];
        guidBytes[11] = randomBytes[3];
        guidBytes[12] = randomBytes[4];
        guidBytes[13] = randomBytes[5];
        guidBytes[14] = randomBytes[6];
        guidBytes[15] = randomBytes[7];

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(guidBytes, 0, 4);
            Array.Reverse(guidBytes, 4, 2);
            Array.Reverse(guidBytes, 6, 2);
        }

        return new Guid(guidBytes);
    }
}
