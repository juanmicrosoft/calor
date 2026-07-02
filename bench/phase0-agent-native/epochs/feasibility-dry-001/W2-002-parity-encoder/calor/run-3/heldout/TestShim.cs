// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace ParityEncoder.HeldOut;

internal static class TestShim
{
    public static int Encode(int[] values) => global::ParityEncoder.ParityEncoderModule.Encode(values);
    public static bool IsValidLength(int[] values, int expectedLength) => global::ParityEncoder.ParityEncoderModule.IsValidLength(values, expectedLength);
    public static int CountEven(int[] values) => global::ParityEncoder.ParityEncoderModule.CountEven(values);
    public static int IndexOfFirstOdd(int[] values) => global::ParityEncoder.ParityEncoderModule.IndexOfFirstOdd(values);
}
