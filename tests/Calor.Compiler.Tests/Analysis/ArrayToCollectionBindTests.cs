using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// The language-level E1a fix (#722): the binder rejects an array-typed
/// initializer bound to a concrete generic collection (<c>List&lt;T&gt;</c>, …),
/// which C# would reject with CS0029 — instead of emitting broken C# and exiting 0.
/// Mirrors C#'s rule: arrays satisfy the collection interfaces but not the classes.
/// </summary>
public class ArrayToCollectionBindTests
{
    private static IReadOnlyList<Diagnostic> Validate(string source, bool strictInference = true)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer errors: " + string.Join("; ", lex.Select(d => d.Message)));

        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser errors: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference).Check(module);
        return bag.ToList();
    }

    private const string ReadAllLinesToList =
        "module Files Files block\n" +
        "  func CountLines (string:path) → int\n" +
        "    ! fs:r\n" +
        "    let List<string>:lines = File.ReadAllLines(path)\n" +
        "    return lines.Length\n";

    private static bool HasArrayTrap(IReadOnlyList<Diagnostic> diags) =>
        diags.Any(d => d.Code == DiagnosticCode.BindArrayToConcreteCollection);

    [Fact]
    public void ReadAllLines_BoundToList_IsRejected()
    {
        Assert.True(HasArrayTrap(Validate(ReadAllLinesToList)));
    }

    [Fact]
    public void IsAlwaysOn_EvenWithStrictInferenceDisabled()
    {
        // Calor0254 is a hard type error, NOT a strict-inference diagnostic — it
        // must fire regardless of --strict-bind-inference (the CLI default is
        // non-strict). Pins the "always-on" design claim so a future refactor
        // cannot silently fold it into the strict-gated block.
        Assert.True(HasArrayTrap(Validate(ReadAllLinesToList, strictInference: false)));
    }

    [Fact]
    public void ReadAllLines_BoundToArrayForm_IsAccepted()
    {
        var diags = Validate(
            "module Files Files block\n" +
            "  func CountLines (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let string[]:lines = File.ReadAllLines(path)\n" +
            "    return lines.Length\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayReturningUserFunction_BoundToList_IsRejected()
    {
        // Generality beyond the BCL list: a user function declared -> [str].
        var diags = Validate(
            "module Test Test block\n" +
            "  func GetItems (string:path) → string[]\n" +
            "    ! fs:r\n" +
            "    return File.ReadAllLines(path)\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let List<string>:items = GetItems(path)\n" +
            "    return items.Length\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayBoundToCollectionInterface_IsAccepted()
    {
        // C# DOES allow array -> IEnumerable<T>/IList<T>/… — only the concrete
        // classes are rejected. The check must not over-fire on interfaces.
        var diags = Validate(
            "module Files Files block\n" +
            "  func Read (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let IEnumerable<string>:lines = File.ReadAllLines(path)\n" +
            "    return 0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ListFromNonArrayInitializer_IsAccepted()
    {
        // A List<str> bound to something that is not an array must not fire.
        var diags = Validate(
            "module Test Test block\n" +
            "  func Make () → List<string>\n" +
            "    !\n" +
            "    return MakeList()\n" +
            "\n" +
            "  func Use () → int\n" +
            "    !\n" +
            "    let List<string>:items = Make()\n" +
            "    return 0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayToList_IsRejected()
    {
        // §R returning an array from a List<str>-typed function (#724). This is the
        // signature variant the exemplar's own trap text warns about.
        var diags = Validate(
            "module Files Files block\n" +
            "  func Get (string:path) → List<string>\n" +
            "    ! fs:r\n" +
            "    return File.ReadAllLines(path)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayFormReturnType_IsAccepted()
    {
        var diags = Validate(
            "module Files Files block\n" +
            "  func Get (string:path) → string[]\n" +
            "    ! fs:r\n" +
            "    return File.ReadAllLines(path)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayToList_IsRejected()
    {
        // §ASSIGN reassigning an array into a List<str>-typed mutable variable (#724).
        var diags = Validate(
            "module Files Files block\n" +
            "  func Count (string:path) → int\n" +
            "    ! fs:r\n" +
            "    var List<string>:items\n" +
            "    set items File.ReadAllLines(path)\n" +
            "    return items.Length\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayFormVariable_IsAccepted()
    {
        var diags = Validate(
            "module Files Files block\n" +
            "  func Count (string:path) → int\n" +
            "    ! fs:r\n" +
            "    var string[]:items\n" +
            "    set items File.ReadAllLines(path)\n" +
            "    return items.Length\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void TrapInsideForeachBody_IsRejected()
    {
        // Regression: the pass must recurse into §EACH bodies — file/dir iteration
        // is the exact idiom where this trap occurs (PR #726 review finding 1).
        // 0.30: a native call is matched by its argument types (no name-only BCL fallback),
        // and this pass does not type loop variables, so the call reads a typed parameter.
        var diags = Validate(
            "module Files Files block\n" +
            "  func Scan (string:dir) → int\n" +
            "    ! fs:rw\n" +
            "    let string[]:dirs = Directory.GetDirectories(dir)\n" +
            "    each d in dirs\n" +
            "      let List<string>:lines = File.ReadAllLines(dir)\n" +
            "    return 0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignToCollectionTypedField_IsRejected()
    {
        // §ASSIGN targeting a class field declared as a concrete collection.
        var diags = Validate(
            "module Files Files block\n" +
            "  class Store\n" +
            "    field List<string>:items private\n" +
            "    func Load (string:path) → void\n" +
            "      ! fs:r\n" +
            "      set items File.ReadAllLines(path)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignToArrayFormField_IsAccepted()
    {
        var diags = Validate(
            "module Files Files block\n" +
            "  class Store\n" +
            "    field string[]:items private\n" +
            "    func Load (string:path) → void\n" +
            "      ! fs:r\n" +
            "      set items File.ReadAllLines(path)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void InnerScopeShadow_DoesNotMistypeOuterAssign()
    {
        // Scope hygiene (PR #726 review finding 3): a List<str> binding inside a
        // nested block must not overwrite the type of a same-named outer variable.
        // The outer §ASSIGN of an array into an outer [str] variable is correct and
        // must NOT be flagged by the inner shadow's type.
        var diags = Validate(
            "module Files Files block\n" +
            "  func Do (string:path, bool:flag) → int\n" +
            "    ! fs:r\n" +
            "    var string[]:x = File.ReadAllLines(path)\n" +
            "    if (flag == true)\n" +
            "      let List<string>:x = MakeList()\n" +
            "    set x File.ReadAllLines(path)\n" +
            "    return 0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_ArrayToCollectionParam_IsRejected()
    {
        // #725: an array argument passed where a user function declares a
        // List<str> parameter (the call nested inside a return expression).
        var diags = Validate(
            "module T T block\n" +
            "  func Take (List<string>:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    return Take(File.ReadAllLines(path))\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_ArrayFormParam_IsAccepted()
    {
        var diags = Validate(
            "module T T block\n" +
            "  func Take (string[]:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    return Take(File.ReadAllLines(path))\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_InCallStatement_IsRejected()
    {
        var diags = Validate(
            "module T T block\n" +
            "  func Take (List<string>:items) → void\n" +
            "    !\n" +
            "    Console.WriteLine(items.Length)\n" +
            "\n" +
            "  func Use (string:path) → void\n" +
            "    ! fs:r\n" +
            "    Take(File.ReadAllLines(path))\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_OverloadArityDisambiguates()
    {
        // Two arity-based overloads of Take: the 1-arg takes List<str> (reject),
        // the 2-arg's first param is [str] (accept). The call resolves by arity.
        var oneArg = Validate(
            "module T T block\n" +
            "  func Take (List<string>:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  func Take (string[]:items, int:n) → int\n" +
            "    !\n" +
            "    return n\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    return Take(File.ReadAllLines(path))\n");
        Assert.True(HasArrayTrap(oneArg));

        var twoArg = Validate(
            "module T T block\n" +
            "  func Take (List<string>:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  func Take (string[]:items, int:n) → int\n" +
            "    !\n" +
            "    return n\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    return Take(File.ReadAllLines(path), 0)\n");
        Assert.False(HasArrayTrap(twoArg));
    }

    [Fact]
    public void ReturnTypeLookup_UsesCallableArityInsteadOfBareName()
    {
        var diagnostics = Validate(
            "module T T block\n" +
            "  func Load () → string[]\n" +
            "    !\n" +
            "    return File.ReadAllLines(\"data.txt\")\n" +
            "\n" +
            "  func Load (int:count) → List<string>\n" +
            "    !\n" +
            "    return new List<string>()\n" +
            "\n" +
            "  func Use () → void\n" +
            "    !\n" +
            "    let List<string>:items = Load()\n");

        Assert.True(HasArrayTrap(diagnostics));
    }

    [Fact]
    public void ReturnTypeLookup_UsesExactSameArityOverloadParameterTypes()
    {
        var arraySelected = Validate(
            "module T T block\n" +
            "  func Load (int:selector) → string[]\n" +
            "    !\n" +
            "    return File.ReadAllLines(\"data.txt\")\n" +
            "\n" +
            "  func Load (string:selector) → List<string>\n" +
            "    !\n" +
            "    return new List<string>()\n" +
            "\n" +
            "  func Use () → void\n" +
            "    !\n" +
            "    let List<string>:items = Load(1)\n");
        var listSelected = Validate(
            "module T T block\n" +
            "  func Load (int:selector) → string[]\n" +
            "    !\n" +
            "    return File.ReadAllLines(\"data.txt\")\n" +
            "\n" +
            "  func Load (string:selector) → List<string>\n" +
            "    !\n" +
            "    return new List<string>()\n" +
            "\n" +
            "  func Use () → void\n" +
            "    !\n" +
            "    let List<string>:items = Load(\"list\")\n");

        Assert.True(HasArrayTrap(arraySelected));
        Assert.False(HasArrayTrap(listSelected));
    }

    [Fact]
    public void FreeFunctionSharingMethodName_IsNotClobbered()
    {
        // PR #728 review finding 1: a method must not clobber a same-name/arity
        // free function's signature. Here the module-level call resolves to the
        // FREE function (array param) — correct code — and must NOT be flagged.
        var diags = Validate(
            "module T T block\n" +
            "  func Take (string[]:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  class Helper\n" +
            "    func Take (List<string>:items) → int\n" +
            "      !\n" +
            "      return items.Length\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    return Take(File.ReadAllLines(path))\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void IntraClassImplicitThisCall_ResolvesToTheMethod()
    {
        // The mirror of the above: inside the class, an unqualified §C{Take}
        // resolves to the class's own method (List<str> param) → still flagged.
        var diags = Validate(
            "module T T block\n" +
            "  func Take (string[]:items) → int\n" +
            "    !\n" +
            "    return items.Length\n" +
            "\n" +
            "  class Helper\n" +
            "    func Take (List<string>:items) → int\n" +
            "      !\n" +
            "      return items.Length\n" +
            "\n" +
            "    func Use (string:path) → int\n" +
            "      ! fs:r\n" +
            "      return Take(File.ReadAllLines(path))\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentInIfConditionCall_IsScanned()
    {
        // Finding 3 (statement expression slots): a call in an §IF condition is a
        // scanned expression position, not a silently-skipped one.
        var diags = Validate(
            "module T T block\n" +
            "  func Take (List<string>:items) → bool\n" +
            "    !\n" +
            "    return (items.Length > 0)\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    ! fs:r\n" +
            "    if Take(File.ReadAllLines(path))\n" +
            "      return 1\n" +
            "    return 0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void HashSetFromArray_IsRejected()
    {
        // Generality on the collection side: not just List<T>.
        var diags = Validate(
            "module Files Files block\n" +
            "  func Read (string:dir) → int\n" +
            "    ! fs:r\n" +
            "    let HashSet<string>:names = Directory.GetFiles(dir)\n" +
            "    return 0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void UserFunctionShadowingBclName_IsJudgedByItsRealReturnType()
    {
        // A user function named like a BCL method must win over the built-in
        // heuristic: here File.ReadAllLines is a user method returning List<str>
        // (not an array), so binding it to List<str> is correct — no false Calor0254.
        var diags = Validate(
            "module Shadow Shadow block\n" +
            "  class File\n" +
            "    func ReadAllLines (string:path) → List<string>\n" +
            "      !\n" +
            "      return MakeList()\n" +
            "\n" +
            "  func Use (string:path) → int\n" +
            "    !\n" +
            "    let List<string>:items = File.ReadAllLines(path)\n" +
            "    return 0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeCalls_ArrayChecksKeepTheirDiagnosticOwners(bool strictInference)
    {
        const string source = """
            module NativeBindings NativeBindings
              func Take (List<string>:items) → int
                !
                return 0
              func Use (string:path, Dictionary<int,int>:d) → List<string>
                ! fs:r
                each k, v in d
                  let List<string>:lines = File.ReadAllLines(path)
                var List<string>:items = new List<string>()
                set items File.ReadAllLines(path)
                Take(File.ReadAllLines(path))
                return File.ReadAllLines(path)
            """;

        var diagnostics = Validate(source, strictInference);
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code));
        Assert.Equal(
            new[]
            {
                "let List<string>:lines = File.ReadAllLines(path)",
                "set items File.ReadAllLines(path)",
                "File.ReadAllLines(path)",
                "return File.ReadAllLines(path)",
            },
            diagnostics.Select(diagnostic => source.Substring(diagnostic.Span.Start, diagnostic.Span.Length)));
        var compiled = Program.Compile(source, "native-bindings.calr",
            new CompilationOptions { StrictBindInference = strictInference, StatusWriter = TextWriter.Null });
        Assert.Equal(diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Span)),
            compiled.Diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.BindArrayToConcreteCollection)
                .Select(diagnostic => (diagnostic.Code, diagnostic.Span)));
    }

    [Theory]
    [InlineData("File.ReadAllText(path)")]
    [InlineData("System.IO.File.ReadAllText(path)")]
    [InlineData("Reader.Read(path)")]
    public void NativeCalls_ScalarReturnReachesRebindCategoryCheck(string expression)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              class Reader
                func Read static (string:path) → string
                  !
                  return path
              func Use (string:path) → int
                ! fs:r
                var int:x = 0
                var x = {{expression}}
                return x
            """;
        var diagnostic = Assert.Single(Validate(source));
        Assert.Equal(DiagnosticCode.BindRebindTypeMismatch, diagnostic.Code);
        Assert.Equal("var x = " + expression, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var compiled = Program.Compile(source, "native-rebind.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compiled.Diagnostics, actual =>
            actual.Code == diagnostic.Code && actual.Span == diagnostic.Span);
    }

    [Theory]
    [InlineData("Read(1)", true)]
    [InlineData("Read(\"list\")", false)]
    [InlineData("Read(selector: 1)", true)]
    [InlineData("Read(selector: \"list\")", false)]
    [InlineData("Read(true)", false)]
    [InlineData("Read(missing: 1)", false)]
    [InlineData("Read(ref number)", false)]
    [InlineData("Read<int>(1)", false)]
    public void NativeCalls_SourceOverloadsDoNotBorrowAnInapplicableReturn(string call, bool expected)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              class Reader
                func Read static (int:selector) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
                func Read static (string:selector) → List<string>
                  !
                  return new List<string>()
              func Use () → void
                ! fs:r
                var int:number = 1
                let List<string>:items = Reader.{{call}}
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Theory]
    [InlineData("Reader.Read(ref number)", true)]
    [InlineData("Reader.Read(number)", false)]
    [InlineData("Reader.Read(out number)", false)]
    [InlineData("Reader.Read(value: ref number)", true)]
    public void NativeCalls_RefSelectionRetainsTheArgumentModifier(string call, bool expected)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              class Reader
                func Read static (ref int:value) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
                func Read static (int:value) → List<string>
                  !
                  return new List<string>()
              func Use () → void
                ! fs:r
                var int:number = 1
                let List<string>:items = {{call}}
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Fact]
    public void NativeCalls_NamedArgumentsKeepTheCollectionParameterPosition()
    {
        const string source = """
            module NativeBindings NativeBindings
              class Reader
                func Take static (List<string>:items, string:label) → void
                  !
              func Use (string:path) → void
                ! fs:r
                Reader.Take(label: "lines", items: File.ReadAllLines(path))
            """;
        var diagnostic = Assert.Single(Validate(source));
        Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code);
        Assert.Equal("File.ReadAllLines(path)", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.Contains("Parameter 2 of 'Reader.Take'", diagnostic.Message);
    }

    [Fact]
    public void NativeCalls_EvaluatedReceiverIsStillScannedWithoutGuessingItsReturn()
    {
        const string source = """
            module NativeBindings NativeBindings
              func Take (List<string>:items) → object
                !
                return new object()
              func Use (string:path) → void
                ! fs:r
                let List<string>:items = Take(File.ReadAllLines(path)).Unknown()
            """;
        var diagnostic = Assert.Single(Validate(source));
        Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code);
        Assert.Equal("File.ReadAllLines(path)", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Theory]
    [InlineData("File.ReadAllLines()")]
    [InlineData("File.ReadAllLines(1)")]
    [InlineData("File.ReadAllLines(missing: path)")]
    [InlineData("File.ReadAllLines(ref path)")]
    [InlineData("File.ReadAllLines<int>(path)")]
    [InlineData("missing.ReadAllLines(path)")]
    public void NativeCalls_InvalidOrUnknownMetadataCallsStayUnknown(string expression)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              func Use (string:path) → void
                ! fs:r
                let List<string>:items = {{expression}}
            """;
        Assert.Empty(Validate(source));
    }

    [Theory]
    [InlineData("File.ReadAllLines(path)", "File")]
    [InlineData("System.IO.File.ReadAllLines(path)", "System")]
    public void NativeCalls_LexicalReceiverDoesNotBorrowABclReturn(string expression, string receiver)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              func Use (object:{{receiver}}, string:path) → void
                !
                let List<string>:items = {{expression}}
            """;
        Assert.Empty(Validate(source));
    }

    [Theory]
    [InlineData("using File = Other.Reader", "File.ReadAllLines(path)")]
    [InlineData("class File", "File.ReadAllLines(path)")]
    [InlineData("interface File\n    func Other () → void\n      !", "File.ReadAllLines(path)")]
    [InlineData("class System", "System.IO.File.ReadAllLines(path)")]
    public void NativeCalls_SourceOrAliasReceiverDoesNotBorrowABclReturn(string declaration, string expression)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              {{declaration}}
              func Use (string:path) → void
                !
                let List<string>:items = {{expression}}
            """;
        Assert.Empty(Validate(source));
    }

    [Fact]
    public void NativeCalls_AmbiguousSourceShadowDoesNotFallBackToBcl()
    {
        const string source = """
            module NativeBindings NativeBindings
              class File
                func ReadAllLines static (string:path) → string[]
                  !
                  return missing
                func ReadAllLines static (string:path) → List<string>
                  !
                  return new List<string>()
              func Use (string:path) → void
                !
                let List<string>:items = File.ReadAllLines(path)
            """;
        Assert.Empty(Validate(source));
    }

    [Fact]
    public void NativeCalls_ValueInvocationDoesNotBorrowASourceMethodReturn()
    {
        const string source = """
            module NativeBindings NativeBindings
              class Reader
                func Read static (string:path) → string[]
                  !
                  return missing
              func Use (string:path) → void
                !
                let Func<string,List<string>>:Read = static (string:value) =>
                  return new List<string>()
                let List<string>:items = Read.Invoke(path)
            """;
        Assert.Empty(Validate(source));
    }

    [Fact]
    public void NativeCalls_ValidArrayAndScalarUsesCompileNormally()
    {
        const string source = """
            module NativeBindings NativeBindings
              func Use (string:path) → string[]
                ! fs:r
                var string:text = ""
                var text = File.ReadAllText(path)
                let IEnumerable<string>:lines = File.ReadAllLines(path)
                return File.ReadAllLines(path)
            """;
        Assert.Empty(Validate(source));
        var result = Program.Compile(source, "native-valid.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData(true, "Array.empty()", true)]
    [InlineData(false, "Array.empty()", false)]
    [InlineData(true, "Array.empty<int>()", false)]
    public void NativeCalls_GenericFactoryStrictnessRetainsExplicitTypeArguments(
        bool strictInference, string expression, bool expected)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              func Use () → void
                !
                let items = {{expression}}
            """;
        var diagnostics = Validate(source, strictInference);
        Assert.Equal(expected, diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.BindCannotInferGenericReturn));
    }

    [Fact]
    public void NativeCalls_SourceFactoryAndTypedInstanceUseTheirOwnSignatures()
    {
        const string source = """
            module NativeBindings NativeBindings
              class Array
                func empty static () → int
                  !
                  return 0
              class Reader
                func Read (string:path) → string[]
                  ! fs:r
                  return File.ReadAllLines(path)
              func Use (Reader:reader, string:path) → void
                ! fs:r
                let value = Array.empty()
                let List<string>:items = reader.Read(path)
            """;
        var diagnostic = Assert.Single(Validate(source));
        Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code);
        Assert.StartsWith("Binding 'items'", diagnostic.Message);
    }

    [Theory]
    [InlineData("File.ReadAllText(1)", "")]
    [InlineData("File.ReadAllText(missing: path)", "")]
    [InlineData("File.ReadAllText(ref path)", "")]
    [InlineData("File.ReadAllText<int>(path)", "")]
    [InlineData("File.ReadAllText(path)", "object:File, ")]
    public void NativeCalls_InvalidOrValueShadowedScalarCallsDoNotInventACategory(
        string expression, string parameters)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              func Use ({{parameters}}string:path) → void
                ! fs:r
                var int:x = 0
                var x = {{expression}}
            """;
        Assert.Empty(Validate(source));
    }

    [Theory]
    [InlineData("Take(items: File.ReadAllLines(path))", true)]
    [InlineData("Take(missing: File.ReadAllLines(path))", false)]
    [InlineData("Take(ref File.ReadAllLines(path))", false)]
    [InlineData("Take<int>(File.ReadAllLines(path))", false)]
    public void NativeCalls_NestedArrayDoesNotRescueAnInvalidOuterSelection(string call, bool expected)
    {
        var source = $$"""
            module NativeBindings NativeBindings
              func Take (List<string>:items) → void
                !
              func Use (string:path) → void
                ! fs:r
                {{call}}
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Theory]
    [InlineData("Take(label: \"lines\", File.ReadAllLines(path))", false)]
    [InlineData("Take(label: \"lines\", items: File.ReadAllLines(path))", true)]
    [InlineData("Take(items: File.ReadAllLines(path), \"lines\")", true)]
    [InlineData("Take(File.ReadAllLines(path), label: \"lines\")", true)]
    public void NativeSelection_NamedArgumentsOnlyPermitInPositionPositionalContinuation(
        string call, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              func Take (List<string>:items, string:label) → void
                !
              func Use (string:path) → void
                ! fs:r
                {{call}}
            """;
        foreach (var strict in new[] { true, false })
        {
            var diagnostics = Validate(source, strict);
            Assert.Equal(expected ? 1 : 0, diagnostics.Count);
            Assert.All(diagnostics, diagnostic =>
            {
                Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code);
                Assert.Equal("File.ReadAllLines(path)",
                    source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
            });
        }
        if (!expected)
        {
            var result = Program.Compile(source, "named-order.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Equal(DiagnosticCode.NoMatchingOverload, Assert.Single(result.Diagnostics.Errors).Code);
        }
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("GetNumber()", false)]
    [InlineData("fixedNumber", false)]
    [InlineData("number", true)]
    [InlineData("(number)", true)]
    [InlineData("items[0]", true)]
    [InlineData("items[true]", false)]
    [InlineData("list[0]", false)]
    public void NativeSelection_RefOperandsRequireKnownWritableStorage(string operand, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (ref int:value) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
              func GetNumber () → int
                !
                return 1
              func Use (int[]:items, List<int>:list) → void
                ! fs:r
                var int:number = 1
                let int:fixedNumber = 1
                let List<string>:lines = Reader.Read(ref {{operand}})
            """;
        foreach (var strict in new[] { true, false })
        {
            var diagnostics = Validate(source, strict);
            Assert.Equal(expected ? 1 : 0, diagnostics.Count);
            Assert.All(diagnostics, diagnostic =>
                Assert.Equal(DiagnosticCode.BindArrayToConcreteCollection, diagnostic.Code));
        }
        if (operand is "1" or "GetNumber()")
        {
            var result = Program.Compile(source, "ref-operand.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.InvalidByReferenceOperand, diagnostic.Code);
            Assert.Equal(operand, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        }
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("number", true)]
    [InlineData("items[0]", true)]
    public void NativeSelection_OutOperandsRequireKnownWritableStorage(string operand, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (out int:value) → string[]
                  ! mut,fs:r
                  set value 1
                  return File.ReadAllLines("data.txt")
              func Use (int[]:items) → void
                ! mut,fs:r
                var int:number = 0
                let List<string>:lines = Reader.Read(out {{operand}})
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
        if (!expected)
        {
            var result = Program.Compile(source, "out-operand.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.Equal(DiagnosticCode.InvalidByReferenceOperand, Assert.Single(result.Diagnostics.Errors).Code);
        }
    }

    [Theory]
    [InlineData("number", true)]
    [InlineData("this.number", true)]
    [InlineData("fixedNumber", false)]
    [InlineData("this.fixedNumber", false)]
    public void NativeSelection_RefFieldsRetainReadonlyAndReceiverRules(string operand, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (ref int:value) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
              class Host
                field int:number = 1
                field int:fixedNumber readonly = 1
                func Use () → void
                  ! fs:r
                  let List<string>:lines = Reader.Read(ref {{operand}})
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NativeSelection_StaticBodiesRequireStaticFieldStorage(bool staticField, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (ref int:value) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
              class Host
                field int:number {{(staticField ? "static" : "")}} = 1
                func Use static () → void
                  ! fs:r
                  let List<string>:lines = Reader.Read(ref number)
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NativeSelection_ArrayFieldStorageRequiresAnAvailableReceiver(bool staticField, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (ref int:value) → string[]
                  ! fs:r
                  return File.ReadAllLines("data.txt")
              class Host
                field int[]:items {{(staticField ? "static" : "")}}
                func Use static () → void
                  ! fs:r
                  let List<string>:lines = Reader.Read(ref items[0])
            """;
        Assert.Equal(expected, HasArrayTrap(Validate(source)));
    }

    [Theory]
    [InlineData("ref", "local")]
    [InlineData("ref", "this.number")]
    [InlineData("ref", "items[0]")]
    [InlineData("out", "local")]
    [InlineData("out", "this.number")]
    [InlineData("out", "items[0]")]
    public void NativeSelection_PublicCompilationPreservesWritableByReferenceOperands(
        string modifier, string operand)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static ({{modifier}} int:value) → string[]
                  ! mut,fs:r
                  set value 1
                  return File.ReadAllLines("data.txt")
              class Host
                field int:number = 0
                func Use (int[]:items) → string[]
                  ! mut,fs:r
                  var int:local = 0
                  return Reader.Read({{modifier}} {{operand}})
            """;
        var result = Program.Compile(source, "writable-operand.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("number", true)]
    [InlineData("items[0]", true)]
    public void NativeSelection_RefApplicabilityAlsoGuardsScalarRebinds(string operand, bool expected)
    {
        var source = $$"""
            module NativeReview NativeReview
              class Reader
                func Read static (ref int:value) → string
                  !
                  return "text"
              func Use (int[]:items) → void
                !
                var int:number = 1
                var int:x = 0
                var x = Reader.Read(ref {{operand}})
            """;
        var diagnostics = Validate(source);
        Assert.Equal(expected ? 1 : 0, diagnostics.Count);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticCode.BindRebindTypeMismatch, diagnostic.Code));
    }

    [Fact]
    public void NativeSelection_FieldScalarShadowRemainsValid()
    {
        const string source = """
            module NativeReview NativeReview
              class Reader
                func ReadAllText public (string:path) → int
                  !
                  return 7
              class Host
                field Reader:File
                func Use public (string:path) → int
                  !
                  var int:x = 0
                  var x = File.ReadAllText(path)
                  return x
            """;
        Assert.Empty(Validate(source));
        var result = Program.Compile(source, "field-shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void DocsAndCompilerGuards_ShareOneBclApiList()
    {
        // The Calor0254 compiler check and the Calor1331 docs lint must recognize
        // the same array-returning APIs. They now read the same shared table; this
        // pins that so a future edit to one path can't silently diverge.
        foreach (var api in Calor.Compiler.Analysis.ArrayReturningBcl.Methods.Keys)
        {
            var doc = new DocFile(
                Calor.Compiler.SelfCheck.ExemplarCompileChecker.RelativePath,
                $"```\n§B{{x:List<str>}} §C{{{api}}} §A a §/C\n```\n");
            var findings = new List<Diagnostic>();
            Calor.Compiler.SelfCheck.ExemplarCompileChecker.CheckArrayBindingTraps(doc, findings);
            Assert.Contains(findings, f => f.Code == DiagnosticCode.DocDriftArrayBindingTrap);
        }
    }
}
