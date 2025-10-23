using System.Numerics;
using System.Text;

namespace EdgeTTS.Common;

internal static class Extension
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string ToBase36String(this byte[] toConvert, bool bigEndian = false)
    {
        if (bigEndian) Array.Reverse(toConvert);
        var dividend = new BigInteger(toConvert);
        var builder = new StringBuilder();
        while (dividend != 0)
        {
            dividend = BigInteger.DivRem(dividend, 36, out var remainder);
            builder.Insert(0, Alphabet[Math.Abs((int)remainder)]);
        }

        return builder.ToString();
    }
}
