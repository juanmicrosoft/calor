// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace ParityEncoder.HeldOut;

internal static class TestShim
{
    public static int Encode(int[] values) => ParityEncoderLib.ParityEncoder.Encode(values);
    public static bool IsValidLength(int[] values, int expectedLength) => ParityEncoderLib.ParityEncoder.IsValidLength(values, expectedLength);
    public static int CountEven(int[] values) => ParityEncoderLib.ParityEncoder.CountEven(values);
    public static int IndexOfFirstOdd(int[] values) => ParityEncoderLib.ParityEncoder.IndexOfFirstOdd(values);
}
