using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Calor.Soundness.Sweep;

// Contract amendment 1.2.0, s1-generated-cases exception condition 3 (run 2 only): the sweep process enumerates its OWN loaded images (dyld image
// list on macOS, /proc/self/maps on Linux), takes the libz3 image mapped under each baseline's directory, and hashes that file. Called before case 1
// and after the last case. A missing image or a hash other than the registered value invalidates the run.
internal static class LoadedImageCapture
{
    // registration.json baselines: the osx-arm64 libz3.dylib registered for Z3 4.15.7 (B1 bootstrap and the N1 0.21.0 package).
    private const string RegisteredLibz3Sha256 = "8d1f54380a32e2c13b03b2e9afa8427908fa480c3f7d1285a1ae034793359ada";

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint _dyld_image_count();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr _dyld_get_image_name(uint index);

    private static (string Method, List<string> Paths) LoadedImages() => OperatingSystem.IsMacOS()
        ? ("dyld image list (_dyld_image_count/_dyld_get_image_name, in-process)",
            Enumerable.Range(0, (int)_dyld_image_count()).Select(i => Marshal.PtrToStringUTF8(_dyld_get_image_name((uint)i))).OfType<string>().ToList())
        : ("/proc/self/maps (in-process)",
            File.ReadLines("/proc/self/maps").Select(l => l.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length == 6).Select(p => p[5].Trim()).ToList());

    // Appends one record per baseline to <out>/loaded-image-capture.jsonl; returns the reasons the capture fails (empty when it holds).
    public static List<string> Capture(string outRoot, string phase, IEnumerable<BaselineHost> hosts)
    {
        var (method, paths) = LoadedImages();
        var libz3 = paths.Where(p => Path.GetFileName(p).StartsWith("libz3", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToList();
        var failures = new List<string>();
        foreach (var host in hosts)
        {
            var mapped = libz3.Where(p => p.StartsWith(host.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
            var hashed = mapped.Select(p => (Path: p, Sha256: File.Exists(p) ? Hashing.Sha256File(p) : null)).ToList();
            var ok = hashed.Count == 1 && hashed[0].Sha256 == RegisteredLibz3Sha256;
            if (!ok) failures.Add($"{host.Id} {phase}: {hashed.Count} libz3 image(s) mapped under {host.Directory}" + string.Concat(hashed.Select(h => $" [{h.Path} {h.Sha256 ?? "missing file"}]")));
            File.AppendAllText(Path.Combine(outRoot, "loaded-image-capture.jsonl"), new JsonObject
            {
                ["baseline"] = host.Id, ["phase"] = phase, ["utc"] = DateTime.UtcNow.ToString("O"), ["pid"] = Environment.ProcessId, ["method"] = method,
                ["path"] = hashed.Count == 1 ? hashed[0].Path : null, ["sha256"] = hashed.Count == 1 ? hashed[0].Sha256 : null,
                ["registeredSha256"] = RegisteredLibz3Sha256, ["ok"] = ok,
                ["mappedUnderBaseline"] = new JsonArray(hashed.Select(h => (JsonNode)new JsonObject { ["path"] = h.Path, ["sha256"] = h.Sha256 }).ToArray()),
                ["allLibz3ImagesInProcess"] = new JsonArray(libz3.Select(p => (JsonNode)p).ToArray()),
            }.ToJsonString() + "\n");
        }
        return failures;
    }
}
