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
/// Native fixtures retain the original implicit CLR namespaces as explicit headers;
/// compilation-unit labels do not stand in for namespaces.
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
            module Shared Shared
              func Same () → void
            """;

        var first = OpenDocument(workspace, firstUri, source);
        var second = OpenDocument(workspace, secondUri, source);
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
            module Utils Utils
              func Pick (int:value) → int
                return value
              func Pick (string:value) → string
                return value
            """;
        var use = """
            module Main Main
              func Run () → int
                return Pick(1)
            """;
        var definitionsState = OpenDocument(workspace,
            DocumentUri.From("file:///utils.calr"),
            definitions);
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);

        Assert.NotNull(definitionsState.BoundModule);
        Assert.NotNull(useState.BoundModule);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Pick", StringComparison.Ordinal));
        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(call);

        Assert.NotNull(resolved.Symbol);
        Assert.Equal("int", resolved.Symbol.Parameters[0].TypeName);
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
        OpenDocument(workspace,
            DocumentUri.From("file:///utils.calr"),
            """
            module Utils Utils
              func Pick (string:value) → string
                return value
            """);
        var use = """
            module Main Main
              func Run () → int
                return Pick(1)
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Pick", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(call);

        Assert.Null(resolved.Doc);
        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_UsesNullableStringCompatibilityAcrossFiles()
    {
        var workspace = new WorkspaceState();
        var definitionsState = OpenDocument(workspace,
            DocumentUri.From("file:///utils.calr"),
            """
            module Utils Utils
              func Take (string:value) → string
                !
                return value
            """);
        var use = """
            module Main Main
              func Run (string?:value) → string
                !
                return Take(value)
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Take", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(call);

        Assert.Same(definitionsState, resolved.Doc);
        Assert.Equal(
            Assert.Single(definitionsState.BoundModule!.Functions).SymbolId,
            resolved.Symbol?.Id);
    }

    [Fact]
    public void ResolveProjectCall_DoesNotApplyNullableStringCompatibilityToConstructors()
    {
        var workspace = new WorkspaceState();
        OpenDocument(workspace,
            DocumentUri.From("file:///models.calr"),
            """
            module Models Models
              class Box
                ctor (string:value)
            """);
        var use = """
            module Main Main
              func Run (string?:value) → Box
                !
                return new Box(value)
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);
        var creation = Assert.IsType<BoundNewExpression>(
            SymbolFinder.FindBoundCallAtOffset(
                useState.BoundModule,
                use.LastIndexOf("Box(", StringComparison.Ordinal)));

        var resolved = workspace.ResolveProjectCall(creation);

        Assert.Null(resolved.Doc);
        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_DoesNotApplyNullableStringCompatibilityToBaseInitializers()
    {
        var workspace = new WorkspaceState();
        OpenDocument(workspace,
            DocumentUri.From("file:///base.calr"),
            """
            module Models Models
              class Base
                ctor (string:value)
            """);
        var use = """
            module Main Main
              class Derived extends Base
                ctor (string?:value)
                  base(value)
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///derived.calr"),
            use);
        var initializer = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("base(value)", StringComparison.Ordinal));

        var resolved = workspace.ResolveProjectCall(initializer);

        Assert.Null(resolved.Doc);
        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void EquivalentWorkspaceRoots_ProducePortableStableSymbolIds()
    {
        const string source = """
            module Shared Shared
              func Same () → void
            """;
        var first = new WorkspaceState("/checkout/one");
        var second = new WorkspaceState("/different/checkout");

        var firstSymbol = Assert.Single(OpenDocument(first,
            DocumentUri.FromFileSystemPath("/checkout/one/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var secondSymbol = Assert.Single(OpenDocument(second,
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
            module Shared Shared
              func Same () → void
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

        var firstRootSymbol = Assert.Single(OpenDocument(firstWorkspace,
            DocumentUri.FromFileSystemPath("/checkout/first/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var secondRootSymbol = Assert.Single(OpenDocument(firstWorkspace,
            DocumentUri.FromFileSystemPath("/checkout/second/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var relocatedFirst = Assert.Single(OpenDocument(secondWorkspace,
            DocumentUri.FromFileSystemPath("/relocated/first/src/shared.calr"),
            source).BoundModule!.Functions).Symbol;
        var relocatedSecond = Assert.Single(OpenDocument(secondWorkspace,
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
        OpenDocument(workspace,
            DocumentUri.From("file:///private.calr"),
            """
            module Utils Utils
              func Hidden private () → int
                return 1
            """);
        var use = """
            module Main Main
              func Run () → int
                return Hidden()
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.IndexOf("Hidden", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(useState, useState.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_DoesNotExposeUnqualifiedClassMethods()
    {
        var workspace = new WorkspaceState();
        OpenDocument(workspace,
            DocumentUri.From("file:///service.calr"),
            """
            module Service Service
              class Worker
                func Run () → int
                  return 1
            """);
        var use = """
            module Main Main
              func RunMain () → int
                return Run()
            """;
        var useState = OpenDocument(workspace,
            DocumentUri.From("file:///main.calr"),
            use);
        var call = SymbolFinder.FindBoundCallAtOffset(
            useState.BoundModule,
            use.LastIndexOf("Run", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(useState, useState.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_RejectsPrivateMemberOutsideContainingClass()
    {
        var workspace = new WorkspaceState();
        var source = """
            module Main Main
              class Worker
                func Hidden private () → int
                  return 1
              func Run () → int
                return Worker.Hidden()
            """;
        var state = OpenDocument(workspace, DocumentUri.From("file:///main.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.IndexOf("Worker.Hidden", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Null(resolved.Symbol);
    }

    [Fact]
    public void ResolveProjectCall_AllowsPrivateMemberInsideContainingClass()
    {
        var workspace = new WorkspaceState();
        var source = """
            module Main Main
              class Worker
                func Hidden private () → int
                  return 1
                func Run () → int
                  return Hidden()
            """;
        var state = OpenDocument(workspace, DocumentUri.From("file:///main.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.LastIndexOf("Hidden", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Equal("Main.Worker.Hidden", resolved.Symbol?.Name);
        Assert.Equal("Main.Worker", resolved.Symbol!.ContainingTypeName);
        Assert.Equal(state.BoundModule!.Functions.Single(function =>
            function.Symbol.Name == "Main.Worker.Hidden").SymbolId, resolved.Symbol.Id);
    }

    [Fact]
    public void ResolveProjectCall_UsesCrossFileBaseClassIdentity()
    {
        var workspace = new WorkspaceState();
        var baseDocument = OpenDocument(workspace,
            DocumentUri.From("file:///base.calr"),
            """
            module BaseModule BaseModule
              class Base
                func Pick (int:value) → int
                  return value
            """);
        var source = """
            module DerivedModule DerivedModule
              class Derived extends Base
                func Use () → int
                  return base.Pick(1)
            """;
        var state = OpenDocument(workspace, DocumentUri.From("file:///derived.calr"), source);
        var call = SymbolFinder.FindBoundCallAtOffset(
            state.BoundModule,
            source.IndexOf("base.Pick", StringComparison.Ordinal));

        Assert.NotNull(call);
        var resolved = workspace.ResolveProjectCall(state, state.Snapshot, call);

        Assert.Equal("BaseModule.Base.Pick", resolved.Symbol?.Name);
        Assert.Same(baseDocument, resolved.Doc);
        Assert.Equal(Assert.Single(baseDocument.BoundModule!.Functions).SymbolId, resolved.Symbol!.Id);
    }

    [Fact]
    public void NewWithoutConstructor_ResolvesToClassDeclaration()
    {
        var workspace = new WorkspaceState();
        var classState = OpenDocument(workspace,
            DocumentUri.From("file:///model.calr"),
            """
            module Models Models
              class Widget
            """);
        var use = """
            module Main Main
              func Make () → Widget
                return new Widget()
            """;
        var useState = OpenDocument(workspace,
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
        var definitions = OpenDocument(workspace,
            definitionsUri,
            """
            module Utils Utils
              func Pick () → int
                return 1
            """);
        var oldSymbol = Assert.Single(definitions.BoundModule!.Functions).Symbol;
        workspace.Update(
            definitionsUri,
            """
            module Utils Utils
              func Before () → int
                return 0
              func Pick () → int
                let int:value = 1
                return value
            """,
            version: 2);
        var use = """
            module Main Main
              func Run () → int
                return Pick()
            """;
        var useState = OpenDocument(workspace, DocumentUri.From("file:///main.calr"), use);

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
        var state = OpenDocument(workspace,
            uri,
            "module State State\n  func Value () → int\n    return 1\n",
            version: 1);
        var captured = state.Snapshot;
        workspace.Update(
            uri,
            "module State State\n  func Value () → int\n    return 2\n",
            version: 2);
        workspace.Update(
            uri,
            "module State State\n  func Value () → int\n    return 0\n",
            version: 1);

        Assert.Equal(1, captured.Version);
        Assert.Contains("return 1", captured.Source);
        Assert.Equal(2, state.Version);
        Assert.Contains("return 2", state.Source);
    }

    #region WorkspaceState Tests

    [Fact]
    public void FindDefinitionAcrossFiles_Function_FindsInOtherDocument()
    {
        var workspace = new WorkspaceState();

        // First document defines a function
        var source1 = """
            module Utils Utils
              func Add (int:a, int:b) → int
                return a + b
            """;
        OpenDocument(workspace, DocumentUri.From("file:///utils.calr"), source1);

        // Second document uses the function
        var source2 = """
            module Main Main
              func Test () → int
                let result = Add(1, 2)
                return result
            """;
        OpenDocument(workspace, DocumentUri.From("file:///main.calr"), source2);

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
            module Models Models
              class Person
                field string:name
                field int:age
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source1);

        // Second document uses the class
        var source2 = """
            module Main Main
              func CreatePerson () → Person
                return new Person()
            """;
        OpenDocument(workspace, DocumentUri.From("file:///main.calr"), source2);

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
            module Interfaces Interfaces
              interface IShape
                func GetArea () → double
            """;
        OpenDocument(workspace, DocumentUri.From("file:///interfaces.calr"), source1);

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
            module Types Types
              enum Color
                Red
                Green
                Blue
            """;
        OpenDocument(workspace, DocumentUri.From("file:///types.calr"), source1);

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
            module Test Test
              func Test () → void
                return 42
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var (doc, node) = workspace.FindDefinitionAcrossFiles("NonExistent");

        Assert.Null(doc);
        Assert.Null(node);
    }

    [Fact]
    public void FindDefinitionAcrossFiles_LocalFunction_FindsInSameDocument()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Test Test
              func Helper () → void
                return 42
              func Main () → void
                let x = Helper()
                return x
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

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
            module Test Test
              func PublicFunc () → void
                return 42
              func PrivateFunc private () → void
                return 0
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "PublicFunc" && s.Kind == "function");
        Assert.DoesNotContain(symbols, s => s.Name == "PrivateFunc");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsClasses()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Test Test
              class MyClass
                field int:value
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "MyClass" && s.Kind == "class");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsInterfaces()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Test Test
              interface IMyInterface
                func DoSomething () → void
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "IMyInterface" && s.Kind == "interface");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsEnums()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Test Test
              enum Status
                Active
                Inactive
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "Status" && s.Kind == "enum");
    }

    [Fact]
    public void GetAllPublicSymbols_ReturnsDelegates()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Test Test
              delegate Callback (int:value) → void
            """;
        OpenDocument(workspace, DocumentUri.From("file:///test.calr"), source);

        var symbols = workspace.GetAllPublicSymbols().ToList();

        Assert.Contains(symbols, s => s.Name == "Callback" && s.Kind == "delegate");
    }

    [Fact]
    public void GetAllPublicSymbols_MultipleDocuments_ReturnsAll()
    {
        var workspace = new WorkspaceState();

        var source1 = """
            module File1 File1
              func FuncA () → void
                return 1
            """;
        OpenDocument(workspace, DocumentUri.From("file:///file1.calr"), source1);

        var source2 = """
            module File2 File2
              func FuncB () → void
                return 2
            """;
        OpenDocument(workspace, DocumentUri.From("file:///file2.calr"), source2);

        var source3 = """
            module File3 File3
              class ClassC
            """;
        OpenDocument(workspace, DocumentUri.From("file:///file3.calr"), source3);

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
            module Models Models
              class Customer
                field string:name
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source1);

        // Second document should have access to Customer type in completions
        var source2 = """
            module Service Service
              func GetCustomer () → int
                return 0
            """;
        var doc2 = OpenDocument(workspace, DocumentUri.From("file:///service.calr"), source2);

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
            module Utils Utils
              func Calculate (int:x) → int
                return x * 2
            """;
        OpenDocument(workspace, DocumentUri.From("file:///utils.calr"), source1);

        // Second document should see Calculate in completions
        var source2 = """
            module Main Main
              func Test () → int
                return 0
            """;
        var doc2 = OpenDocument(workspace, DocumentUri.From("file:///main.calr"), source2);

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
            module Models Models
              class Address
                field string:street
                field string:city
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source1);

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
            module Base Base
              class Animal
                field string:name
                func Speak () → string
                  return "..."
            """;
        OpenDocument(workspace, DocumentUri.From("file:///base.calr"), source1);

        // Second document defines a derived class
        var source2 = """
            module Derived Derived
              class Dog extends Animal
                field string:breed
            """;
        OpenDocument(workspace, DocumentUri.From("file:///derived.calr"), source2);

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
            module Test Test
              func ToRemove () → void
                return 42
            """;
        var uri = DocumentUri.From("file:///toremove.calr");
        OpenDocument(workspace, uri, source);

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
            module Test Test
              func OldName () → void
                return 42
            """;
        OpenDocument(workspace, uri, source1);

        // Verify old name is found
        var (doc1, _) = workspace.FindDefinitionAcrossFiles("OldName");
        Assert.NotNull(doc1);

        // Update the document
        var source2 = """
            module Test Test
              func NewName () → void
                return 42
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
            module Models Models
              class Person
                field string:name
                field int:age
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source);

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
            module Models Models
              class Person
                field string:name
                func GetName () → string
                  return name
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "GetName");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_ClassProperty_FindsProperty()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Models Models
              class Person
                property string:FullName
                  get
                    return "test"
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "FullName");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_EnumMember_FindsMember()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Types Types
              enum Color
                Red
                Green
                Blue
            """;
        OpenDocument(workspace, DocumentUri.From("file:///types.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Color", "Red");

        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_InterfaceMethod_FindsMethod()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Interfaces Interfaces
              interface IShape
                func GetArea () → double
            """;
        OpenDocument(workspace, DocumentUri.From("file:///interfaces.calr"), source);

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
            module Base Base
              class Animal
                field string:name
            """;
        OpenDocument(workspace, DocumentUri.From("file:///base.calr"), source1);

        // Derived class in another file
        var source2 = """
            module Derived Derived
              class Dog extends Animal
                field string:breed
            """;
        OpenDocument(workspace, DocumentUri.From("file:///derived.calr"), source2);

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
        OpenDocument(workspace, aUri, """
            module CycleA CycleA
              class A extends B
            """);
        OpenDocument(workspace, bUri, """
            module CycleB CycleB
              class B extends A
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
        OpenDocument(workspace, aUri, """
            module CycleA CycleA
              class A extends B
            """);
        OpenDocument(workspace, bUri, """
            module CycleB CycleB
              class B extends C
            """);
        OpenDocument(workspace, cUri, """
            module CycleC CycleC
              class C extends A
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
        OpenDocument(workspace, aUri, """
            module CycleA CycleA
              class A extends CycleB.B
            """);
        OpenDocument(workspace, bUri, """
            module CycleB CycleB
              class B extends CycleA.A
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
                module LeftA LeftA
                  class A extends LeftB.B
                """,
            [DocumentUri.From("file:///left-b.calr")] = """
                module LeftB LeftB
                  class B extends LeftA.A
                """,
            [DocumentUri.From("file:///right-a.calr")] = """
                module RightA RightA
                  class A extends RightB.B
                """,
            [DocumentUri.From("file:///right-b.calr")] = """
                module RightB RightB
                  class B extends RightA.A
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
                module CycleA CycleA
                  class A partial extends CycleB.B
                """,
            [DocumentUri.From("file:///partial-a-members.calr")] = """
                module CycleA CycleA
                  class A partial
                    field int:value
                """,
            [DocumentUri.From("file:///partial-b-base.calr")] = """
                module CycleB CycleB
                  class B partial extends CycleA.A
                """,
            [DocumentUri.From("file:///partial-b-members.calr")] = """
                module CycleB CycleB
                  class B partial
                    field int:value
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
                module CycleA CycleA
                  class A partial extends CycleB.B
                """,
            [DocumentUri.From("file:///partial-indirect-a-part.calr")] = """
                module CycleA CycleA
                  class A partial
                """,
            [DocumentUri.From("file:///partial-indirect-b-base.calr")] = """
                module CycleB CycleB
                  class B partial extends CycleC.C
                """,
            [DocumentUri.From("file:///partial-indirect-b-part.calr")] = """
                module CycleB CycleB
                  class B partial
                """,
            [DocumentUri.From("file:///partial-indirect-c-base.calr")] = """
                module CycleC CycleC
                  class C partial extends CycleA.A
                """,
            [DocumentUri.From("file:///partial-indirect-c-part.calr")] = """
                module CycleC CycleC
                  class C partial
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
                module Duplicate Duplicate
                  class A extends Other.B
                """,
            [DocumentUri.From("file:///duplicate-a-two.calr")] = """
                module Duplicate Duplicate
                  class A
                """,
            [DocumentUri.From("file:///duplicate-b.calr")] = """
                module Other Other
                  class B extends Duplicate.A
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
                module Generic Generic
                  class Foo<T> extends Generic.Bar<T>
                """,
            [DocumentUri.From("file:///generic-bar-one.calr")] = """
                module Generic Generic
                  class Bar<T> extends Generic.Foo<T>
                """,
            [DocumentUri.From("file:///generic-foo-two.calr")] = """
                module Generic Generic
                  class Foo<T,U> extends Generic.Bar<T,U>
                """,
            [DocumentUri.From("file:///generic-bar-two.calr")] = """
                module Generic Generic
                  class Bar<T,U> extends Generic.Foo<T,U>
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
        // Keep the original data-row identity while using the selected native
        // spelling of the same CLR Int32 rectangular-array type.
        var nativeTypeArgument = typeArgument == "i32[,]" ? "int[,]" : typeArgument;
        var workspace = new WorkspaceState();
        var fooUri = DocumentUri.From(
            $"file:///arity-foo-{typeArgument.Length}.calr");
        var barUri = DocumentUri.From(
            $"file:///arity-bar-{typeArgument.Length}.calr");
        OpenDocument(workspace, fooUri, $$"""
            module Generic Generic
              class Foo<T> extends Generic.Bar<{{nativeTypeArgument}}>
            """);
        OpenDocument(workspace, barUri, $$"""
            module Generic Generic
              class Bar<T> extends Generic.Foo<{{nativeTypeArgument}}>
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
        OpenDocument(workspace, localUri, """
            module Local Local
              class Host
                class Parent extends Child
                class Child extends Parent
            """);
        OpenDocument(workspace, globalUri, """
            module Other Other
              class Parent
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
                module Local Local
                  class Base
                """,
            [DocumentUri.From("file:///ambiguous-base-two.calr")] = """
                module Local Local
                  class Base
                """,
            [DocumentUri.From("file:///ambiguous-derived.calr")] = """
                module Local Local
                  using Other
                  class Derived extends Base
                """,
            [DocumentUri.From("file:///ambiguous-import.calr")] = """
                module Other Other
                  class Base extends Local.Derived
                """,
        };
        foreach (var (uri, source) in sources)
            OpenDocument(workspace, uri, source);

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
            "class Foo<T> partial extends Generic.Bar<T>\n",
            "class Foo<U> partial");
    }

    [Fact]
    public void PartialGenericConstraintMismatch_IsNotMergedIntoCycle()
    {
        AssertIncompatiblePartialGenericDoesNotCycle(
            "class Foo<T> partial extends Generic.Bar<T>\n    where T : class\n",
            "class Foo<T> partial\n    where T : struct");
    }

    [Fact]
    public void CompatiblePartialGenericDeclarations_MergeIntoCycle()
    {
        var workspace = new WorkspaceState();
        var firstUri = DocumentUri.From("file:///compatible-partial-one.calr");
        var secondUri = DocumentUri.From("file:///compatible-partial-two.calr");
        var barUri = DocumentUri.From("file:///compatible-partial-bar.calr");
        var firstState = OpenDocument(workspace, firstUri, """
            module Generic Generic
              class Foo<T> partial extends Generic.Bar<T>
                where T : class
            """);
        var secondState = OpenDocument(workspace, secondUri, """
            module Generic Generic
              class Foo<T> partial
                where T : class
            """);
        OpenDocument(workspace, barUri, """
            module Generic Generic
              class Bar<T> extends Generic.Foo<T>
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
        OpenDocument(workspace, nestedUri, """
            module A A
              class B
                class C extends A.B.C
            """);
        OpenDocument(workspace, moduleUri, """
            module A.B A.B
              class C extends A.B.C
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
        OpenDocument(workspace, firstUri, $"module Generic Generic\n  {firstDeclaration}\n");
        OpenDocument(workspace, secondUri, $"module Generic Generic\n  {secondDeclaration}\n");
        OpenDocument(workspace, barUri, """
            module Generic Generic
              class Bar<T> extends Generic.Foo<T>
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
            module TestModule TestModule
              func Compute () → int
                return 42
              func Use () → int
                return Compute()
            """;
        const string updated = """
            module TestModule TestModule
              func Calculate () → int
                return 42
              func Use () → int
                return Calculate()
            """;
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///workspace-snapshot.calr");
        OpenDocument(workspace, uri, original, version: 1);
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
            "module Blocked Blocked\n");
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
        const string diskSource = "module Disk Disk\n";
        const string openSource = "module Open Open\n";
        const string reopenedSource = "module Reopened Reopened\n";
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
            module Models Models
              class Person
                field string:name
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source);

        var (doc, node) = workspace.FindMemberAcrossFiles("Person", "nonexistent");

        Assert.Null(doc);
        Assert.Null(node);
    }

    [Fact]
    public void FindMemberAcrossFiles_NonExistentType_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var source = """
            module Models Models
              class Person
                field string:name
            """;
        OpenDocument(workspace, DocumentUri.From("file:///models.calr"), source);

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
        var invalid = workspace.GetOrCreate(DocumentUri.From("file:///invalid.calr"), source1);
        Assert.True(invalid.Diagnostics.HasErrors);

        // Valid document
        var source2 = """
            module Valid Valid
              func ValidFunc () → void
                return 0
            """;
        OpenDocument(workspace, DocumentUri.From("file:///valid.calr"), source2);

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
            module File1 File1
              func Helper () → void
                return 1
            """;
        OpenDocument(workspace, DocumentUri.From("file:///file1.calr"), source1);

        // Second document also defines Helper
        var source2 = """
            module File2 File2
              func Helper () → void
                return 2
            """;
        OpenDocument(workspace, DocumentUri.From("file:///file2.calr"), source2);

        // Should find one of them (first encountered)
        var (doc, node) = workspace.FindDefinitionAcrossFiles("Helper");
        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    private static DocumentState OpenDocument(
        WorkspaceState workspace, DocumentUri uri, string source, int version = 0)
    {
        var document = workspace.GetOrCreate(uri, source, version);
        Assert.NotNull(document.Ast);
        Assert.Equal(document.Ast.Name, Assert.Single(document.Ast.NamespaceScopes).FullName);
        Assert.DoesNotContain(document.Diagnostics, diagnostic =>
            diagnostic.Severity == Compiler.Diagnostics.DiagnosticSeverity.Error
            && (diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor083", StringComparison.Ordinal)));
        return document;
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
