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
        // Async console operations (via Console.Out/Console.Error TextWriter)
        ["System.IO.TextWriter::WriteAsync(System.String)"] = EffectSet.From("cw"),
        ["System.IO.TextWriter::WriteAsync(System.Char)"] = EffectSet.From("cw"),
        ["System.IO.TextWriter::WriteLineAsync()"] = EffectSet.From("cw"),
        ["System.IO.TextWriter::WriteLineAsync(System.String)"] = EffectSet.From("cw"),
        ["System.IO.TextWriter::WriteLineAsync(System.Char)"] = EffectSet.From("cw"),
        ["System.IO.TextWriter::FlushAsync()"] = EffectSet.From("cw"),
        ["System.IO.StreamWriter::WriteAsync(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.StreamWriter::WriteLineAsync(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.StreamWriter::FlushAsync()"] = EffectSet.From("fs:w"),
        ["System.IO.StreamReader::ReadLineAsync()"] = EffectSet.From("fs:r"),
        ["System.IO.StreamReader::ReadToEndAsync()"] = EffectSet.From("fs:r"),

        // File I/O
        ["System.IO.File::ReadAllText(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllText(System.String,System.Text.Encoding)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllLines(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllLines(System.String,System.Text.Encoding)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllBytes(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllTextAsync(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllTextAsync(System.String,System.Threading.CancellationToken)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllLinesAsync(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::ReadAllBytesAsync(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::WriteAllText(System.String,System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllText(System.String,System.String,System.Text.Encoding)"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllLines(System.String,System.String[])"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllLines(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllBytes(System.String,System.Byte[])"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllTextAsync(System.String,System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fs:w"),
        ["System.IO.File::WriteAllBytesAsync(System.String,System.Byte[])"] = EffectSet.From("fs:w"),
        ["System.IO.File::AppendAllText(System.String,System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::AppendAllLines(System.String,System.Collections.Generic.IEnumerable{System.String})"] = EffectSet.From("fs:w"),
        ["System.IO.File::AppendAllTextAsync(System.String,System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::Delete(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::Copy(System.String,System.String)"] = EffectSet.From("fs:rw"),
        ["System.IO.File::Copy(System.String,System.String,System.Boolean)"] = EffectSet.From("fs:rw"),
        ["System.IO.File::Move(System.String,System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::Exists(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::Create(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::Open(System.String,System.IO.FileMode)"] = EffectSet.From("fs:rw"),
        ["System.IO.File::OpenRead(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.File::OpenWrite(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.File::OpenText(System.String)"] = EffectSet.From("fs:r"),

        // Directory I/O
        ["System.IO.Directory::CreateDirectory(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.Directory::Delete(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.Directory::Delete(System.String,System.Boolean)"] = EffectSet.From("fs:w"),
        ["System.IO.Directory::Exists(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.Directory::GetFiles(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.Directory::GetFiles(System.String,System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.Directory::GetDirectories(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.Directory::Move(System.String,System.String)"] = EffectSet.From("fs:w"),

        // StreamReader/StreamWriter
        ["System.IO.StreamReader::.ctor(System.String)"] = EffectSet.From("fs:r"),
        ["System.IO.StreamReader::.ctor(System.IO.Stream)"] = EffectSet.Empty,  // Stream already opened
        ["System.IO.StreamWriter::.ctor(System.String)"] = EffectSet.From("fs:w"),
        ["System.IO.StreamWriter::.ctor(System.String,System.Boolean)"] = EffectSet.From("fs:w"),
        ["System.IO.StreamWriter::.ctor(System.IO.Stream)"] = EffectSet.Empty,  // Stream already opened

        // Network - HttpClient
        ["System.Net.Http.HttpClient::GetAsync(System.String)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::GetAsync(System.Uri)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::GetAsync(System.String,System.Threading.CancellationToken)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::GetStringAsync(System.String)"] = EffectSet.From("net:r"),
        ["System.Net.Http.HttpClient::GetStringAsync(System.Uri)"] = EffectSet.From("net:r"),
        ["System.Net.Http.HttpClient::GetByteArrayAsync(System.String)"] = EffectSet.From("net:r"),
        ["System.Net.Http.HttpClient::GetStreamAsync(System.String)"] = EffectSet.From("net:r"),
        ["System.Net.Http.HttpClient::PostAsync(System.String,System.Net.Http.HttpContent)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::PostAsync(System.Uri,System.Net.Http.HttpContent)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::PutAsync(System.String,System.Net.Http.HttpContent)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::DeleteAsync(System.String)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage)"] = EffectSet.From("net:rw"),
        ["System.Net.Http.HttpClient::SendAsync(System.Net.Http.HttpRequestMessage,System.Threading.CancellationToken)"] = EffectSet.From("net:rw"),

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
        ["System.Environment::GetEnvironmentVariable(System.String)"] = EffectSet.From("env:r"),
        ["System.Environment::SetEnvironmentVariable(System.String,System.String)"] = EffectSet.From("env:w"),
        ["System.Environment::get_CurrentDirectory()"] = EffectSet.From("env:r"),
        ["System.Environment::set_CurrentDirectory(System.String)"] = EffectSet.From("env:w"),
        ["System.Environment::GetFolderPath(System.Environment+SpecialFolder)"] = EffectSet.From("env:r"),
        ["System.Environment::get_MachineName()"] = EffectSet.From("env:r"),
        ["System.Environment::get_UserName()"] = EffectSet.From("env:r"),
        ["System.Environment::get_CommandLine()"] = EffectSet.From("env:r"),
        ["System.Environment::GetCommandLineArgs()"] = EffectSet.From("env:r"),
        ["System.Environment::Exit(System.Int32)"] = EffectSet.From("proc"),

        // Process
        ["System.Diagnostics.Process::Start(System.String)"] = EffectSet.From("proc"),
        ["System.Diagnostics.Process::Start(System.String,System.String)"] = EffectSet.From("proc"),
        ["System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)"] = EffectSet.From("proc"),
        ["System.Diagnostics.Process::Kill()"] = EffectSet.From("proc"),

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

        // LINQ (System.Linq.Enumerable) — all pure/side-effect-free
        ["System.Linq.Enumerable::Where()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Select()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::SelectMany()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::OrderBy()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::OrderByDescending()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ThenBy()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ThenByDescending()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::GroupBy()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Join()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::GroupJoin()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::First()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::FirstOrDefault()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Single()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::SingleOrDefault()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Last()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::LastOrDefault()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Any()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::All()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Count()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Sum()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Average()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Min()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Max()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Distinct()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Union()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Intersect()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Except()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Skip()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Take()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::SkipWhile()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::TakeWhile()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Reverse()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Concat()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Zip()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Aggregate()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ToList()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ToArray()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ToDictionary()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::ToHashSet()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::OfType()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Cast()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::AsEnumerable()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::DefaultIfEmpty()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Append()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Prepend()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Contains()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::SequenceEqual()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Range()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Repeat()"] = EffectSet.Empty,
        ["System.Linq.Enumerable::Empty()"] = EffectSet.Empty,

        // Pure methods (explicit empty effects)
        ["System.String::Concat(System.String,System.String)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object,System.Object)"] = EffectSet.Empty,
        ["System.String::Format(System.String,System.Object[])"] = EffectSet.Empty,
        ["System.Int32::Parse(System.String)"] = EffectSet.Empty,
        ["System.Int32::TryParse(System.String,System.Int32@)"] = EffectSet.Empty,
        ["System.Double::Parse(System.String)"] = EffectSet.Empty,
        ["System.Math::Max(System.Int32,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Max(System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Min(System.Int32,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Min(System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Abs(System.Int32)"] = EffectSet.Empty,
        ["System.Math::Abs(System.Double)"] = EffectSet.Empty,
        ["System.Math::Pow(System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Sqrt(System.Double)"] = EffectSet.Empty,
        ["System.Math::Floor(System.Double)"] = EffectSet.Empty,
        ["System.Math::Ceiling(System.Double)"] = EffectSet.Empty,
        ["System.Math::Round(System.Double)"] = EffectSet.Empty,
        ["System.Math::Round(System.Double,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Log(System.Double)"] = EffectSet.Empty,
        ["System.Math::Log(System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Log10(System.Double)"] = EffectSet.Empty,
        ["System.Math::Log2(System.Double)"] = EffectSet.Empty,
        ["System.Math::Sin(System.Double)"] = EffectSet.Empty,
        ["System.Math::Cos(System.Double)"] = EffectSet.Empty,
        ["System.Math::Tan(System.Double)"] = EffectSet.Empty,
        ["System.Math::Clamp(System.Int32,System.Int32,System.Int32)"] = EffectSet.Empty,
        ["System.Math::Clamp(System.Double,System.Double,System.Double)"] = EffectSet.Empty,
        ["System.Math::Sign(System.Int32)"] = EffectSet.Empty,
        ["System.Math::Sign(System.Double)"] = EffectSet.Empty,
        ["System.Math::Truncate(System.Double)"] = EffectSet.Empty,
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
