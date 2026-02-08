using System.Reflection;
using System.Runtime.CompilerServices;

namespace Calor.Compiler.Verification.Z3;

/// <summary>
/// Factory for creating Z3 contexts with graceful fallback when Z3 native library is unavailable.
/// </summary>
public static class Z3ContextFactory
{
    private static bool? _isAvailable;
    private static readonly object _lock = new();

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
        try
        {
            // First check if the assembly can even be loaded
            Assembly? assembly;
            try
            {
                assembly = Assembly.Load("Microsoft.Z3");
            }
            catch
            {
                return false;
            }

            if (assembly == null)
                return false;

            // Use reflection to find the Context type and create an instance
            // This avoids compile-time type references that can cause JIT failures
            var contextType = assembly.GetType("Microsoft.Z3.Context");
            if (contextType == null)
                return false;

            // Try to create a Context instance using reflection
            object? context = null;
            try
            {
                context = Activator.CreateInstance(contextType);
                if (context == null)
                    return false;

                // Try to call a simple method to verify the native library is working
                var mkIntConstMethod = contextType.GetMethod("MkIntConst", new[] { typeof(string) });
                if (mkIntConstMethod == null)
                    return false;

                var result = mkIntConstMethod.Invoke(context, new object[] { "__z3_test__" });
                return result != null;
            }
            finally
            {
                // Dispose the context if it implements IDisposable
                if (context is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }
            }
        }
        catch (TargetInvocationException ex) when (
            ex.InnerException is DllNotFoundException ||
            ex.InnerException is TypeInitializationException ||
            ex.InnerException is FileNotFoundException)
        {
            return false;
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
            // Any other exception during initialization means Z3 isn't working
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
