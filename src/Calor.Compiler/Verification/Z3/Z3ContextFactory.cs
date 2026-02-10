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

            // Register resolver for any future P/Invoke calls
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;
            _resolverRegistered = true;
        }
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

    private static IntPtr TryLoadZ3Native()
    {
        var basePath = AppContext.BaseDirectory;
        var libPaths = new List<string>();

        // Try output root first (where we copy the current platform's native lib)
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
            else
                libPaths.Add(Path.Combine(basePath, "runtimes", "osx-x64", "native", "libz3.dylib"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            libPaths.Add(Path.Combine(basePath, "libz3.so"));
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                libPaths.Add(Path.Combine(basePath, "runtimes", "linux-arm64", "native", "libz3.so"));
            else
                libPaths.Add(Path.Combine(basePath, "runtimes", "linux-x64", "native", "libz3.so"));
        }

        foreach (var path in libPaths)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                return handle;
        }

        return IntPtr.Zero;
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
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (TypeInitializationException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (Exception)
        {
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
}
