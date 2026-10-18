using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace EdgeTTS.Network;

internal static class SecMSGECHash
{
    private const uint SHA256_ADJUSTMENT        = 0xC9DC06A7;
    private const uint SHA256_SIGMA0_ADJUSTMENT = 0x059C0B80;
    private const uint SHA256_SIGMA1_ADJUSTMENT = 0x000B92C0;
    private const uint CS64_KEY_FACTOR          = 0x82893933;
    private const uint CS64_TERM_ADJUSTMENT     = 0xF2F90B1A;
    private const uint CS64_OFFSET_ADJUSTMENT   = 0x0B1A0000;
    private const uint CS64_ROTATE_FACTOR_EVEN  = 0xEF0569FB;
    private const uint CS64_ROTATE_FACTOR_ODD   = 0xC31713DB;

    private static readonly uint[] SHA256_INITIAL =
    [
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
    ];

    private static readonly uint[] SHA256_STATE =
    [
        0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A, 0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2
    ];

    private static readonly uint[] SHA256_SCHEDULE =
    [
        0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA, 0xA622EE8C, 0xD1DD9491, 0xC0DD25A0, 0xF9E70AB6,
        0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33, 0xD258B060, 0x72766564
    ];

    private static readonly uint[] SHA256_ROUND_CONSTANTS =
    [
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
    ];

    private static readonly uint[] SHA256_BLOCK_CONSTANTS = CreateBlockConstants();

    private static readonly (uint Coefficient, uint Offset)[] CS64_COEFFICIENTS =
    [
        (0xE82E91B1, 0xB32700D0),
        (0x44DE24C6, 0x88DFF51D),
        (0x3BEB65BC, 0x29514038),
        (0xC89C5933, 0xB328CBDF),
        (0x8D1EC211, 0xD405B4D5),
        (0x314EF5D5, 0x26E55F3C),
        (0xE92491B1, 0x607801C6),
        (0xF0A5B9D5, 0x82B2C1FB),
        (0x06064CF3, 0xFA51A3DA),
        (0xDA79FA60, 0x207CC464),
        (0xBED20560, 0x3C3C74F3),
        (0x89F9C9F8, 0xD2D14B61),
        (0x35FA38C6, 0xBB04A2C1),
        (0xCB49E3C6, 0xC5626167)
    ];

    public static string Compute
    (
        string ticks
    )
    {
        var data   = Encoding.ASCII.GetBytes(ticks);
        var digest = ComputeDigest(data);
        var (swap, acc) = ComputeCS64(data, digest);

        Span<byte> cs64 = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(cs64,      swap);
        BinaryPrimitives.WriteUInt32LittleEndian(cs64[4..], acc);

        return $"{Convert.ToHexString(cs64)}-{Convert.ToHexString(digest)}";
    }

    private static uint[] CreateBlockConstants()
    {
        var constants = (uint[])SHA256_ROUND_CONSTANTS.Clone();
        constants[14] = SHA256_ADJUSTMENT;
        return constants;
    }

    private static byte[] ComputeDigest
    (
        ReadOnlySpan<byte> data
    )
    {
        var messageBitLength = 57 + data.Length;
        var remainder        = messageBitLength      % 64;
        var zeroPadding      = (55 - remainder + 64) % 64;
        var blockLength      = data.Length - 7 + 1 + zeroPadding + 8;

        var padded = new byte[blockLength];
        data[7..].CopyTo(padded);
        padded[data.Length - 7] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(blockLength - 8), (ulong)(messageBitLength * 8));

        var state = CreateInitialState(data[..7]);
        var words = new uint[64];

        for (var offset = 0; offset < blockLength; offset += 64)
        {
            for (var index = 0; index < 16; index++)
                words[index] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(offset + (index * 4)));

            ExpandSchedule(words, 16);

            var working = ComputeRounds(state, words, SHA256_ROUND_CONSTANTS, 0);
            for (var index = 0; index < 8; index++)
                state[index] = unchecked(state[index] + working[index]);
        }

        var digest = new byte[32];
        for (var index = 0; index < 8; index++)
            BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(index * 4), state[index]);

        return digest;
    }

    private static uint[] CreateInitialState
    (
        ReadOnlySpan<byte> prefix
    )
    {
        var words = new uint[64];
        words[14] = (uint)((prefix[0] << 16) | (prefix[1] << 8) | prefix[2]);
        words[15] = BinaryPrimitives.ReadUInt32BigEndian(prefix[3..]);

        for (var index = 16; index < 30; index++)
        {
            var sigma1 = SmallSigma1(words[index - 2]);
            if (index == 16)
                sigma1 ^= SHA256_SIGMA1_ADJUSTMENT;

            var value = unchecked(SHA256_SCHEDULE[index - 16] + sigma1);
            if (index >= 21)
                value = unchecked(value + words[index - 7]);
            if (index == 29)
                value = unchecked(value + (SmallSigma0(words[index - 15]) ^ SHA256_SIGMA0_ADJUSTMENT));

            words[index] = value;
        }

        words[30] = unchecked
        (
            SmallSigma1(words[28]) + words[23] + SmallSigma0(words[15]) + words[14] + SHA256_ADJUSTMENT - SHA256_ROUND_CONSTANTS[14]
        );

        ExpandSchedule(words, 31);

        var working = ComputeRounds(SHA256_STATE, words, SHA256_BLOCK_CONSTANTS, 14);
        var state   = new uint[8];

        for (var index = 0; index < 8; index++)
            state[index] = unchecked(SHA256_INITIAL[index] + working[index]);

        return state;
    }

    private static uint[] ComputeRounds
    (
        uint[] state,
        uint[] words,
        uint[] constants,
        int    firstRound
    )
    {
        var a = state[0];
        var b = state[1];
        var c = state[2];
        var d = state[3];
        var e = state[4];
        var f = state[5];
        var g = state[6];
        var h = state[7];

        for (var index = firstRound; index < 64; index++)
        {
            var t1 = unchecked(h         + Sigma1(e) + (g ^ (e & (f ^ g))) + constants[index] + words[index]);
            var t2 = unchecked(Sigma0(a) + ((a                 & b) | (c & (a | b))));

            h = g;
            g = f;
            f = e;
            e = unchecked(d + t1);
            d = c;
            c = b;
            b = a;
            a = unchecked(t1 + t2);
        }

        return [a, b, c, d, e, f, g, h];
    }

    private static void ExpandSchedule
    (
        uint[] words,
        int    firstIndex
    )
    {
        for (var index = firstIndex; index < 64; index++)
            words[index] = unchecked
            (
                SmallSigma1(words[index - 2]) + words[index - 7] + SmallSigma0(words[index - 15]) + words[index - 16]
            );
    }

    private static (uint Swap, uint Acc) ComputeCS64
    (
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> digest
    )
    {
        var keyA = BinaryPrimitives.ReadUInt32LittleEndian(digest)      | 1;
        var keyB = BinaryPrimitives.ReadUInt32LittleEndian(digest[4..]) | 1;

        var keys          = new[] { keyA, keyB };
        var factors       = new[] { unchecked(keyA * CS64_KEY_FACTOR), unchecked(keyB * CS64_KEY_FACTOR) };
        var rotateFactors = new[] { CS64_ROTATE_FACTOR_EVEN, CS64_ROTATE_FACTOR_ODD };

        Span<byte> prefixed = stackalloc byte[data.Length + 1];
        data.CopyTo(prefixed[1..]);
        var words = ToEvenWords(prefixed);

        var terms   = new uint[CS64_COEFFICIENTS.Length + words.Length];
        var offsets = new uint[terms.Length];

        for (var index = 0; index < CS64_COEFFICIENTS.Length; index++)
        {
            var selector = index & 1;
            terms[index]   = unchecked(CS64_COEFFICIENTS[index].Coefficient * factors[selector]);
            offsets[index] = CS64_COEFFICIENTS[index].Offset;
        }

        for (var index = 0; index < words.Length; index++)
        {
            var selector = index & 1;
            var word     = words[index];
            var term     = unchecked(word                               * keys[selector]);
            var offset   = unchecked(BitOperations.RotateLeft(word, 16) * rotateFactors[selector]);

            if (index == 0)
            {
                term   = unchecked(term   + (CS64_TERM_ADJUSTMENT * factors[0]));
                offset = unchecked(offset + CS64_OFFSET_ADJUSTMENT);
            }

            terms[CS64_COEFFICIENTS.Length   + index] = term;
            offsets[CS64_COEFFICIENTS.Length + index] = offset;
        }

        uint swapAcc  = 0;
        uint swapLast = 0;
        uint state    = 0;
        uint acc      = 0;

        for (var index = 0; index < terms.Length; index += 2)
        {
            var x = terms[index];
            var y = terms[index + 1];

            var t = BitOperations.RotateLeft(unchecked(offsets[index] + x), 16);
            var q = unchecked(t                                            * 0x76F50F34);
            swapAcc = unchecked(swapAcc + (BitOperations.RotateLeft(q, 16) * 0xC3EFEA97) + q);

            t        = BitOperations.RotateLeft(unchecked(offsets[index + 1] + y), 16);
            q        = unchecked(t * 0x1881CE3C);
            swapLast = unchecked((BitOperations.RotateLeft(q, 16) * 0x35BD1EC9) + q);

            swapAcc = unchecked(swapAcc + swapLast);

            state = unchecked((state * keys[0]) + x);
            state = unchecked(BitOperations.RotateLeft(state, 16) * 0xCF98B111);
            state = unchecked(BitOperations.RotateLeft(state, 16) * 0x87085B9F);
            state = unchecked(BitOperations.RotateLeft(state, 16) * 0x12CEB96D);
            state = unchecked(BitOperations.RotateLeft(state, 16) * 0x257E1D83);
            acc   = unchecked(acc + state);

            t     = unchecked((state * keys[1]) + y);
            t     = unchecked(BitOperations.RotateLeft(t,                                                       16) * 0xA27416F5);
            t     = unchecked(BitOperations.RotateLeft(t,                                                       16) * 0xD38396FF);
            state = unchecked(BitOperations.RotateLeft(unchecked(BitOperations.RotateLeft(t, 16) * 0x7C932B89), 16) * 0xBFA49F69);
            acc   = unchecked(acc + state);
        }

        return (swapLast ^ state, swapAcc ^ acc);
    }

    private static uint[] ToEvenWords
    (
        ReadOnlySpan<byte> data
    )
    {
        var remainder    = data.Length % 4;
        var paddedLength = data.Length + ((4 - remainder) % 4);
        var buffer       = new byte[paddedLength];
        data.CopyTo(buffer);

        var wordCount = paddedLength / 4;
        var words     = new uint[wordCount + (wordCount & 1)];

        for (var index = 0; index < wordCount; index++)
            words[index] = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(index * 4));

        return words;
    }

    private static uint Sigma0
    (
        uint value
    ) =>
        BitOperations.RotateRight(value, 2) ^ BitOperations.RotateRight(value, 13) ^ BitOperations.RotateRight(value, 22);

    private static uint Sigma1
    (
        uint value
    ) =>
        BitOperations.RotateRight(value, 6) ^ BitOperations.RotateRight(value, 11) ^ BitOperations.RotateRight(value, 25);

    private static uint SmallSigma0
    (
        uint value
    ) =>
        BitOperations.RotateRight(value, 7) ^ BitOperations.RotateRight(value, 18) ^ (value >> 3);

    private static uint SmallSigma1
    (
        uint value
    ) =>
        BitOperations.RotateRight(value, 17) ^ BitOperations.RotateRight(value, 19) ^ (value >> 10);
}
