namespace Calor.Compiler.Effects;

/// <summary>
/// Built-in effect catalog for standard library methods.
/// Uses fully-qualified signatures for disambiguation.
/// </summary>
public static class BuiltInEffects
{
    /// <summary>
    /// Maps fully-qualified method signatures to their effects.
    /// Signature format: Namespace.Type::Method(ParamType1,ParamType2)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, EffectSet> Catalog = new Dictionary<string, EffectSet>(StringComparer.Ordinal)
    {
        // Console I/O
        ["System.Console::WriteLine()"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.String)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Object)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Int32)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Int64)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Double)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Boolean)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.Char)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.String,System.Object)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.String,System.Object,System.Object)"] = EffectSet.From("cw"),
        ["System.Console::WriteLine(System.String,System.Object[])"] = EffectSet.From("cw"),
        ["System.Console::Write(System.String)"] = EffectSet.From("cw"),
        ["System.Console::Write(System.Object)"] = EffectSet.From("cw"),
        ["System.Console::Write(System.Int32)"] = EffectSet.From("cw"),
        ["System.Console::Write(System.Char)"] = EffectSet.From("cw"),
        ["System.Console::ReadLine()"] = EffectSet.From("cr"),
        ["System.Console::Read()"] = EffectSet.From("cr"),
        ["System.Console::ReadKey()"] = EffectSet.From("cr"),
        ["System.Console::ReadKey(System.Boolean)"] = EffectSet.From("cr"),

        // File I/O
        ["System.IO.File::ReadAllText(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllText(System.String,System.Text.Encoding)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllLines(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllLines(System.String,System.Text.Encoding)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllBytes(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllTextAsync(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllTextAsync(System.String,System.Threading.CancellationToken)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllLinesAsync(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::ReadAllBytesAsync(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::WriteAllText(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllText(System.String,System.String,System.Text.Encoding)"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllLines(System.String,System.String[])"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllLines(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllBytes(System.String,System.Byte[])"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllTextAsync(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fw"),
        ["System.IO.File::WriteAllBytesAsync(System.String,System.Byte[])"] = EffectSet.From("fw"),
        ["System.IO.File::AppendAllText(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::AppendAllLines(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fw"),
        ["System.IO.File::AppendAllTextAsync(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::Delete(System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::Copy(System.String,System.String)"] = EffectSet.From("fr", "fw"),
        ["System.IO.File::Copy(System.String,System.String,System.Boolean)"] = EffectSet.From("fr", "fw"),
        ["System.IO.File::Move(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::Exists(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::Create(System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::Open(System.String,System.IO.FileMode)"] = EffectSet.From("fr", "fw"),
        ["System.IO.File::OpenRead(System.String)"] = EffectSet.From("fr"),
        ["System.IO.File::OpenWrite(System.String)"] = EffectSet.From("fw"),
        ["System.IO.File::OpenText(System.String)"] = EffectSet.From("fr"),

        // Directory I/O
        ["System.IO.Directory::CreateDirectory(System.String)"] = EffectSet.From("fw"),
        ["System.IO.Directory::Delete(System.String)"] = EffectSet.From("fw"),
        ["System.IO.Directory::Delete(System.String,System.Boolean)"] = EffectSet.From("fw"),
        ["System.IO.Directory::Exists(System.String)"] = EffectSet.From("fr"),
        ["System.IO.Directory::GetFiles(System.String)"] = EffectSet.From("fr"),
        ["System.IO.Directory::GetFiles(System.String,System.String)"] = EffectSet.From("fr"),
        ["System.IO.Directory::GetDirectories(System.String)"] = EffectSet.From("fr"),
        ["System.IO.Directory::Move(System.String,System.String)"] = EffectSet.From("fw"),

        // StreamReader/StreamWriter
        ["System.IO.StreamReader::.ctor(System.String)"] = EffectSet.From("fr"),
        ["System.IO.StreamReader::.ctor(System.IO.Stream)"] = EffectSet.Empty,  // Stream already opened
        ["System.IO.StreamWriter::.ctor(System.String)"] = EffectSet.From("fw"),
        ["System.IO.StreamWriter::.ctor(System.String,System.Boolean)"] = EffectSet.From("fw"),
        ["System.IO.StreamWriter::.ctor(System.IO.Stream)"] = EffectSet.Empty,  // Stream already opened

        // Network - HttpClient
        ["System.Net.Http.HttpClient::GetAsync(System.String)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetAsync(System.Uri)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetAsync(System.String,System.Threading.CancellationToken)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetStringAsync(System.String)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetStringAsync(System.Uri)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetByteArrayAsync(System.String)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::GetStreamAsync(System.String)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::PostAsync(System.String,System.Net.Http.HttpContent)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::PostAsync(System.Uri,System.Net.Http.HttpContent)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::PutAsync(System.String,System.Net.Http.HttpContent)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::DeleteAsync(System.String)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage)"] = EffectSet.From("net"),
        ["System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage,System.Threading.CancellationToken)"] = EffectSet.From("net"),

        // Random/Nondeterminism
        ["System.Random::Next()"] = EffectSet.From("rand"),
        ["System.Random::Next(System.Int32)"] = EffectSet.From("rand"),
        ["System.Random::Next(System.Int32,System.Int32)"] = EffectSet.From("rand"),
        ["System.Random::NextDouble()"] = EffectSet.From("rand"),
        ["System.Random::NextBytes(System.Byte[])"] = EffectSet.From("rand"),
        ["System.Random::NextInt64()"] = EffectSet.From("rand"),
        ["System.Random::NextSingle()"] = EffectSet.From("rand"),
        ["System.Random::.ctor()"] = EffectSet.From("rand"),  // Seeds from time
        ["System.Random::.ctor(System.Int32)"] = EffectSet.Empty,  // Deterministic seed

        // DateTime (properties treated as method calls)
        ["System.DateTime::get_Now()"] = EffectSet.From("time"),
        ["System.DateTime::get_UtcNow()"] = EffectSet.From("time"),
        ["System.DateTime::get_Today()"] = EffectSet.From("time"),
        ["System.DateTimeOffset::get_Now()"] = EffectSet.From("time"),
        ["System.DateTimeOffset::get_UtcNow()"] = EffectSet.From("time"),

        // Guid
        ["System.Guid::NewGuid()"] = EffectSet.From("rand"),

        // Environment
        ["System.Environment::GetEnvironmentVariable(System.String)"] = EffectSet.From("fr"),
        ["System.Environment::SetEnvironmentVariable(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.Environment::get_CurrentDirectory()"] = EffectSet.From("fr"),
        ["System.Environment::set_CurrentDirectory(System.String)"] = EffectSet.From("fw"),
        ["System.Environment::GetFolderPath(System.Environment+SpecialFolder)"] = EffectSet.From("fr"),
        ["System.Environment::get_MachineName()"] = EffectSet.From("fr"),
        ["System.Environment::get_UserName()"] = EffectSet.From("fr"),
        ["System.Environment::get_CommandLine()"] = EffectSet.From("fr"),
        ["System.Environment::GetCommandLineArgs()"] = EffectSet.From("fr"),
        ["System.Environment::Exit(System.Int32)"] = EffectSet.From("cw"),  // Effectively terminates

        // Process
        ["System.Diagnostics.Process::Start(System.String)"] = EffectSet.From("fw"),
        ["System.Diagnostics.Process::Start(System.String,System.String)"] = EffectSet.From("fw"),
        ["System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)"] = EffectSet.From("fw"),
        ["System.Diagnostics.Process::Kill()"] = EffectSet.From("fw"),

        // Debug/Trace (informational only)
        ["System.Diagnostics.Debug::WriteLine(System.String)"] = EffectSet.From("cw"),
        ["System.Diagnostics.Debug::Write(System.String)"] = EffectSet.From("cw"),
        ["System.Diagnostics.Trace::WriteLine(System.String)"] = EffectSet.From("cw"),
        ["System.Diagnostics.Trace::Write(System.String)"] = EffectSet.From("cw"),

        // Thread/Task (not nondeterministic by themselves, but noted for completeness)
        ["System.Threading.Thread::Sleep(System.Int32)"] = EffectSet.Empty,
        ["System.Threading.Thread::Sleep(System.TimeSpan)"] = EffectSet.Empty,
        ["System.Threading.Tasks.Task::Delay(System.Int32)"] = EffectSet.Empty,
        ["System.Threading.Tasks.Task::Delay(System.TimeSpan)"] = EffectSet.Empty,

        // Pure methods (explicit empty effects)
        ["System.String::Concat(System.String,System.String)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object,System.Object)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object[])"] = EffectSet.Empty,
        ["System.Int32::Parse(System.String)"] = EffectSet.Empty,
        ["System.Int32::TryParse(System.String,System.Int32@)"] = EffectSet.Empty,
        ["System.Double::Parse(System.String)"] = EffectSet.Empty,
        ["System.Math::Max(System.Int32,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Min(System.Int32,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Abs(System.Int32)"] = EffectSet.Empty,
        ["System.Math::Pow(System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Sqrt(System.Double)"] = EffectSet.Empty,
    };

    /// <summary>
    /// Attempts to look up effects for a method signature.
    /// Returns null if the signature is not in the catalog.
    /// </summary>
    public static EffectSet? TryGetEffects(string signature)
    {
        return Catalog.TryGetValue(signature, out var effects) ? effects : null;
    }

    /// <summary>
    /// Returns true if the signature is a known pure method.
    /// </summary>
    public static bool IsKnownPure(string signature)
    {
        return Catalog.TryGetValue(signature, out var effects) && effects.IsEmpty;
    }

    /// <summary>
    /// Returns true if the method signature is in the catalog.
    /// </summary>
    public static bool IsKnown(string signature)
    {
        return Catalog.ContainsKey(signature);
    }
}
