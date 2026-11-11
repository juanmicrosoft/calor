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
        "§M{Files}\n" +
        "  §F{CountLines:pub} (str:path) -> i32\n" +
        "    §E{fs:r}\n" +
        "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
        "    §R (len lines)\n";

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
            "§M{Files}\n" +
            "  §F{CountLines:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len lines)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayReturningUserFunction_BoundToList_IsRejected()
    {
        // Generality beyond the BCL list: a user function declared -> [str].
        var diags = Validate(
            "§M{Test}\n" +
            "  §F{GetItems:pub} (str:path) -> [str]\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{items:List<str>} §C{GetItems} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArrayBoundToCollectionInterface_IsAccepted()
    {
        // C# DOES allow array -> IEnumerable<T>/IList<T>/… — only the concrete
        // classes are rejected. The check must not over-fire on interfaces.
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Read:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{lines:IEnumerable<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ListFromNonArrayInitializer_IsAccepted()
    {
        // A List<str> bound to something that is not an array must not fire.
        var diags = Validate(
            "§M{Test}\n" +
            "  §F{Make:pub} () -> List<str>\n" +
            "    §R §C{MakeList} §/C\n" +
            "  §F{Use:pub} () -> i32\n" +
            "    §B{items:List<str>} §C{Make} §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayToList_IsRejected()
    {
        // §R returning an array from a List<str>-typed function (#724). This is the
        // signature variant the exemplar's own trap text warns about.
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Get:pub} (str:path) -> List<str>\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ReturnPosition_ArrayFormReturnType_IsAccepted()
    {
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Get:pub} (str:path) -> [str]\n" +
            "    §E{fs:r}\n" +
            "    §R §C{File.ReadAllLines} §A path §/C\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayToList_IsRejected()
    {
        // §ASSIGN reassigning an array into a List<str>-typed mutable variable (#724).
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Count:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~items:List<str>}\n" +
            "    §ASSIGN items §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignPosition_ArrayFormVariable_IsAccepted()
    {
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Count:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~items:[str]}\n" +
            "    §ASSIGN items §C{File.ReadAllLines} §A path §/C\n" +
            "    §R (len items)\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void TrapInsideForeachBody_IsRejected()
    {
        // Regression: the pass must recurse into §EACH bodies — file/dir iteration
        // is the exact idiom where this trap occurs (PR #726 review finding 1).
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Scan:pub} (str:dir) -> i32\n" +
            "    §E{fs:rw}\n" +
            "    §B{dirs:[str]} §C{Directory.GetDirectories} §A dir §/C\n" +
            "    §EACH{d} dirs\n" +
            "      §B{lines:List<str>} §C{File.ReadAllLines} §A d §/C\n" +
            "    §R INT:0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignToCollectionTypedField_IsRejected()
    {
        // §ASSIGN targeting a class field declared as a concrete collection.
        var diags = Validate(
            "§M{Files}\n" +
            "  §CL{Store:pub}\n" +
            "    §FLD{List<str>:items:priv}\n" +
            "    §MT{Load:pub} (str:path) -> void\n" +
            "      §E{fs:r}\n" +
            "      §ASSIGN items §C{File.ReadAllLines} §A path §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void AssignToArrayFormField_IsAccepted()
    {
        var diags = Validate(
            "§M{Files}\n" +
            "  §CL{Store:pub}\n" +
            "    §FLD{[str]:items:priv}\n" +
            "    §MT{Load:pub} (str:path) -> void\n" +
            "      §E{fs:r}\n" +
            "      §ASSIGN items §C{File.ReadAllLines} §A path §/C\n");

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
            "§M{Files}\n" +
            "  §F{Do:pub} (str:path, bool:flag) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{~x:[str]} §C{File.ReadAllLines} §A path §/C\n" +
            "    §IF (== flag true)\n" +
            "      §B{x:List<str>} §C{MakeList} §/C\n" +
            "    §ASSIGN x §C{File.ReadAllLines} §A path §/C\n" +
            "    §R INT:0\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_ArrayToCollectionParam_IsRejected()
    {
        // #725: an array argument passed where a user function declares a
        // List<str> parameter (the call nested inside a return expression).
        var diags = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} (List<str>:items) -> i32\n" +
            "    §R (len items)\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_ArrayFormParam_IsAccepted()
    {
        var diags = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} ([str]:items) -> i32\n" +
            "    §R (len items)\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_InCallStatement_IsRejected()
    {
        var diags = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} (List<str>:items) -> void\n" +
            "    §P (len items)\n" +
            "  §F{Use:pub} (str:path) -> void\n" +
            "    §E{fs:r}\n" +
            "    §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentPosition_OverloadArityDisambiguates()
    {
        // Two arity-based overloads of Take: the 1-arg takes List<str> (reject),
        // the 2-arg's first param is [str] (accept). The call resolves by arity.
        var oneArg = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} (List<str>:items) -> i32\n" +
            "    §R (len items)\n" +
            "  §F{Take:pub} ([str]:items, i32:n) -> i32\n" +
            "    §R n\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");
        Assert.True(HasArrayTrap(oneArg));

        var twoArg = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} (List<str>:items) -> i32\n" +
            "    §R (len items)\n" +
            "  §F{Take:pub} ([str]:items, i32:n) -> i32\n" +
            "    §R n\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §A INT:0 §/C\n");
        Assert.False(HasArrayTrap(twoArg));
    }

    [Fact]
    public void ReturnTypeLookup_UsesCallableArityInsteadOfBareName()
    {
        var diagnostics = Validate(
            "§M{T}\n" +
            "  §F{Load:pub} () -> [str]\n" +
            "    §R §C{File.ReadAllLines} §A STR:\"data.txt\" §/C\n" +
            "  §F{Load:pub} (i32:count) -> List<str>\n" +
            "    §R §NEW{List<str>} §/NEW\n" +
            "  §F{Use:pub} () -> void\n" +
            "    §B{items:List<str>} §C{Load} §/C\n");

        Assert.True(HasArrayTrap(diagnostics));
    }

    [Fact]
    public void ReturnTypeLookup_UsesExactSameArityOverloadParameterTypes()
    {
        var arraySelected = Validate(
            "§M{T}\n" +
            "  §F{Load:pub} (i32:selector) -> [str]\n" +
            "    §R §C{File.ReadAllLines} §A STR:\"data.txt\" §/C\n" +
            "  §F{Load:pub} (str:selector) -> List<str>\n" +
            "    §R §NEW{List<str>} §/NEW\n" +
            "  §F{Use:pub} () -> void\n" +
            "    §B{items:List<str>} §C{Load} §A INT:1 §/C\n");
        var listSelected = Validate(
            "§M{T}\n" +
            "  §F{Load:pub} (i32:selector) -> [str]\n" +
            "    §R §C{File.ReadAllLines} §A STR:\"data.txt\" §/C\n" +
            "  §F{Load:pub} (str:selector) -> List<str>\n" +
            "    §R §NEW{List<str>} §/NEW\n" +
            "  §F{Use:pub} () -> void\n" +
            "    §B{items:List<str>} §C{Load} §A STR:\"list\" §/C\n");

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
            "§M{T}\n" +
            "  §F{Take:pub} ([str]:items) -> i32\n" +
            "    §R (len items)\n" +
            "  §CL{Helper:pub}\n" +
            "    §MT{Take:pub} (List<str>:items) -> i32\n" +
            "      §R (len items)\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");

        Assert.False(HasArrayTrap(diags));
    }

    [Fact]
    public void IntraClassImplicitThisCall_ResolvesToTheMethod()
    {
        // The mirror of the above: inside the class, an unqualified §C{Take}
        // resolves to the class's own method (List<str> param) → still flagged.
        var diags = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} ([str]:items) -> i32\n" +   // free function with array param
            "    §R (len items)\n" +
            "  §CL{Helper:pub}\n" +
            "    §MT{Take:pub} (List<str>:items) -> i32\n" +
            "      §R (len items)\n" +
            "    §MT{Use:pub} (str:path) -> i32\n" +
            "      §E{fs:r}\n" +
            "      §R §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void ArgumentInIfConditionCall_IsScanned()
    {
        // Finding 3 (statement expression slots): a call in an §IF condition is a
        // scanned expression position, not a silently-skipped one.
        var diags = Validate(
            "§M{T}\n" +
            "  §F{Take:pub} (List<str>:items) -> bool\n" +
            "    §R (> (len items) 0)\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §IF §C{Take} §A §C{File.ReadAllLines} §A path §/C §/C\n" +
            "      §R 1\n" +
            "    §R 0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void HashSetFromArray_IsRejected()
    {
        // Generality on the collection side: not just List<T>.
        var diags = Validate(
            "§M{Files}\n" +
            "  §F{Read:pub} (str:dir) -> i32\n" +
            "    §E{fs:r}\n" +
            "    §B{names:HashSet<str>} §C{Directory.GetFiles} §A dir §/C\n" +
            "    §R INT:0\n");

        Assert.True(HasArrayTrap(diags));
    }

    [Fact]
    public void UserFunctionShadowingBclName_IsJudgedByItsRealReturnType()
    {
        // A user function named like a BCL method must win over the built-in
        // heuristic: here File.ReadAllLines is a user method returning List<str>
        // (not an array), so binding it to List<str> is correct — no false Calor0254.
        var diags = Validate(
            "§M{Shadow}\n" +
            "  §CL{File:pub}\n" +
            "    §MT{ReadAllLines:pub} (str:path) -> List<str>\n" +
            "      §R §C{MakeList} §/C\n" +
            "  §F{Use:pub} (str:path) -> i32\n" +
            "    §B{items:List<str>} §C{File.ReadAllLines} §A path §/C\n" +
            "    §R INT:0\n");

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
