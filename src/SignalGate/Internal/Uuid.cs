using System;
using System.Security.Cryptography;

namespace SignalGate.Internal;

// Random version 4 UUIDs, formatted as lowercase 8-4-4-4-12 hex.
internal static class Uuid
{
    private const string HexDigits = "0123456789abcdef";

    internal static string NewV4()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        Span<char> chars = stackalloc char[36];
        int position = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i is 4 or 6 or 8 or 10)
            {
                chars[position++] = '-';
            }

            chars[position++] = HexDigits[bytes[i] >> 4];
            chars[position++] = HexDigits[bytes[i] & 0x0F];
        }

        return new string(chars);
    }
}
