namespace ParityEncoderLib;

/// <summary>
/// Encodes integer arrays into a parity checksum. An element is odd exactly
/// when element % 2 != 0 under truncating remainder semantics (negative odd
/// numbers count as odd). Supported domain: arrays with fewer than 65536
/// elements.
/// </summary>
public static class ParityEncoder
{
    public static int Encode(int[] values)
    {
        int odd = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] % 2 != 0)
            {
                odd++;
            }
        }
        return odd * 65536 + values.Length;
    }
}
