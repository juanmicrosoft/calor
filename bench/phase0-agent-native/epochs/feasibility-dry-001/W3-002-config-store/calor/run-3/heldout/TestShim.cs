// Calor-arm shim (harness-provided, fixed, not agent-editable).
// Calor module M emits namespace M / static class MModule.
namespace ConfigStore.HeldOut;

internal static class TestShim
{
    public static string Get(string path, string key) => global::ConfigStore.ConfigStoreModule.Get(path, key);
    public static void Set(string path, string key, string value) => global::ConfigStore.ConfigStoreModule.Set(path, key, value);
    public static bool Has(string path, string key) => global::ConfigStore.ConfigStoreModule.Has(path, key);
}
