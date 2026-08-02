using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end CLI tests: invoke calor.dll as a subprocess with multiple --input flags
/// and verify cross-module effect enforcement fires through the real command-line pipeline
/// (System.CommandLine parsing → CompilationDriver → CrossModuleEffectEnforcementPass).
/// </summary>
public class CliMultiFileTests : IDisposable
{
    private readonly string _tempDir;

    public CliMultiFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cli-mf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => CliTestHarness.RunCli(_tempDir, args);

    private (string APath, string BPath) WriteCrossModuleViolationPair()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(aPath, """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                §O{void}
                §E{db:w}
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §C{SaveOrder}
                §/C
            """);
        return (aPath, bPath);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Violation_Errors()
    {
        var (aPath, bPath) = WriteCrossModuleViolationPair();

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);

        Assert.NotEqual(0, exit);
        var combined = stdOut + stdErr;
        Assert.Contains("Calor0410", combined);
        Assert.Contains("HandleRequest", combined);
        Assert.Contains("db:w", combined);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Violation_PermissiveEffects_WarnsAndSucceeds()
    {
        // --permissive-effects must reach the cross-module pass: the violation is
        // demoted to a warning (still visible on stderr) and the compile succeeds.
        var (aPath, bPath) = WriteCrossModuleViolationPair();

        var (exit, stdOut, stdErr) = RunCli(
            "--input", aPath, "--input", bPath, "--permissive-effects");

        Assert.True(exit == 0, $"Expected exit 0 under --permissive-effects. Exit={exit}\nStdOut:\n{stdOut}\nStdErr:\n{stdErr}");
        Assert.Contains("warning Calor0410", stdErr);
        Assert.Contains("HandleRequest", stdErr);
        Assert.DoesNotContain("error Calor0410", stdOut + stdErr);
    }

    [Fact]
    public void MultiFile_CrossModuleEffect_Declared_Succeeds()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        File.WriteAllText(aPath, """
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                §O{void}
                §E{db:w}
            """);
        File.WriteAllText(bPath, """
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §E{db:w}
                §C{SaveOrder}
                §/C
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);

        var combined = stdOut + stdErr;
        Assert.True(exit == 0, $"Expected clean compile. Exit={exit}\nStdOut:\n{stdOut}\nStdErr:\n{stdErr}");
        Assert.DoesNotContain("Calor0410", combined);
    }

    [Fact]
    public void MultiFile_OutputFlag_RejectedForMultipleInputs()
    {
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        var outPath = Path.Combine(_tempDir, "out.cs");
        File.WriteAllText(aPath, """
            §M{m1:A}
              §F{f001:Foo:pub}
                §O{void}
            """);
        File.WriteAllText(bPath, """
            §M{m2:B}
              §F{f001:Bar:pub}
                §O{void}
            """);

        var (exit, stdOut, stdErr) = RunCli(
            "--input", aPath, "--input", bPath, "--output", outPath);

        Assert.NotEqual(0, exit);
        Assert.Contains("--output is only supported when compiling a single file", stdOut + stdErr);
    }


    // ------------------------------------------------------------------
    // G3 (#809): cross-module calls must emit QUALIFIED C# that links under
    // csc — bare-name emission was CS0103 in every multi-module build.
    // ------------------------------------------------------------------

    private (string StorePath, string CatalogPath) WriteCrossModuleCallPair()
    {
        var storePath = Path.Combine(_tempDir, "store.calr");
        var catalogPath = Path.Combine(_tempDir, "catalog.calr");
        File.WriteAllText(storePath, """
            §M{m001:Store}
              §F{f001:SaveSnapshot:pub} (str:path, str:name) -> void
                §E{fs:w}
                §C{File.WriteAllText} §A path §A name §/C
            """);
        File.WriteAllText(catalogPath, """
            §M{m002:Catalog}
              §F{f001:Ping:pub} (str:path) -> void
                §E{fs:w}
                §C{SaveSnapshot} §A path §A "x" §/C
            """);
        return (storePath, catalogPath);
    }

    [Fact]
    public void MultiFile_CrossModuleCall_EmitsQualifiedTarget()
    {
        var (storePath, catalogPath) = WriteCrossModuleCallPair();

        var (exit, stdOut, stdErr) = RunCli("--input", storePath, "--input", catalogPath, "--enforce-effects");

        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");
        var emitted = File.ReadAllText(Path.Combine(_tempDir, "catalog.g.cs"));
        Assert.Contains("global::Store.StoreModule.SaveSnapshot(path, \"x\");", emitted);
    }

    [Fact]
    public void MultiFile_CrossModuleCall_OutputsCompileUnderRoslyn()
    {
        var (storePath, catalogPath) = WriteCrossModuleCallPair();

        var (exit, stdOut, stdErr) = RunCli("--input", storePath, "--input", catalogPath, "--enforce-effects");
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        // The #809 exit criterion: both emitted files must COMPILE AND LINK
        // together under Roslyn (the front-end succeeding is not enough).
        var storeCs = File.ReadAllText(Path.Combine(_tempDir, "store.g.cs"));
        var catalogCs = File.ReadAllText(Path.Combine(_tempDir, "catalog.g.cs"));

        var trees = new[]
        {
            // The MSBuild integration compiles with ImplicitUsings; mirror the
            // common set here so BCL names (File, Console) resolve.
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                "global using System; global using System.IO;"),
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(storeCs),
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(catalogCs)
        };
        var references = new[]
        {
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.IO.File).Assembly.Location),
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
        };
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "XModTest",
            trees,
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            // Calor.Runtime using is emitted but not referenced in this minimal compilation
            .Where(d => !d.GetMessage().Contains("'Calor'"))
            .ToArray();
        Assert.True(errors.Length == 0,
            "emitted C# fails to link: " + string.Join("; ", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void MultiFile_AmbiguousBareName_StaysUnqualified()
    {
        // Two modules export the same name: qualification must NOT guess —
        // the bare emission is kept (mirroring effect resolution's
        // skip-ambiguous rule), and csc's CS0103 remains the honest failure.
        var aPath = Path.Combine(_tempDir, "a.calr");
        var bPath = Path.Combine(_tempDir, "b.calr");
        var cPath = Path.Combine(_tempDir, "c.calr");
        File.WriteAllText(aPath, """
            §M{m001:Alpha}
              §F{f001:Emit:pub} () -> void
                §E{cw}
                §P "alpha"
            """);
        File.WriteAllText(bPath, """
            §M{m002:Beta}
              §F{f001:Emit:pub} () -> void
                §E{cw}
                §P "beta"
            """);
        File.WriteAllText(cPath, """
            §M{m003:Caller}
              §F{f001:Run:pub} () -> void
                §E{cw}
                §C{Emit}
                §/C
            """);

        // WS-W2 (D-W2.1): an ambiguous cross-module bare name is not in the
        // unambiguous pre-parse map, so under default enforcement the per-module
        // pass now fails closed (Calor0411 + Calor0410) instead of assuming
        // purity. This test pins EMISSION shape, so it compiles under the
        // --permissive-effects waiver; csc's CS0103 remains the honest failure.
        RunCli("--input", aPath, "--input", bPath, "--input", cPath, "--permissive-effects");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "c.g.cs"));
        Assert.Contains("Emit();", emitted);
        Assert.DoesNotContain("global::Alpha", emitted);
        Assert.DoesNotContain("global::Beta", emitted);
    }

    [Fact]
    public void MultiFile_SelfModuleCall_StaysUnqualified()
    {
        var aPath = Path.Combine(_tempDir, "self.calr");
        var bPath = Path.Combine(_tempDir, "other.calr");
        File.WriteAllText(aPath, """
            §M{m001:SelfMod}
              §F{f001:Helper:pub} () -> void
                §E{cw}
                §P "hi"
              §F{f002:Run:pub} () -> void
                §E{cw}
                §C{Helper}
                §/C
            """);
        File.WriteAllText(bPath, """
            §M{m002:OtherMod}
              §F{f001:Unrelated:pub} () -> void
                §E{}
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "self.g.cs"));
        Assert.Contains("Helper();", emitted);
        Assert.DoesNotContain("global::SelfMod", emitted);
    }

    [Fact]
    public void MultiFile_CrossModuleCall_WithContracts_VerifiesAndLinks()
    {
        // D-G4.2 seed: contracts and effects on the same cross-module chain —
        // the multi-module surface the Guarantees fixtures build on.
        var mathPath = Path.Combine(_tempDir, "math.calr");
        var appPath = Path.Combine(_tempDir, "app.calr");
        File.WriteAllText(mathPath, """
            §M{m001:MathMod}
              §F{f001:Clamp:pub} (i32:x) -> i32
                §Q (>= x (- 0 1000))
                §S (>= result 0)
                §IF{if1} (< x 0)
                  §R 0
                §EL
                  §R x
            """);
        File.WriteAllText(appPath, """
            §M{m002:AppMod}
              §F{f001:Use:pub} (i32:v) -> void
                §C{Clamp} §A v §/C
            """);

        var (exit, stdOut, stdErr) = RunCli(
            "--input", mathPath, "--input", appPath, "--verify");
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "app.g.cs"));
        Assert.Contains("global::MathMod.MathModModule.Clamp(", emitted);
    }


    [Fact]
    public void MultiFile_ClassSiblingMethodCall_IsNotMisqualified()
    {
        // #823 review C1: a class method calling a SIBLING method bare must not
        // be rewritten to another module's same-named public function — that
        // compiled cleanly and silently ran the wrong code.
        var aPath = Path.Combine(_tempDir, "alpha.calr");
        var bPath = Path.Combine(_tempDir, "beta.calr");
        File.WriteAllText(aPath, """
            §M{m001:Alpha}
              §CL{c001:Keeper:pub}
                §MT{t001:Save:pub} () -> void
                  §E{cw}
                  §P "keeper-save"
                §MT{t002:Other:pub} () -> void
                  §E{cw}
                  §C{Save}
                  §/C
            """);
        File.WriteAllText(bPath, """
            §M{m002:Beta}
              §F{f001:Save:pub} () -> void
                §E{cw}
                §P "beta-save"
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "alpha.g.cs"));
        Assert.DoesNotContain("global::Beta", emitted);
    }

    [Fact]
    public void MultiFile_DelegateParameterCall_IsNotMisqualified()
    {
        // #823 review C2: a parameter (or local) holding a delegate, invoked by
        // name, must win over another module's same-named public function.
        // (Known pre-existing twin on the ENFORCEMENT side: the cross-module
        // registry also resolves the shadowed bare name to Lib.Notify and
        // charges its effects — hence the §E{cw} below. Same on main;
        // out of #823's emission scope, noted in the PR.)
        var aPath = Path.Combine(_tempDir, "caller.calr");
        var bPath = Path.Combine(_tempDir, "lib.calr");
        File.WriteAllText(aPath, """
            §M{m001:CallerMod}
              §F{f001:Run:pub} (Func<i32,i32>:Notify) -> void
                §E{cw}
                §C{Notify} §A INT:42 §/C
            """);
        File.WriteAllText(bPath, """
            §M{m002:Lib}
              §F{f001:Notify:pub} (i32:x) -> void
                §E{cw}
                §P x
            """);

        // WS-W2 (D-W2.1): invoking the Func-typed 'Notify' parameter is now a
        // Calor0418 error under default enforcement (the parameter shadows the
        // cross-module name for effects exactly as it does for emission). This
        // test pins EMISSION shape, so it compiles under the
        // --permissive-effects waiver (Calor0418 demoted to a warning).
        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath, "--permissive-effects");
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "caller.g.cs"));
        Assert.Contains("Notify(42);", emitted);
        Assert.DoesNotContain("global::Lib", emitted);
    }

    [Fact]
    public void MultiFile_InternalCrossModuleFunction_IsQualified()
    {
        // #823 review M1: enforcement resolves INTERNAL cross-module functions
        // too; emission must qualify them or the front-end passes and csc fails
        // (the exact #809 shape). The emitted callee is internal static in the
        // same assembly, so the qualified call is legal C#.
        var storePath = Path.Combine(_tempDir, "istore.calr");
        var catalogPath = Path.Combine(_tempDir, "icatalog.calr");
        File.WriteAllText(storePath, """
            §M{m001:IStore}
              §F{f001:Stash:int} (str:path) -> void
                §E{fs:w}
                §C{File.WriteAllText} §A path §A "x" §/C
            """);
        File.WriteAllText(catalogPath, """
            §M{m002:ICatalog}
              §F{f001:Ping:pub} (str:path) -> void
                §E{fs:w}
                §C{Stash} §A path §/C
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", storePath, "--input", catalogPath);
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "icatalog.g.cs"));
        Assert.Contains("global::IStore.IStoreModule.Stash(path);", emitted);
    }


    [Fact]
    public void MultiFile_NestedClassCallingEnclosingStatic_IsNotMisqualified()
    {
        // #823 re-review NEW-1: enclosing classes' members are bare-visible from
        // nested types; the guard must consult the whole class-scope stack.
        var aPath = Path.Combine(_tempDir, "nest.calr");
        var bPath = Path.Combine(_tempDir, "nbeta.calr");
        File.WriteAllText(aPath, """
            §M{m001:NAlpha}
              §CL{c001:Outer:pub}
                §MT{t001:Save:pub:stat} () -> void
                  §E{cw}
                  §P "outer-static-save"
                §CL{c002:Inner:pub}
                  §MT{t002:Go:pub} () -> void
                    §E{cw}
                    §C{Save}
                    §/C
            """);
        File.WriteAllText(bPath, """
            §M{m002:NBeta}
              §F{f001:Save:pub} () -> void
                §E{cw}
                §P "beta-save"
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "nest.g.cs"));
        Assert.DoesNotContain("global::NBeta", emitted);
    }

    [Fact]
    public void MultiFile_DerivedClassMethod_NeverQualifies()
    {
        // #823 re-review NEW-2: inherited members are not enumerable at emission,
        // so derived classes suppress qualification entirely — a bare call to an
        // inherited member must stay bare (CS0103 for genuinely-cross-module
        // calls is the accepted trade-off; recorded in the CHANGELOG).
        var aPath = Path.Combine(_tempDir, "derived.calr");
        var bPath = Path.Combine(_tempDir, "dlib.calr");
        File.WriteAllText(aPath, """
            §M{m001:DApp}
              §CL{c001:Base:pub}
                §MT{t001:Ping:pub} () -> void
                  §E{cw}
                  §P "base-ping"
              §CL{c002:Derived:Base}
                §MT{t002:Go:pub} () -> void
                  §E{cw}
                  §C{Ping}
                  §/C
            """);
        File.WriteAllText(bPath, """
            §M{m002:DLib}
              §F{f001:Ping:pub} () -> void
                §E{cw}
                §P "lib-ping"
            """);

        var (exit, stdOut, stdErr) = RunCli("--input", aPath, "--input", bPath);
        Assert.True(exit == 0, $"compile failed: {stdOut}{stdErr}");

        var emitted = File.ReadAllText(Path.Combine(_tempDir, "derived.g.cs"));
        Assert.DoesNotContain("global::DLib", emitted);
    }
}
