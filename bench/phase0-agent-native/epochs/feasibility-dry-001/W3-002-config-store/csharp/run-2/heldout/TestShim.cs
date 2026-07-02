// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace ConfigStore.HeldOut;

internal static class TestShim
{
    public static string Get(string path, string key) => ConfigStoreLib.ConfigStore.Get(path, key);
    public static void Set(string path, string key, string value) => ConfigStoreLib.ConfigStore.Set(path, key, value);
    public static bool Has(string path, string key) => ConfigStoreLib.ConfigStore.Has(path, key);
}
