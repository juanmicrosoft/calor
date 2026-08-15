using Calor.Compiler.Binding;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Xunit;

namespace Calor.LanguageServer.Tests.Integration;

/// <summary>
/// Tests for cross-file resolution functionality - go-to-definition and completions
/// across multiple open documents.
/// </summary>
public class CrossFileResolutionTests
{
    [Fact]
    public void SameStructuralIdsInDifferentFiles_RemainDistinct()
    {
        var workspace = new WorkspaceState();
        var firstUri = DocumentUri.FromFileSystemPath("/workspace/first.calr");
        var secondUri = DocumentUri.FromFileSystemPath("/workspace/second.calr");
        var source = """
            §M{m001:Shared}
              §F{f001:Same:pub}
                §O{void}
            """;

        var first = workspace.GetOrCreate(firstUri, source);
        var second = workspace.GetOrCreate(secondUri, source);
        var firstSymbol = Assert.Single(first.BoundModule!.Functions).Symbol;
        var secondSymbol = Assert.Single(second.BoundModule!.Functions).Symbol;

        Assert.NotEqual(firstSymbol.Id, secondSymbol.Id);
        Assert.Same(firstSymbol, workspace.FindBoundSymbol(firstSymbol.Id).Symbol);
        Assert.Same(secondSymbol, workspace.FindBoundSymbol(secondSymbol.Id).Symbol);
    }

    [Fact]
    public void ResolveProjectCall_UsesExactOverloadSymbolId()
    {
        var workspace = new WorkspaceState();
        var definitions = """
            §M{m001:Utils}
              §F{f001:Pick:pub}
                §I{i32:value}
                §O{i32}
                §R value
              §F{f002:Pick:pub}
                §I{str:value}
                §O{str}
                §R value
            """;
        var use = """
            §M{m002:Main}
              §F{f003:Run:pub}
                §O{i32}
                §R §C{Pick} §A INT:1 §/C
            """;
        var definitionsState = workspace.GetOrCreate(
            DocumentUri.From("file:///utils.calr"),
            definitions);
        var useState = workspace.GetOrCreate(
            DocumentUri.From("file:///main.calr"),
            use);

        Assert.NotNull(definitionsState.BoundModule);
        Assert.NotNull(useState.BoundModule);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Pick", StringComparison.Ordinal));
        var resolved = workspace.ResolveProjectCall(call);

        Assert.NotNull(resolved.Symbol);
        Assert.Equal("INT", resolved.Symbol.Parameters[0].TypeName);
        Assert.Equal(
            definitionsState.BoundModule.Functions[0].SymbolId,
            resolved.Symbol.Id);
        Assert.NotEqual(
            definitionsState.BoundModule.Functions[1].SymbolId,
            resolved.Symbol.Id);

        var references = workspace.FindProjectFunctionReferences(
                resolved.Symbol,
                includeDeclaration: true)
            .ToArray();
        Assert.Contains(references, reference => reference.Doc == definitionsState);
        Assert.Contains(references, reference => reference.Doc == useState);
    }

    [Fact]
    public void ResolveProjectCall_DoesNotNameResolveIncompatibleOverload()
    {
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(
            DocumentUri.From("file:///utils.calr"),
            """
            §M{m001:Utils}
              §F{f001:Pick:pub}
                §I{str:value}
                §O{str}
                §R value
            """);
        var use = """
            §M{m002:Main}
              §F{f002:Run:pub}
                §O{i32}
                §R §C{Pick} §A INT:1 §/C
            """;
        var useState = workspace.GetOrCreate(
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Pick", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(call);

        Assert.Null(resolved.Doc);
        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void EquivalentWorkspaceRoots_ProducePortableStableSymbolIds()
    {
        const string source = """
            §M{m001:Shared}
              §F{f001:Same:pub}
                §O{void}
            """;
        var first = new WorkspaceState("/checkout/one");
        var second = new WorkspaceState("/different/checkout");

        var firstSymbol = Assert.Single(first.GetOrCreate(
            DocumentUri.FromFileSystemPath("/checkout/one/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var secondSymbol = Assert.Single(second.GetOrCreate(
            DocumentUri.FromFileSystemPath("/different/checkout/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;

        Assert.Equal(firstSymbol.Id, secondSymbol.Id);
        Assert.Contains("workspace%3Aroot0%3Asrc%2Fshared.calr", firstSymbol.Id.Value);
        Assert.DoesNotContain("/checkout/one", firstSymbol.Id.Value);
    }

    [Fact]
    public void MultipleWorkspaceRoots_DisambiguateIdenticalRelativePathsPortably()
    {
        const string source = """
            §M{m001:Shared}
              §F{f001:Same:pub}
                §O{void}
            """;
        var firstWorkspace = new WorkspaceState();
        firstWorkspace.ConfigureWorkspaceRoots(
        [
            new Uri("file:///checkout/first"),
            new Uri("file:///checkout/second"),
        ]);
        var secondWorkspace = new WorkspaceState();
        secondWorkspace.ConfigureWorkspaceRoots(
        [
            new Uri("file:///relocated/first"),
            new Uri("file:///relocated/second"),
        ]);

        var firstRootSymbol = Assert.Single(firstWorkspace.GetOrCreate(
            DocumentUri.FromFileSystemPath("/checkout/first/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var secondRootSymbol = Assert.Single(firstWorkspace.GetOrCreate(
            DocumentUri.FromFileSystemPath("/checkout/second/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var relocatedFirst = Assert.Single(secondWorkspace.GetOrCreate(
            DocumentUri.FromFileSystemPath("/relocated/first/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var relocatedSecond = Assert.Single(secondWorkspace.GetOrCreate(
            DocumentUri.FromFileSystemPath("/relocated/second/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;

        Assert.NotEqual(firstRootSymbol.Id, secondRootSymbol.Id);
        Assert.Equal(firstRootSymbol.Id, relocatedFirst.Id);
        Assert.Equal(secondRootSymbol.Id, relocatedSecond.Id);
        Assert.Contains("workspace%3Aroot0%3Asrc%2Fshared.calr", firstRootSymbol.Id.Value);
        Assert.Contains("workspace%3Aroot1%3Asrc%2Fshared.calr", secondRootSymbol.Id.Value);
        Assert.DoesNotContain("/checkout/", firstRootSymbol.Id.Value);
        Assert.DoesNotContain("/relocated/", relocatedFirst.Id.Value);
    }

    [Fact]
    public void ResolveProjectCall_ExcludesPrivateFunctionsFromOtherDocuments()
    {
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(
            DocumentUri.From("file:///private.calr"),
            """
            §M{m001:Utils}
              §F{f001:Hidden:pri} () -> i32
                §R 1
            """);
        var use = """
            §M{m002:Main}
              §F{f002:Run:pub} () -> i32
                §R §C{Hidden} §/C
            """;
        var useState = workspace.GetOrCreate(
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Hidden", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(useState, useState.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_DoesNotExposeUnqualifiedClassMethods()
    {
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(
            DocumentUri.From("file:///service.calr"),
            """
            §M{m001:Service}
              §CL{c1:Worker:pub}
                §MT{m1:Run:pub} () -> i32
                  §R 1
            """);
        var use = """
            §M{m002:Main}
              §F{f002:RunMain:pub} () -> i32
                §R §C{Run} §/C
            """;
        var useState = workspace.GetOrCreate(
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.LastIndexOf("Run", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(useState, useState.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_RejectsPrivateMemberOutsideContainingClass()
    {
        var workspace = new WorkspaceState();
        var source = """
            §M{m001:Main}
              §CL{c1:Worker:pub}
                §MT{m1:Hidden:pri} () -> i32
                  §R 1
              §F{f2:Run:pub} () -> i32
                §R §C{Worker.Hidden} §/C
            """;
        var state = workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.IndexOf("Worker.Hidden", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_AllowsPrivateMemberInsideContainingClass()
    {
        var workspace = new WorkspaceState();
        var source = """
            §M{m001:Main}
              §CL{c1:Worker:pub}
                §MT{m1:Hidden:pri} () -> i32
                  §R 1
                §MT{m2:Run:pub} () -> i32
                  §R §C{Hidden} §/C
            """;
        var state = workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.LastIndexOf("Hidden", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Equal("Worker.Hidden", resolved.Symbol?.Name);
    }

    [Fact]
    public void ResolveProjectCall_UsesCrossFileBaseClassIdentity()
    {
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(
            DocumentUri.From("file:///base.calr"),
            """
            §M{m001:BaseModule}
              §CL{c1:Base:pub}
                §MT{m1:Pick:pub} (i32:value) -> i32
                  §R value
            """);
        var source = """
            §M{m002:DerivedModule}
              §CL{c2:Derived:Base:pub}
                §MT{m2:Use:pub} () -> i32
                  §R §C{base.Pick} §A INT:1 §/C
            """;
        var state = workspace.GetOrCreate(DocumentUri.From("file:///derived.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.IndexOf("base.Pick", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Equal("Base.Pick", resolved.Symbol?.Name);
    }

    [Fact]
    public void NewWithoutConstructor_ResolvesToClassDeclaration()
    {
        var workspace = new WorkspaceState();
        var classState = workspace.GetOrCreate(
            DocumentUri.From("file:///model.calr"),
            """
            §M{m001:Models}
              §CL{c1:Widget:pub}
            """);
        var use = """
            §M{m002:Main}
              §F{f2:Make:pub} () -> Widget
                §R §NEW{Widget} §/NEW
            """;
        var useState = workspace.GetOrCreate(
            DocumentUri.From("file:///main.calr"),
            use);
        var creation = Assert.IsType<BoundNewExpression>(
            SymbolFinder.FindBoundCallAtOffset(
                useState.BoundModule,
                use.LastIndexOf("Widget", StringComparison.Ordinal)));

        Assert.Null(creation.ResolvedConstructor);
        var resolved = workspace.ResolveProjectType(useState, useState.Snapshot, creation);
        var type = Assert.IsType<TypeSymbol>(resolved.Symbol);
        Assert.Equal("Widget", type.Name);
        Assert.Same(classState, resolved.Doc);
        Assert.Equal(
            "Widget",
            classState.Source.Substring(type.DeclarationSpan.Start, type.DeclarationSpan.Length));
    }

    [Fact]
    public void ProjectReferenceOwnership_UsesSymbolIdAcrossReanalysis()
    {
        var workspace = new WorkspaceState();
        var definitionsUri = DocumentUri.From("file:///utils.calr");
        var definitions = workspace.GetOrCreate(
            definitionsUri,
            """
            §M{m001:Utils}
              §F{f001:Pick:pub} () -> i32
                §R 1
            """);
        var oldSymbol = Assert.Single(definitions.BoundModule!.Functions).Symbol;
        workspace.Update(
            definitionsUri,
            """
            §M{m001:Utils}
              §F{before:Before:pub} () -> i32
                §R 0
              §F{f001:Pick:pub} () -> i32
                §B{value:i32} 1
                §R value
            """,
            version: 2);
        var use = """
            §M{m002:Main}
              §F{f002:Run:pub} () -> i32
                §R §C{Pick} §/C
            """;
        var useState = workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), use);

        var references = workspace.FindProjectFunctionReferences(oldSymbol, includeDeclaration: true);

        var declaration = Assert.Single(references.Where(reference =>
            reference.Doc.Uri == definitions.Uri));
        Assert.Equal(
            "Pick",
            declaration.Snapshot.Source.Substring(
                declaration.Span.Start,
                declaration.Span.Length));
        Assert.Contains(references, reference => reference.Doc == useState);
    }

    [Fact]
    public void DocumentSnapshots_RemainStableAndRejectStaleReplacement()
    {
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///state.calr");
        var state = workspace.GetOrCreate(
            uri,
            "§M{m1:State}\n  §F{f1:Value:pub} () -> i32\n    §R 1\n",
            version: 1);
        var captured = state.Snapshot;
        workspace.Update(
            uri,
            "§M{m1:State}\n  §F{f1:Value:pub} () -> i32\n    §R 2\n",
            version: 2);
        workspace.Update(
            uri,
            "§M{m1:State}\n  §F{f1:Value:pub} () -> i32\n    §R 0\n",
            version: 1);

        Assert.Equal(1, captured.Version);
        Assert.Contains("§R 1", captured.Source);
        Assert.Equal(2, state.Version);
        Assert.Contains("§R 2", state.Source);
    }

    #region WorkspaceState Tests

    [Fact]
    public void FindDefinitionAcrossFiles_Function_FindsInOtherDocument()
    {
        var workspace = new WorkspaceState();

        // First document defines a function
        var source1 = """
            §M{m001:Utils}
              §F{f001:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R a + b
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///utils.calr"), source1);

        // Second document uses the function
        var source2 = """
            §M{m002:Main}
              §F{f002:Test}
                §O{i32}
                §B{result} §C{Add} 1 2 §/C
                §R result
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), source2);

        // Find the function definition
        var (doc, node) = workspace.FindDefinitionAcrossFiles("Add");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("utils.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_Class_FindsInOtherDocument()
    {
        var workspace = new WorkspaceState();

        // First document defines a class
        var source1 = """
            §M{m001:Models}
              §CL{c001:Person}
                §FLD{str:name}
                §FLD{i32:age}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source1);

        // Second document uses the class
        var source2 = """
            §M{m002:Main}
              §F{f001:CreatePerson}
                §O{Person}
                §R §NEW Person
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), source2);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("Person");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("models.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_Interface_FindsInOtherDocument()
    {
        var workspace = new WorkspaceState();

        // First document defines an interface
        var source1 = """
            §M{m001:Interfaces}
              §IFACE{i001:IShape}
                §MT{m001:GetArea}
                  §O{f64}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///interfaces.calr"), source1);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("IShape");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("interfaces.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_Enum_FindsInOtherDocument()
    {
        var workspace = new WorkspaceState();

        // First document defines an enum
        var source1 = """
            §M{m001:Types}
              §EN{e001:Color}
              §EM{Red}
              §EM{Green}
              §EM{Blue}
              §/EN{e001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///types.calr"), source1);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("Color");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("types.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_NotFound_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §F{f001:Test}
                §R 42
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("NonExistent");

        Assert.Null(doc);
        Assert.Null(node);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_LocalFunction_FindsInSameDocument()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §F{f001:Helper}
                §R 42
              §F{f002:Main}
                §B{x} §C{Helper} §/C
                §R x
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("Helper");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("test.calr", doc.Uri.LocalPath);
    }

    #endregion

    #region GetAllPublicSymbols Tests

    [Fact]
    public void GetAllPublicSymbols_ReturnsPublicFunctions()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §F{f001:PublicFunc:pub}
                §R 42
              §F{f002:PrivateFunc:priv}
                §R 0
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "PublicFunc" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "PrivateFunc");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsClasses()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §CL{c001:MyClass}
                §FLD{i32:value}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "MyClass" && s.Kind == "class");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsInterfaces()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §IFACE{i001:IMyInterface}
                §MT{m001:DoSomething}
                  §O{void}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "IMyInterface" && s.Kind == "interface");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsEnums()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §EN{e001:Status}
              §EM{Active}
              §EM{Inactive}
              §/EN{e001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "Status" && s.Kind == "enum");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsDelegates()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §DEL{d001:Callback}
              §I{i32:value}
              §O{void}
              §/DEL{d001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "Callback" && s.Kind == "delegate");
    }

    [Fact]
    public void GetAllPublicSymbols_MultipleDocuments_ReturnsAll()
    {
        var workspace = new WorkspaceState();

        var source1 = """
            §M{m001:File1}
              §F{f001:FuncA:pub}
                §R 1
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file1.calr"), source1);

        var source2 = """
            §M{m002:File2}
              §F{f001:FuncB:pub}
                §R 2
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file2.calr"), source2);

        var source3 = """
            §M{m003:File3}
              §CL{c001:ClassC}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file3.calr"), source3);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "FuncA");
        Assert.Contains(symbols, s => s.Name == "FuncB");
        Assert.Contains(symbols, s => s.Name == "ClassC");
    }

    #endregion

    #region Cross-File Completion Tests

    [Fact]
    public void Completions_IncludeTypesFromOtherDocuments()
    {
        var workspace = new WorkspaceState();

        // First document defines a class
        var source1 = """
            §M{m001:Models}
              §CL{c001:Customer}
                §FLD{str:name}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source1);

        // Second document should have access to Customer type in completions
        var source2 = """
            §M{m002:Service}
              §F{f001:GetCustomer}
                §O{i32}
                §R 0
            """;
        var doc2 = workspace.GetOrCreate(DocumentUri.From("file:///service.calr"), source2);

        // Get all public symbols (simulating what CompletionHandler does)
        var symbols = workspace.GetAllPublicSymbols()
            .Where(s => s.Doc.Uri != doc2.Uri)
            .ToList();

        Assert.Contains(symbols, s => s.Name == "Customer" && s.Kind == "class");
    }

    [Fact]
    public void Completions_IncludeFunctionsFromOtherDocuments()
    {
        var workspace = new WorkspaceState();

        // First document defines a utility function
        var source1 = """
            §M{m001:Utils}
              §F{f001:Calculate:pub}
                §I{i32:x}
                §O{i32}
                §R x * 2
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///utils.calr"), source1);

        // Second document should see Calculate in completions
        var source2 = """
            §M{m002:Main}
              §F{f001:Test}
                §O{i32}
                §R 0
            """;
        var doc2 = workspace.GetOrCreate(DocumentUri.From("file:///main.calr"), source2);

        var symbols = workspace.GetAllPublicSymbols()
            .Where(s => s.Doc.Uri != doc2.Uri)
            .ToList();

        Assert.Contains(symbols, s => s.Name == "Calculate" && s.Kind == "function");
    }

    #endregion

    #region Cross-File Type Resolution Tests

    [Fact]
    public void TypeResolution_ClassFromOtherFile_ResolvesFields()
    {
        var workspace = new WorkspaceState();

        // First document defines a class
        var source1 = """
            §M{m001:Models}
              §CL{c001:Address}
                §FLD{str:street}
                §FLD{str:city}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source1);

        // Verify class can be found in workspace
        var (doc, node) = workspace.FindDefinitionAcrossFiles("Address");

        Assert.NotNull(doc);
        Assert.NotNull(node);

        // Verify the class has the expected fields (this would be used by CompletionHandler)
        var addressClass = doc.Ast?.Classes.FirstOrDefault(c => c.Name == "Address");
        Assert.NotNull(addressClass);
        Assert.Equal(2, addressClass.Fields.Count);
        Assert.Contains(addressClass.Fields, f => f.Name == "street");
        Assert.Contains(addressClass.Fields, f => f.Name == "city");
    }

    [Fact]
    public void TypeResolution_InheritedClass_ResolvesBaseClassMembers()
    {
        var workspace = new WorkspaceState();

        // First document defines a base class
        var source1 = """
            §M{m001:Base}
              §CL{c001:Animal}
                §FLD{str:name}
                §MT{m001:Speak}
                  §O{str}
                  §R "..."
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///base.calr"), source1);

        // Second document defines a derived class
        var source2 = """
            §M{m002:Derived}
              §CL{c001:Dog}
                §EXT{Animal}
                §FLD{str:breed}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///derived.calr"), source2);

        // Verify we can find Dog class
        var (dogDoc, dogNode) = workspace.FindDefinitionAcrossFiles("Dog");
        Assert.NotNull(dogDoc);

        var dogClass = dogDoc.Ast?.Classes.FirstOrDefault(c => c.Name == "Dog");
        Assert.NotNull(dogClass);
        Assert.Equal("Animal", dogClass.BaseClass);

        // Verify we can find the base class too
        var (animalDoc, _) = workspace.FindDefinitionAcrossFiles("Animal");
        Assert.NotNull(animalDoc);

        var animalClass = animalDoc.Ast?.Classes.FirstOrDefault(c => c.Name == "Animal");
        Assert.NotNull(animalClass);
        Assert.Contains(animalClass.Fields, f => f.Name == "name");
    }

    #endregion

    #region Document Management Tests

    [Fact]
    public void DocumentRemoved_SymbolsNoLongerAvailable()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Test}
              §F{f001:ToRemove:pub}
                §R 42
            """;
        var uri = DocumentUri.From("file:///toremove.calr");
        workspace.GetOrCreate(uri, source);

        // Verify symbol is found initially
        var (doc1, node1) = workspace.FindDefinitionAcrossFiles("ToRemove");
        Assert.NotNull(doc1);

        // Remove the document
        workspace.Remove(uri);

        // Verify symbol is no longer found
        var (doc2, node2) = workspace.FindDefinitionAcrossFiles("ToRemove");
        Assert.Null(doc2);
    }

    [Fact]
    public void DocumentUpdated_SymbolsReflectChanges()
    {
        var workspace = new WorkspaceState();

        var uri = DocumentUri.From("file:///changing.calr");

        // Initial source
        var source1 = """
            §M{m001:Test}
              §F{f001:OldName:pub}
                §R 42
            """;
        workspace.GetOrCreate(uri, source1);

        // Verify old name is found
        var (doc1, _) = workspace.FindDefinitionAcrossFiles("OldName");
        Assert.NotNull(doc1);

        // Update the document
        var source2 = """
            §M{m001:Test}
              §F{f001:NewName:pub}
                §R 42
            """;
        workspace.Update(uri, source2, 2);

        // Verify old name is no longer found
        var (doc2, _) = workspace.FindDefinitionAcrossFiles("OldName");
        Assert.Null(doc2);

        // Verify new name is found
        var (doc3, _) = workspace.FindDefinitionAcrossFiles("NewName");
        Assert.NotNull(doc3);
    }

    #endregion

    #region Cross-File Member Access Tests

    [Fact]
    public void FindMemberAcrossFiles_ClassField_FindsField()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
              §CL{c001:Person}
                §FLD{str:name}
                §FLD{i32:age}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "name");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("models.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindMemberAcrossFiles_ClassMethod_FindsMethod()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
              §CL{c001:Person}
                §FLD{str:name}
                §MT{m001:GetName}
                  §O{str}
                  §R name
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "GetName");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_ClassProperty_FindsProperty()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
              §CL{c001:Person}
                §PROP{str:FullName}
                §GET
                §R "test"
                §/GET
                §/PROP
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "FullName");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_EnumMember_FindsMember()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Types}
              §EN{e001:Color}
              §EM{Red}
              §EM{Green}
              §EM{Blue}
              §/EN{e001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///types.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Color", "Red");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_InterfaceMethod_FindsMethod()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Interfaces}
              §IFACE{i001:IShape}
                §MT{m001:GetArea}
                  §O{f64}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///interfaces.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("IShape", "GetArea");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_InheritedField_FindsFromBaseClass()
    {
        var workspace = new WorkspaceState();

        // Base class in one file
        var source1 = """
            §M{m001:Base}
              §CL{c001:Animal}
                §FLD{str:name}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///base.calr"), source1);

        // Derived class in another file
        var source2 = """
            §M{m002:Derived}
              §CL{c001:Dog}
                §EXT{Animal}
                §FLD{str:breed}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///derived.calr"), source2);

        // Looking up "name" on Dog should find it in Animal
        var (doc, node) = workspace.FindMemberAcrossFiles("Dog", "name");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("base.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public async Task DirectCrossFileInheritanceCycle_IsDeterministicAndTraversalTerminatesAsync()
    {
        var workspace = new WorkspaceState();
        var aUri = DocumentUri.From("file:///cycle-a.calr");
        var bUri = DocumentUri.From("file:///cycle-b.calr");
        workspace.GetOrCreate(aUri, """
            §M{m001:CycleA}
              §CL{c001:A}
                §EXT{B}
            """);
        workspace.GetOrCreate(bUri, """
            §M{m002:CycleB}
              §CL{c002:B}
                §EXT{A}
            """);

        var snapshot = workspace.CaptureSnapshot();
        var aDiagnostic = Assert.Single(
            workspace.GetDiagnostics(snapshot, snapshot.GetDocument(aUri)!)
                .Where(diagnostic =>
                    diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
        var bDiagnostic = Assert.Single(
            workspace.GetDiagnostics(snapshot, snapshot.GetDocument(bUri)!)
                .Where(diagnostic =>
                    diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));

        Assert.Equal("Inheritance cycle detected: A -> B -> A.", aDiagnostic.Message);
        Assert.Equal(aDiagnostic.Message, bDiagnostic.Message);
        var result = await Task.Run(() =>
                workspace.FindMemberAcrossFiles("A", "missing"))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(result.Doc);
        Assert.Null(result.Node);
    }

    [Fact]
    public async Task IndirectCrossFileInheritanceCycle_IsCanonicalAcrossAllFilesAsync()
    {
        var workspace = new WorkspaceState();
        var aUri = DocumentUri.From("file:///indirect-a.calr");
        var bUri = DocumentUri.From("file:///indirect-b.calr");
        var cUri = DocumentUri.From("file:///indirect-c.calr");
        workspace.GetOrCreate(aUri, """
            §M{m001:CycleA}
              §CL{c001:A}
                §EXT{B}
            """);
        workspace.GetOrCreate(bUri, """
            §M{m002:CycleB}
              §CL{c002:B}
                §EXT{C}
            """);
        workspace.GetOrCreate(cUri, """
            §M{m003:CycleC}
              §CL{c003:C}
                §EXT{A}
            """);

        var snapshot = workspace.CaptureSnapshot();
        var messages = new[] { aUri, bUri, cUri }
            .Select(uri => Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(diagnostic =>
                        diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle))
                .Message)
            .ToArray();

        Assert.All(messages, message =>
            Assert.Equal(
                "Inheritance cycle detected: A -> B -> C -> A.",
                message));
        var result = await Task.Run(() =>
                workspace.FindMemberAcrossFiles("B", "missing"))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(result.Doc);
        Assert.Null(result.Node);
    }

    [Fact]
    public void QualifiedCrossFileInheritanceCycle_ResolvesModuleQualifiedBases()
    {
        var workspace = new WorkspaceState();
        var aUri = DocumentUri.From("file:///qualified-cycle-a.calr");
        var bUri = DocumentUri.From("file:///qualified-cycle-b.calr");
        workspace.GetOrCreate(aUri, """
            §M{m001:CycleA}
              §CL{c001:A}
                §EXT{CycleB.B}
            """);
        workspace.GetOrCreate(bUri, """
            §M{m002:CycleB}
              §CL{c002:B}
                §EXT{CycleA.A}
            """);

        var snapshot = workspace.CaptureSnapshot();
        var diagnostics = new[] { aUri, bUri }
            .Select(uri => Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(diagnostic =>
                        diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)))
            .ToArray();

        Assert.All(diagnostics, diagnostic =>
            Assert.Equal("Inheritance cycle detected: A -> B -> A.", diagnostic.Message));
    }

    [Fact]
    public void DistinctSameDisplayInheritanceCycles_AreNotDeduplicatedByMessage()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///left-a.calr")] = """
                §M{m001:LeftA}
                  §CL{c001:A}
                    §EXT{LeftB.B}
                """,
            [DocumentUri.From("file:///left-b.calr")] = """
                §M{m002:LeftB}
                  §CL{c002:B}
                    §EXT{LeftA.A}
                """,
            [DocumentUri.From("file:///right-a.calr")] = """
                §M{m003:RightA}
                  §CL{c003:A}
                    §EXT{RightB.B}
                """,
            [DocumentUri.From("file:///right-b.calr")] = """
                §M{m004:RightB}
                  §CL{c004:B}
                    §EXT{RightA.A}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        var diagnostics = sources.Keys
            .Select(uri => Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(diagnostic =>
                        diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)))
            .ToArray();

        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal("Inheritance cycle detected: A -> B -> A.", diagnostic.Message));
    }

    [Fact]
    public void DirectCrossFilePartialInheritanceCycle_DiagnosesEveryPart()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///partial-a-base.calr")] = """
                §M{ma1:CycleA}
                  §CL{a1:A:pub:partial}
                    §EXT{CycleB.B}
                """,
            [DocumentUri.From("file:///partial-a-members.calr")] = """
                §M{ma2:CycleA}
                  §CL{a2:A:pub:partial}
                    §FLD{i32:value}
                """,
            [DocumentUri.From("file:///partial-b-base.calr")] = """
                §M{mb1:CycleB}
                  §CL{b1:B:pub:partial}
                    §EXT{CycleA.A}
                """,
            [DocumentUri.From("file:///partial-b-members.calr")] = """
                §M{mb2:CycleB}
                  §CL{b2:B:pub:partial}
                    §FLD{i32:value}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        var diagnostics = sources.Keys
            .Select(uri => Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(diagnostic =>
                        diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)))
            .ToArray();

        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal("Inheritance cycle detected: A -> B -> A.", diagnostic.Message));
    }

    [Fact]
    public void IndirectCrossFilePartialInheritanceCycle_DiagnosesEveryPart()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///partial-indirect-a-base.calr")] = """
                §M{ma1:CycleA}
                  §CL{a1:A:pub:partial}
                    §EXT{CycleB.B}
                """,
            [DocumentUri.From("file:///partial-indirect-a-part.calr")] = """
                §M{ma2:CycleA}
                  §CL{a2:A:pub:partial}
                """,
            [DocumentUri.From("file:///partial-indirect-b-base.calr")] = """
                §M{mb1:CycleB}
                  §CL{b1:B:pub:partial}
                    §EXT{CycleC.C}
                """,
            [DocumentUri.From("file:///partial-indirect-b-part.calr")] = """
                §M{mb2:CycleB}
                  §CL{b2:B:pub:partial}
                """,
            [DocumentUri.From("file:///partial-indirect-c-base.calr")] = """
                §M{mc1:CycleC}
                  §CL{c1:C:pub:partial}
                    §EXT{CycleA.A}
                """,
            [DocumentUri.From("file:///partial-indirect-c-part.calr")] = """
                §M{mc2:CycleC}
                  §CL{c2:C:pub:partial}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        var diagnostics = sources.Keys
            .Select(uri => Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(diagnostic =>
                        diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)))
            .ToArray();

        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(
                "Inheritance cycle detected: A -> B -> C -> A.",
                diagnostic.Message));
    }

    [Fact]
    public void NonPartialDuplicateDeclarations_AreNotMergedIntoInheritanceGraph()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///duplicate-a-one.calr")] = """
                §M{ma1:Duplicate}
                  §CL{a1:A:pub}
                    §EXT{Other.B}
                """,
            [DocumentUri.From("file:///duplicate-a-two.calr")] = """
                §M{ma2:Duplicate}
                  §CL{a2:A:pub}
                """,
            [DocumentUri.From("file:///duplicate-b.calr")] = """
                §M{mb1:Other}
                  §CL{b1:B:pub}
                    §EXT{Duplicate.A}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        Assert.All(sources.Keys, uri =>
            Assert.DoesNotContain(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!),
                diagnostic =>
                    diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
    }

    [Fact]
    public void GenericInheritanceCycles_ResolveMatchingArityOnly()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///generic-foo-one.calr")] = """
                §M{m1:Generic}
                  §CL{f1:Foo:pub}<T>
                    §EXT{Generic.Bar<T>}
                """,
            [DocumentUri.From("file:///generic-bar-one.calr")] = """
                §M{m2:Generic}
                  §CL{b1:Bar:pub}<T>
                    §EXT{Generic.Foo<T>}
                """,
            [DocumentUri.From("file:///generic-foo-two.calr")] = """
                §M{m3:Generic}
                  §CL{f2:Foo:pub}<T,U>
                    §EXT{Generic.Bar<T,U>}
                """,
            [DocumentUri.From("file:///generic-bar-two.calr")] = """
                §M{m4:Generic}
                  §CL{b2:Bar:pub}<T,U>
                    §EXT{Generic.Foo<T,U>}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        foreach (var uri in sources.Keys)
        {
            var diagnostic = Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(item =>
                        item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
            var expectedArity = uri.ToString().Contains("-one.", StringComparison.Ordinal)
                ? "`1"
                : "`2";
            Assert.Contains(expectedArity, diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                expectedArity == "`1" ? "`2" : "`1",
                diagnostic.Message,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("i32[,]")]
    [InlineData("Nested<A,B>")]
    [InlineData("(A,B)")]
    [InlineData("(Nested<A,B>[,],(C,D))")]
    public void GenericArityParser_IgnoresNestedDelimiterCommas(
        string typeArgument)
    {
        var workspace = new WorkspaceState();
        var fooUri = DocumentUri.From(
            $"file:///arity-foo-{typeArgument.Length}.calr");
        var barUri = DocumentUri.From(
            $"file:///arity-bar-{typeArgument.Length}.calr");
        workspace.GetOrCreate(fooUri, $$"""
            §M{m1:Generic}
              §CL{f1:Foo:pub}<T>
                §EXT{Generic.Bar<{{typeArgument}}>}
            """);
        workspace.GetOrCreate(barUri, $$"""
            §M{m2:Generic}
              §CL{b1:Bar:pub}<T>
                §EXT{Generic.Foo<{{typeArgument}}>}
            """);

        var snapshot = workspace.CaptureSnapshot();
        Assert.All(new[] { fooUri, barUri }, uri =>
        {
            var diagnostic = Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(item =>
                        item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
            Assert.Contains("`1", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("`2", diagnostic.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LexicalNestedTypeResolution_WinsOverAmbiguousGlobalSimpleName()
    {
        var workspace = new WorkspaceState();
        var localUri = DocumentUri.From("file:///lexical-local.calr");
        var globalUri = DocumentUri.From("file:///lexical-global.calr");
        workspace.GetOrCreate(localUri, """
            §M{m1:Local}
              §CL{h1:Host:pub}
                §CL{Parent:pub}
                  §EXT{Child}
                §CL{Child:pub}
                  §EXT{Parent}
            """);
        workspace.GetOrCreate(globalUri, """
            §M{m2:Other}
              §CL{p1:Parent:pub}
            """);

        var snapshot = workspace.CaptureSnapshot();
        var localAst = snapshot.GetDocument(localUri)!.Analysis.Ast!;
        var host = Assert.Single(localAst.Classes);
        Assert.Equal(2, host.NestedClasses.Count);
        Assert.Equal(
            new[] { "Parent", "Child" },
            host.NestedClasses.Select(node => node.Name).ToArray());
        Assert.Equal(
            "Child",
            host.NestedClasses.Single(node => node.Name == "Parent").BaseClass);
        Assert.Equal(
            "Parent",
            host.NestedClasses.Single(node => node.Name == "Child").BaseClass);
        var localDiagnostics = workspace.GetDiagnostics(
                snapshot,
                snapshot.GetDocument(localUri)!)
            .Where(item =>
                item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)
            .ToArray();

        Assert.Equal(2, localDiagnostics.Length);
        Assert.DoesNotContain(
            workspace.GetDiagnostics(snapshot, snapshot.GetDocument(globalUri)!),
            item => item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle);
    }

    [Fact]
    public void AmbiguousLocalTypeResolution_DoesNotFallThroughToImportedGlobal()
    {
        var workspace = new WorkspaceState();
        var sources = new Dictionary<DocumentUri, string>
        {
            [DocumentUri.From("file:///ambiguous-base-one.calr")] = """
                §M{m1:Local}
                  §CL{b1:Base:pub}
                """,
            [DocumentUri.From("file:///ambiguous-base-two.calr")] = """
                §M{m2:Local}
                  §CL{b2:Base:pub}
                """,
            [DocumentUri.From("file:///ambiguous-derived.calr")] = """
                §M{m3:Local}
                  §U{Other}
                  §CL{d1:Derived:pub}
                    §EXT{Base}
                """,
            [DocumentUri.From("file:///ambiguous-import.calr")] = """
                §M{m4:Other}
                  §CL{b3:Base:pub}
                    §EXT{Local.Derived}
                """,
        };
        foreach (var (uri, source) in sources)
            workspace.GetOrCreate(uri, source);

        var snapshot = workspace.CaptureSnapshot();
        Assert.All(sources.Keys, uri =>
            Assert.DoesNotContain(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!),
                item => item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
    }

    [Fact]
    public void PartialGenericParameterNameMismatch_IsNotMergedIntoCycle()
    {
        AssertIncompatiblePartialGenericDoesNotCycle(
            "§CL{f1:Foo:pub:partial}<T>\n    §EXT{Generic.Bar<T>}",
            "§CL{f2:Foo:pub:partial}<U>");
    }

    [Fact]
    public void PartialGenericConstraintMismatch_IsNotMergedIntoCycle()
    {
        AssertIncompatiblePartialGenericDoesNotCycle(
            "§CL{f1:Foo:pub:partial}<T>\n    §WHERE T : class\n    §EXT{Generic.Bar<T>}",
            "§CL{f2:Foo:pub:partial}<T>\n    §WHERE T : struct");
    }

    [Fact]
    public void CompatiblePartialGenericDeclarations_MergeIntoCycle()
    {
        var workspace = new WorkspaceState();
        var firstUri = DocumentUri.From("file:///compatible-partial-one.calr");
        var secondUri = DocumentUri.From("file:///compatible-partial-two.calr");
        var barUri = DocumentUri.From("file:///compatible-partial-bar.calr");
        var firstState = workspace.GetOrCreate(firstUri, """
            §M{m1:Generic}
              §CL{f1:Foo:pub:partial}<T>
                §WHERE T : class
                §EXT{Generic.Bar<T>}
            """);
        var secondState = workspace.GetOrCreate(secondUri, """
            §M{m2:Generic}
              §CL{f2:Foo:pub:partial}<T>
                §WHERE T : class
            """);
        workspace.GetOrCreate(barUri, """
            §M{m3:Generic}
              §CL{b1:Bar:pub}<T>
                §EXT{Generic.Foo<T>}
            """);

        foreach (var node in new[]
                 {
                     Assert.Single(firstState.Snapshot.Ast!.Classes),
                     Assert.Single(secondState.Snapshot.Ast!.Classes),
                 })
        {
            Assert.True(node.IsPartial);
            Assert.Equal("Foo", node.Name);
            var parameter = Assert.Single(node.TypeParameters);
            Assert.Equal("T", parameter.Name);
            Assert.Equal(
                Compiler.Ast.TypeConstraintKind.Class,
                Assert.Single(parameter.Constraints).Kind);
        }
        Assert.Equal(
            "Generic.Bar<T>",
            Assert.Single(firstState.Snapshot.Ast!.Classes).BaseClass);
        Assert.Equal(
            "Generic.Foo<T>",
            Assert.Single(workspace.Get(barUri)!.Snapshot.Ast!.Classes).BaseClass);
        Assert.Equal(
            "Bar",
            Assert.Single(workspace.Get(barUri)!.Snapshot.Ast!.Classes).Name);

        var snapshot = workspace.CaptureSnapshot();
        Assert.All(new[] { firstUri, secondUri, barUri }, uri =>
            Assert.Single(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!)
                    .Where(item =>
                        item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle)));
    }

    [Fact]
    public void ModuleNestedTypeFlatteningCollision_IsAmbiguousWithoutThrowing()
    {
        var workspace = new WorkspaceState();
        var nestedUri = DocumentUri.From("file:///structured-nested.calr");
        var moduleUri = DocumentUri.From("file:///structured-module.calr");
        workspace.GetOrCreate(nestedUri, """
            §M{m1:A}
              §CL{b1:B:pub}
                §CL{C:pub}
                  §EXT{A.B.C}
            """);
        workspace.GetOrCreate(moduleUri, """
            §M{m2:A.B}
              §CL{c2:C:pub}
                §EXT{A.B.C}
            """);

        var snapshot = workspace.CaptureSnapshot();

        Assert.Equal(2, snapshot.Documents.Length);
        Assert.All(new[] { nestedUri, moduleUri }, uri =>
            Assert.DoesNotContain(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!),
                diagnostic =>
                    diagnostic.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
    }

    private static void AssertIncompatiblePartialGenericDoesNotCycle(
        string firstDeclaration,
        string secondDeclaration)
    {
        var workspace = new WorkspaceState();
        var firstUri = DocumentUri.From("file:///incompatible-partial-one.calr");
        var secondUri = DocumentUri.From("file:///incompatible-partial-two.calr");
        var barUri = DocumentUri.From("file:///incompatible-partial-bar.calr");
        workspace.GetOrCreate(firstUri, $"§M{{m1:Generic}}\n  {firstDeclaration}\n");
        workspace.GetOrCreate(secondUri, $"§M{{m2:Generic}}\n  {secondDeclaration}\n");
        workspace.GetOrCreate(barUri, """
            §M{m3:Generic}
              §CL{b1:Bar:pub}<T>
                §EXT{Generic.Foo<T>}
            """);

        var snapshot = workspace.CaptureSnapshot();
        Assert.All(new[] { firstUri, secondUri, barUri }, uri =>
            Assert.DoesNotContain(
                workspace.GetDiagnostics(snapshot, snapshot.GetDocument(uri)!),
                item => item.Code == Compiler.Diagnostics.DiagnosticCode.InheritanceCycle));
    }

    [Fact]
    public async Task CapturedWorkspaceIndex_RemainsVersionCoherentAfterUpdateAsync()
    {
        const string original = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R 42
              §F{f002:Use:pub} () -> i32
                §R §C{Compute} §/C
            """;
        const string updated = """
            §M{m001:TestModule}
              §F{f001:Calculate:pub} () -> i32
                §R 42
              §F{f002:Use:pub} () -> i32
                §R §C{Calculate} §/C
            """;
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///workspace-snapshot.calr");
        workspace.GetOrCreate(uri, original, version: 1);
        var captured = workspace.CaptureSnapshot();
        var capturedDocument = captured.GetDocument(uri)!;
        var originalReference = original.LastIndexOf("Compute", StringComparison.Ordinal);
        var capturedOccurrence = workspace.ResolveOccurrence(
            captured,
            uri,
            originalReference);

        var directUpdate = await workspace.Get(uri)!.UpdateAsync(
            updated,
            newVersion: 2);
        Assert.True(directUpdate.Accepted);
        var current = workspace.CaptureSnapshot();
        var currentReference = updated.LastIndexOf("Calculate", StringComparison.Ordinal);
        var currentOccurrence = workspace.ResolveOccurrence(
            current,
            uri,
            currentReference);

        Assert.NotNull(capturedOccurrence);
        Assert.Equal(1, capturedDocument.Analysis.Version);
        Assert.Contains("Compute", capturedDocument.Analysis.Source, StringComparison.Ordinal);
        Assert.Equal(
            "Compute",
            capturedDocument.Analysis.Source.Substring(
                capturedOccurrence.Span.Start,
                capturedOccurrence.Span.Length));
        Assert.Equal(2, current.GetDocument(uri)!.Analysis.Version);
        Assert.NotNull(currentOccurrence);
        Assert.Equal(
            "Calculate",
            current.GetDocument(uri)!.Analysis.Source.Substring(
                currentOccurrence.Span.Start,
                currentOccurrence.Span.Length));
        Assert.Contains(
            "Calculate",
            current.GetDocument(uri)!.Analysis.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledWorkspaceScan_IsBoundedAndDoesNotHoldIndexLockAsync()
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repositoryRoot, "Calor.sln")))
        {
            repositoryRoot = Directory.GetParent(repositoryRoot)?.FullName
                ?? throw new InvalidOperationException("Repository root not found.");
        }
        var directory = Path.Combine(
            repositoryRoot,
            $"workspace-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "blocked.calr"),
            "§M{m001:Blocked}\n");
        var readerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new WorkspaceState(
            workspaceRootPath: null,
            logger: null,
            workspaceFileReader: async (_, cancellationToken) =>
            {
                readerStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            });
        using var cancellation = new CancellationTokenSource();

        try
        {
            var scan = workspace.ConfigureWorkspaceRootAsync(
                new UriBuilder(Uri.UriSchemeFile, string.Empty)
                {
                    Path = directory + Path.DirectorySeparatorChar,
                }.Uri,
                cancellation.Token);
            await readerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var snapshot = await Task.Run(() => workspace.CaptureSnapshot())
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(snapshot.Documents);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => scan.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WorkspaceEnumerationFailure_IsLoggedAndDoesNotAbortInitializationAsync()
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repositoryRoot, "Calor.sln")))
        {
            repositoryRoot = Directory.GetParent(repositoryRoot)?.FullName
                ?? throw new InvalidOperationException("Repository root not found.");
        }
        var directory = Path.Combine(
            repositoryRoot,
            $"workspace-enumeration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var logger = new CapturingWorkspaceLogger();
        var workspace = new WorkspaceState(
            workspaceRootPath: null,
            logger,
            workspaceFileReader: File.ReadAllTextAsync,
            workspaceFileEnumerator: _ => Enumerable.Range(0, 1)
                .Select<int, string>(_ =>
                    throw new IOException("injected enumeration failure")));

        try
        {
            await workspace.ConfigureWorkspaceRootAsync(
                    new UriBuilder(Uri.UriSchemeFile, string.Empty)
                    {
                        Path = directory + Path.DirectorySeparatorChar,
                    }.Uri,
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Empty(workspace.CaptureSnapshot().Documents);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.IsType<IOException>(entry.Exception);
            Assert.Equal(
                Path.GetFullPath(directory),
                entry.Properties["WorkspaceRoot"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReopenDuringWorkspaceScan_OpenDocumentWinsWithoutDuplicateRegistryEntryAsync()
    {
        var repositoryRoot = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repositoryRoot, "Calor.sln")))
        {
            repositoryRoot = Directory.GetParent(repositoryRoot)?.FullName
                ?? throw new InvalidOperationException("Repository root not found.");
        }
        var directory = Path.Combine(
            repositoryRoot,
            $"workspace-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "race.calr");
        const string diskSource = "§M{d001:Disk}\n";
        const string openSource = "§M{o001:Open}\n";
        const string reopenedSource = "§M{r001:Reopened}\n";
        await File.WriteAllTextAsync(path, diskSource);
        var uri = DocumentUri.FromFileSystemPath(path);
        var scanReadyToApply = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowScanApply = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCalls = 0;
        var workspace = new WorkspaceState(
            workspaceRootPath: null,
            logger: null,
            workspaceFileReader: File.ReadAllTextAsync,
            workspaceFileEnumerator: null,
            beforeWorkspaceScanApply: () =>
            {
                if (Interlocked.Increment(ref applyCalls) == 1)
                {
                    scanReadyToApply.TrySetResult();
                    return allowScanApply.Task;
                }
                return Task.CompletedTask;
            });

        try
        {
            var scan = workspace.ConfigureWorkspaceRootAsync(
                new UriBuilder(Uri.UriSchemeFile, string.Empty)
                {
                    Path = directory + Path.DirectorySeparatorChar,
                }.Uri,
                CancellationToken.None);
            await scanReadyToApply.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await workspace.GetOrCreateAsync(
                uri,
                openSource,
                version: 7));
            allowScanApply.TrySetResult();
            await scan.WaitAsync(TimeSpan.FromSeconds(2));

            var openSnapshot = workspace.CaptureSnapshot();
            var openDocument = Assert.Single(openSnapshot.Documents);
            Assert.Equal(uri, DocumentUri.From(openDocument.Document.Uri));
            Assert.Equal(7, openDocument.Analysis.Version);
            Assert.Equal(openSource, openDocument.Analysis.Source);
            Assert.True(workspace.Contains(uri));

            Assert.True(workspace.Remove(uri));
            await workspace.RefreshClosedDocumentsAsync(CancellationToken.None);
            var closedDocument = Assert.Single(
                workspace.CaptureSnapshot().Documents);
            Assert.Equal(diskSource, closedDocument.Analysis.Source);
            Assert.False(workspace.Contains(uri));

            Assert.True(await workspace.GetOrCreateAsync(
                uri,
                reopenedSource,
                version: 8));
            var reopened = Assert.Single(workspace.CaptureSnapshot().Documents);
            Assert.Equal(8, reopened.Analysis.Version);
            Assert.Equal(reopenedSource, reopened.Analysis.Source);

            Assert.True(workspace.Remove(uri));
            File.Delete(path);
            await workspace.RefreshClosedDocumentsAsync(CancellationToken.None);
            Assert.Empty(workspace.CaptureSnapshot().Documents);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindMemberAcrossFiles_NonExistentMember_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
              §CL{c001:Person}
                §FLD{str:name}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "nonexistent");

        Assert.Null(doc);
        Assert.Null(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_NonExistentType_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
              §CL{c001:Person}
                §FLD{str:name}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("NonExistent", "name");

        Assert.Null(doc);
        Assert.Null(node);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EmptyWorkspace_FindDefinition_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var (doc, node) = workspace.FindDefinitionAcrossFiles("Anything");

        Assert.Null(doc);
        Assert.Null(node);
    }

    [Fact]
    public void EmptyWorkspace_GetAllPublicSymbols_ReturnsEmpty()
    {
        var workspace = new WorkspaceState();

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Empty(symbols);
    }

    [Fact]
    public void DocumentWithParseError_SkippedInSearch()
    {
        var workspace = new WorkspaceState();

        // Invalid document (unclosed module)
        var source1 = """
            §M{m001:Invalid
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///invalid.calr"), source1);

        // Valid document
        var source2 = """
            §M{m002:Valid}
              §F{f001:ValidFunc:pub}
                §R 0
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///valid.calr"), source2);

        // Should still find the valid function
        var (doc, node) = workspace.FindDefinitionAcrossFiles("ValidFunc");
        Assert.NotNull(doc);
        Assert.Contains("valid.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void DuplicateSymbolNames_FindsFirst()
    {
        var workspace = new WorkspaceState();

        // First document defines Helper
        var source1 = """
            §M{m001:File1}
              §F{f001:Helper:pub}
                §R 1
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file1.calr"), source1);

        // Second document also defines Helper
        var source2 = """
            §M{m002:File2}
              §F{f001:Helper:pub}
                §R 2
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file2.calr"), source2);

        // Should find one of them (first encountered)
        var (doc, node) = workspace.FindDefinitionAcrossFiles("Helper");
        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    private sealed class CapturingWorkspaceLogger : ILogger<WorkspaceState>
    {
        public List<WorkspaceLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) =>
            EmptyScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new WorkspaceLogEntry(
                logLevel,
                exception,
                properties));
        }

        private sealed class EmptyScope : IDisposable
        {
            public static EmptyScope Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed record WorkspaceLogEntry(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    #endregion
}
