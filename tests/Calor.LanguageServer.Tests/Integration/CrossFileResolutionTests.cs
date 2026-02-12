using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Xunit;

namespace Calor.LanguageServer.Tests.Integration;

/// <summary>
/// Tests for cross-file resolution functionality - go-to-definition and completions
/// across multiple open documents.
/// </summary>
public class CrossFileResolutionTests
{
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
            §/F{f001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///utils.calr"), source1);

        // Second document uses the function
        var source2 = """
            §M{m002:Main}
            §F{f002:Test}
            §O{i32}
            §B{result} §C{Add} 1 2 §/C
            §R result
            §/F{f002}
            §/M{m002}
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
            §/CL{c001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source1);

        // Second document uses the class
        var source2 = """
            §M{m002:Main}
            §F{f001:CreatePerson}
            §O{Person}
            §R §NEW Person
            §/F{f001}
            §/M{m002}
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
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
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
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §F{f002:Main}
            §B{x} §C{Helper} §/C
            §R x
            §/F{f002}
            §/M{m001}
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
            §/F{f001}
            §F{f002:PrivateFunc:priv}
            §R 0
            §/F{f002}
            §/M{m001}
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
            §/CL{c001}
            §/M{m001}
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
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
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
            §/M{m001}
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
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file1.calr"), source1);

        var source2 = """
            §M{m002:File2}
            §F{f001:FuncB:pub}
            §R 2
            §/F{f001}
            §/M{m002}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file2.calr"), source2);

        var source3 = """
            §M{m003:File3}
            §CL{c001:ClassC}
            §/CL{c001}
            §/M{m003}
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
            §/CL{c001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///models.calr"), source1);

        // Second document should have access to Customer type in completions
        var source2 = """
            §M{m002:Service}
            §F{f001:GetCustomer}
            §O{i32}
            §R 0
            §/F{f001}
            §/M{m002}
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
            §/F{f001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///utils.calr"), source1);

        // Second document should see Calculate in completions
        var source2 = """
            §M{m002:Main}
            §F{f001:Test}
            §O{i32}
            §R 0
            §/F{f001}
            §/M{m002}
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
            §/CL{c001}
            §/M{m001}
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
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///base.calr"), source1);

        // Second document defines a derived class
        var source2 = """
            §M{m002:Derived}
            §CL{c001:Dog}
            §EXT{Animal}
            §FLD{str:breed}
            §/CL{c001}
            §/M{m002}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/CL{c001}
            §/M{m001}
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
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
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
            §/CL{c001}
            §/M{m001}
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
            §/M{m001}
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
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
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
            §/CL{c001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///base.calr"), source1);

        // Derived class in another file
        var source2 = """
            §M{m002:Derived}
            §CL{c001:Dog}
            §EXT{Animal}
            §FLD{str:breed}
            §/CL{c001}
            §/M{m002}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///derived.calr"), source2);

        // Looking up "name" on Dog should find it in Animal
        var (doc, node) = workspace.FindMemberAcrossFiles("Dog", "name");

        Assert.NotNull(doc);
        Assert.NotNull(node);
        Assert.Contains("base.calr", doc.Uri.LocalPath);
    }

    [Fact]
    public void FindMemberAcrossFiles_NonExistentMember_ReturnsNull()
    {
        var workspace = new WorkspaceState();

        var source = """
            §M{m001:Models}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §/M{m001}
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
            §/CL{c001}
            §/M{m001}
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
            §/F{f001}
            §/M{m002}
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
            §/F{f001}
            §/M{m001}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file1.calr"), source1);

        // Second document also defines Helper
        var source2 = """
            §M{m002:File2}
            §F{f001:Helper:pub}
            §R 2
            §/F{f001}
            §/M{m002}
            """;
        workspace.GetOrCreate(DocumentUri.From("file:///file2.calr"), source2);

        // Should find one of them (first encountered)
        var (doc, node) = workspace.FindDefinitionAcrossFiles("Helper");
        Assert.NotNull(doc);
        Assert.NotNull(node);
    }

    #endregion
}
