using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Xunit;

namespace Calor.LanguageServer.Tests.Integration;

public sealed class QualifiedBaseResolutionTests
{
    [Theory]
    [InlineData("Caller", "Right.Base", "")]
    [InlineData("Caller", "Base", "  using Right\n")]
    [InlineData("Right", "Base", "")]
    public void BaseLookupRetainsNamespaceAndExactOverloadIdentity(
        string callerNamespace, string baseName, string imports)
    {
        var workspace = new WorkspaceState();
        var expected = Add(workspace, "right.calr", """
            module Shared Right
              class Base
                func Pick (int:value) → int
                  !
                  return value
                func Pick (string:value) → string
                  !
                  return value
            """);
        AddBase(workspace, "wrong.calr", "Wrong");
        var caller = AddCaller(workspace, callerNamespace, baseName, imports);
        var result = Resolve(workspace, caller);
        AssertOwner(result, expected, "Right.Base");
        Assert.Equal("int", result.Symbol!.Parameters[0].TypeName);
        Assert.NotEqual(expected.BoundModule!.Functions[1].SymbolId, result.Symbol.Id);
    }

    [Fact]
    public void EqualSimpleCallerNamesCannotSelectAnotherNamespacesBase()
    {
        var workspace = new WorkspaceState();
        Add(workspace, "a-decoy-caller.calr", """
            module Shared Wrong
              class Derived extends Wrong.Base
            """);
        AddBase(workspace, "wrong.calr", "Wrong");
        var expected = AddBase(workspace, "right.calr", "Right");
        var caller = AddCaller(workspace, "Caller", "Right.Base");
        AssertOwner(Resolve(workspace, caller), expected, "Right.Base");
    }

    [Fact]
    public void GlobalBaseQualificationDoesNotBindALexicalTypeShadow()
    {
        var workspace = new WorkspaceState();
        Add(workspace, "lexical-shadow.calr", """
            module Shared Caller
              class Right
                class Base
                  func Pick (int:value) → int
                    !
                    return value
            """);
        var expected = AddBase(workspace, "right.calr", "Right");
        var caller = AddCaller(workspace, "Caller", "global::Right.Base");
        AssertOwner(Resolve(workspace, caller), expected, "Right.Base");
    }

    [Fact]
    public void MissingQualifiedBaseCannotFallBackToAnotherNamespacesSimpleName()
    {
        var workspace = new WorkspaceState();
        AddBase(workspace, "wrong.calr", "Wrong");
        AssertUnresolved(Resolve(workspace, AddCaller(workspace, "Caller", "Missing.Base")));
    }

    [Fact]
    public void MissingQualifiedBaseCannotSelectAForeignNestedTypeWithoutAnImport()
    {
        var workspace = new WorkspaceState();
        Add(workspace, "wrong.calr", """
            module Shared Wrong
              class Missing
                class Base
                  func Pick (int:value) → int
                    !
                    return value
            """);
        AssertUnresolved(Resolve(workspace, AddCaller(workspace, "Caller", "Missing.Base")));
    }

    [Fact]
    public void AmbiguousImportedBasesDoNotSelectAConvenientMethod()
    {
        var workspace = new WorkspaceState();
        AddBase(workspace, "left.calr", "Left");
        AddBase(workspace, "right.calr", "Right");
        var caller = AddCaller(workspace, "Caller", "Base", "  using Left\n  using Right\n");
        AssertUnresolved(Resolve(workspace, caller));
    }

    [Fact]
    public void DuplicateQualifiedBasesDoNotSelectOneDeclaration()
    {
        var workspace = new WorkspaceState();
        AddBase(workspace, "right-one.calr", "Right");
        AddBase(workspace, "right-two.calr", "Right");
        AssertUnresolved(Resolve(workspace, AddCaller(workspace, "Caller", "Right.Base")));
    }

    [Fact]
    public void CompatiblePartialBasesRetainTheExactMethodOwner()
    {
        var workspace = new WorkspaceState();
        Add(workspace, "partial-fields.calr", """
            module Shared Right
              class Base partial
                field int:Value
            """);
        var expected = Add(workspace, "partial-methods.calr", """
            module Shared Right
              class Base partial
                func Pick (int:value) → int
                  !
                  return value
            """);
        AssertOwner(Resolve(workspace, AddCaller(workspace, "Caller", "Right.Base")), expected, "Right.Base");
    }

    [Theory]
    [InlineData("private", false)]
    [InlineData("protected", true)]
    public void QualifiedBaseLookupPreservesMemberVisibility(string visibility, bool accessible)
    {
        var workspace = new WorkspaceState();
        var expected = Add(workspace, "right.calr",
            $"module Shared Right\n  class Base\n    func Pick {visibility} (int:value) → int\n"
            + (visibility == "private" ? "" : "      !\n") + "      return value");
        AddBase(workspace, "wrong.calr", "Wrong");
        var result = Resolve(workspace, AddCaller(workspace, "Caller", "Right.Base"));
        if (accessible)
            AssertOwner(result, expected, "Right.Base");
        else
            AssertUnresolved(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedGenericIdentityDoesNotFlattenContainingTypeArity(bool includeActualBase)
    {
        var workspace = new WorkspaceState();
        Add(workspace, "right-decoy.calr", """
            module Shared Right
              class Outer<T,U>
                class Base<V>
                  func Pick (int:value) → int
                    !
                    return value
            """);
        var expected = includeActualBase ? Add(workspace, "right.calr", """
            module Shared Right
              class Outer<T>
                class Base<U>
                  func Pick (int:value) → int
                    !
                    return value
            """) : null;
        var caller = Add(workspace, "caller.calr", """
            module Shared Caller
              class Outer<T>
                class Derived extends Right.Outer<int>.Base<string>
                  func Use () → int
                    !
                    return base.Pick(1)
            """);
        var result = Resolve(workspace, caller);
        if (expected != null)
            AssertOwner(result, expected, "Right.Outer`1.Base`1");
        else
            AssertUnresolved(result);
    }

    [Fact]
    public void StatementBaseCallRetainsTheSameStructuralReceiverAndOwner()
    {
        var workspace = new WorkspaceState();
        var expected = AddBase(workspace, "right.calr", "Right");
        var caller = Add(workspace, "caller.calr", """
            module Shared Caller
              class Derived extends Right.Base
                func Use () → int
                  !
                  base.Pick(1)
                  return 0
            """);
        AssertOwner(Resolve(workspace, caller), expected, "Right.Base");
    }

    private static DocumentState AddBase(WorkspaceState workspace, string file, string namespaceName)
        => Add(workspace, file,
            $"module Shared {namespaceName}\n  class Base\n    func Pick (int:value) → int\n      !\n      return value");

    private static DocumentState AddCaller(
        WorkspaceState workspace, string namespaceName, string baseName, string imports = "")
        => Add(workspace, "caller.calr",
            $"module Shared {namespaceName}\n{imports}  class Derived extends {baseName}\n"
            + "    func Use () → int\n      !\n      return base.Pick(1)");

    private static DocumentState Add(WorkspaceState workspace, string file, string source)
    {
        var document = workspace.GetOrCreate(DocumentUri.From($"file:///{file}"), source);
        Assert.NotNull(document.Ast);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && (diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor083", StringComparison.Ordinal)));
        return document;
    }

    private static ProjectFunctionLocation Resolve(WorkspaceState workspace, DocumentState caller)
    {
        var offset = caller.Source.IndexOf("base.Pick", StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var call = SymbolFinder.FindBoundCallAtOffset(caller.BoundModule, offset);
        Assert.NotNull(call);
        var receiver = call switch
        {
            BoundCallExpression expression => expression.Receiver,
            BoundCallStatement statement => statement.Receiver,
            _ => null
        };
        Assert.IsType<BoundBaseExpression>(receiver);
        return workspace.ResolveProjectCall(caller, caller.Snapshot, call);
    }

    private static void AssertOwner(ProjectFunctionLocation result, DocumentState owner, string containingType)
    {
        Assert.Same(owner, result.Doc);
        Assert.NotNull(result.Symbol);
        Assert.Equal($"{containingType}.Pick", result.Symbol.Name);
        Assert.Equal(containingType, result.Symbol.ContainingTypeName);
        var expected = owner.BoundModule!.Functions.Single(function =>
            function.Symbol.Name == $"{containingType}.Pick" && function.Symbol.Parameters[0].TypeName == "int");
        Assert.Same(expected.Symbol, result.Symbol);
        Assert.Equal(expected.SymbolId, result.Symbol.Id);
    }

    private static void AssertUnresolved(ProjectFunctionLocation result)
    {
        Assert.Null(result.Doc);
        Assert.Null(result.Snapshot);
        Assert.Null(result.Symbol);
    }
}
