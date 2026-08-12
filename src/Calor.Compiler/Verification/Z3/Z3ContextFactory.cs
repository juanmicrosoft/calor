using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Factory for creating Z3 contexts with graceful fallback when Z3 native library is unavailable.
/// </summary>
public static class Z3ContextFactory
{
    private static bool? _isAvailable;
    private static readonly object _lock = new();
    private static bool _resolverRegistered;
    private static IntPtr _z3NativeHandle;

    /// <summary>
    /// Registers a custom DLL import resolver and pre-loads the Z3 native library.
    /// Must be called before any Z3 types are accessed.
    /// </summary>
    private static void EnsureResolverRegistered()
    {
        if (_resolverRegistered)
            return;

        lock (_lock)
        {
            if (_resolverRegistered)
                return;

            // Pre-load the native library FIRST
            _z3NativeHandle = TryLoadZ3Native();

            // Bind Microsoft.Z3 imports only to the explicit, fingerprinted
            // probe paths. Throwing for an unavailable Z3 handle prevents the
            // runtime from silently falling back to an ambient system library.
            NativeLibrary.SetDllImportResolver(
                typeof(Microsoft.Z3.Context).Assembly,
                ResolveZ3DllImport);

            // Retain load-context handlers for hosts that raise resolution
            // events for transitive unmanaged imports.
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;

            // When this assembly is loaded by MSBuild (Calor.Sdk package path),
            // it lives in a non-default AssemblyLoadContext, and Microsoft.Z3's
            // DllImport failure resolution raises ResolvingUnmanagedDll on THAT
            // context — the Default-context handler above never fires there.
            var ownContext = AssemblyLoadContext.GetLoadContext(typeof(Z3ContextFactory).Assembly);
            if (ownContext != null && ownContext != AssemblyLoadContext.Default)
            {
                ownContext.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
            }

            _resolverRegistered = true;
        }
    }

    private static IntPtr ResolveZ3DllImport(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!libraryName.Contains("z3", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;
        if (_z3NativeHandle != IntPtr.Zero)
            return _z3NativeHandle;

        throw new DllNotFoundException(
            "Z3 was not found in Calor's explicit native-library probe paths.");
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        // Only handle libz3
        if (!libraryName.Contains("z3", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        // Return the pre-loaded handle if available
        if (_z3NativeHandle != IntPtr.Zero)
            return _z3NativeHandle;

        // Otherwise try to load it now
        return TryLoadZ3Native();
    }

    // IL3000: Assembly.Location is empty under single-file publish (the VS Code
    // extension packs the language server that way). That is a supported input
    // here, not a defect: AppContext.BaseDirectory is probe root #1 and covers
    // the single-file case, while Location is consulted only to reach the
    // MSBuild-task layout (natives next to calor.dll, where BaseDirectory is the
    // MSBuild host directory). The empty result is filtered by the
    // IsNullOrEmpty guard below, so the single-file app simply probes one root.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000",
        Justification = "Empty Location is expected under single-file and is guarded; " +
                        "AppContext.BaseDirectory is the primary probe root.")]
    private static IntPtr TryLoadZ3Native()
    {
        foreach (var path in GetNativeLibraryProbePaths())
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000",
        Justification = "Empty Location is expected under single-file and is guarded; " +
                        "AppContext.BaseDirectory is the primary probe root.")]
    internal static IReadOnlyList<string> GetNativeLibraryProbePaths()
    {
        // Probe roots, in order:
        //  1. AppContext.BaseDirectory — the CLI / test-host case (libz3 copied
        //     to the output root, or under runtimes/<rid>/native).
        //  2. The directory containing this assembly — the MSBuild task case
        //     (Calor.Sdk package): AppContext.BaseDirectory is the MSBuild host
        //     directory there, while the packaged natives sit next to calor.dll
        //     under tasks/net10.0/runtimes/<rid>/native.
        var basePaths = new List<string> { AppContext.BaseDirectory };
        var assemblyDir = Path.GetDirectoryName(typeof(Z3ContextFactory).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir) && !basePaths.Contains(assemblyDir))
            basePaths.Add(assemblyDir);

        var libPaths = new List<string>();
        foreach (var basePath in basePaths)
        {
            // Try each root first (where we copy the current platform's native lib)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libPaths.Add(Path.Combine(basePath, "libz3.dll"));
                libPaths.Add(Path.Combine(basePath, "runtimes", "win-x64", "native", "libz3.dll"));
                libPaths.Add(Path.Combine(basePath, "runtimes", "win-arm64", "native", "libz3.dll"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libPaths.Add(Path.Combine(basePath, "libz3.dylib"));
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    libPaths.Add(Path.Combine(basePath, "runtimes", "osx-arm64", "native", "libz3.dylib"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libPaths.Add(Path.Combine(basePath, "libz3.so"));
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    libPaths.Add(Path.Combine(basePath, "runtimes", "linux-arm64", "native", "libz3.so"));
                else
                    libPaths.Add(Path.Combine(basePath, "runtimes", "linux-x64", "native", "libz3.so"));
            }
        }

        return libPaths;
    }

    /// <summary>
    /// Gets whether the Z3 native library is available on this system.
    /// This property is safe to access even if the Z3 assembly cannot be loaded.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue)
                return _isAvailable.Value;

            lock (_lock)
            {
                _isAvailable ??= CheckAvailabilitySafe();
                return _isAvailable.Value;
            }
        }
    }

    /// <summary>
    /// Attempts to create a Z3 context. Returns null if Z3 is unavailable.
    /// </summary>
    public static object? TryCreate()
    {
        if (!IsAvailable)
            return null;

        return TryCreateInternal();
    }

    /// <summary>
    /// Creates a Z3 context or throws if unavailable.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Z3 is not available.</exception>
    public static Microsoft.Z3.Context Create()
    {
        if (!IsAvailable)
            throw new InvalidOperationException(
                "Z3 native library is not available. Install the Z3 solver or ensure the native library is in the system path.");

        return CreateInternal();
    }

    /// <summary>
    /// Safe availability check that uses reflection to avoid JIT issues with Z3 types.
    /// </summary>
    private static bool CheckAvailabilitySafe()
    {
        // Try direct instantiation with explicit method impl to prevent JIT from loading Z3 types early
        return TryCheckAvailabilityDirect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryCheckAvailabilityDirect()
    {
        try
        {
            // Ensure our native library resolver is registered before accessing Z3 types
            EnsureResolverRegistered();

            // Simply try to create a Z3 Context - this will fail if native lib isn't available
            using var ctx = new Microsoft.Z3.Context();
            var testExpr = ctx.MkIntConst("__z3_test__");
            return testExpr != null;
        }
        catch (Exception ex)
        {
            // Optional diagnosis path — set CALOR_Z3_DEBUG=1 to surface the real load failure.
            if (Environment.GetEnvironmentVariable("CALOR_Z3_DEBUG") == "1")
            {
                Console.Error.WriteLine($"[Z3 load failure] {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.Error.WriteLine($"[Z3 inner] {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.Error.WriteLine($"[Z3 base path] {AppContext.BaseDirectory}");
                Console.Error.WriteLine($"[Z3 native handle] 0x{_z3NativeHandle.ToInt64():X}");
            }
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? TryCreateInternal()
    {
        try
        {
            return new Microsoft.Z3.Context();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Microsoft.Z3.Context CreateInternal()
    {
        return new Microsoft.Z3.Context();
    }

    /// <summary>
    /// Gets the Z3 library version string, or null if Z3 is unavailable.
    /// </summary>
    public static string? GetZ3Version()
    {
        if (!IsAvailable)
            return null;

        return GetZ3VersionInternal();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? GetZ3VersionInternal()
    {
        try
        {
            return Microsoft.Z3.Version.FullVersion;
        }
        catch
        {
            return null;
        }
    }
}
