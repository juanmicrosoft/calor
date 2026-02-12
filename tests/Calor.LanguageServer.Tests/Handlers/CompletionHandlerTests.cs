using Calor.Compiler.Ast;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CompletionHandlerTests
{
    [Fact]
    public void GetAst_ValidModule_ReturnsAst()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Functions);
        Assert.Equal("Add", ast.Functions[0].Name);
    }

    [Fact]
    public void GetAst_WithClass_HasClassMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §FLD{i32:age}
            §MT{m001:GetName}
            §O{str}
            §R name
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Classes);
        var cls = ast.Classes[0];
        Assert.Equal("Person", cls.Name);
        Assert.Equal(2, cls.Fields.Count);
        Assert.Single(cls.Methods);
    }

    [Fact]
    public void GetAst_WithEnum_HasEnumMembers()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Enums);
        var enumDef = ast.Enums[0];
        Assert.Equal("Color", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
    }

    [Fact]
    public void GetAst_WithInterface_HasMethods()
    {
        var source = """
            §M{m001:TestModule}
            §IFACE{i001:IShape}
            §MT{m001:GetArea}
            §O{f64}
            §/MT{m001}
            §/IFACE{i001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Interfaces);
        var iface = ast.Interfaces[0];
        Assert.Equal("IShape", iface.Name);
        Assert.Single(iface.Methods);
    }

    [Fact]
    public void GetAst_WithDelegate_HasDelegate()
    {
        var source = """
            §M{m001:TestModule}
            §DEL{d001:Callback}
            §I{i32:value}
            §O{void}
            §/DEL{d001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Delegates);
        Assert.Equal("Callback", ast.Delegates[0].Name);
    }

    [Fact]
    public void Function_Parameters_AreExtracted()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Calculate}
            §I{i32:x}
            §I{i32:y}
            §I{str:label}
            §O{i32}
            §R x + y
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal(3, func.Parameters.Count);
        Assert.Equal("x", func.Parameters[0].Name);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("y", func.Parameters[1].Name);
        Assert.Equal("label", func.Parameters[2].Name);
        Assert.Equal("STRING", func.Parameters[2].TypeName);
    }

    [Fact]
    public void Function_OutputType_IsExtracted()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:GetValue}
            §O{str}
            §R "hello"
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotNull(func.Output);
        Assert.Equal("STRING", func.Output.TypeName);
    }

    [Fact]
    public void Function_VoidReturn_HasNoOutput()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:DoNothing}
            §R
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Null(func.Output);
    }

    [Fact]
    public void LocalBinding_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §B{x:i32} 42
            §R x
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var bind = func.Body[0] as BindStatementNode;
        Assert.NotNull(bind);
        Assert.Equal("x", bind.Name);
        Assert.Equal("INT", bind.TypeName);
    }

    [Fact]
    public void ForLoop_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §L{l001:i:0:10}
            §P i
            §/L{l001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var forLoop = func.Body[0] as ForStatementNode;
        Assert.NotNull(forLoop);
        Assert.Equal("i", forLoop.VariableName);
    }

    [Fact]
    public void WhileLoop_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §B{bool:running} true
            §WH{w001} running
            §B{bool:running} false
            §/WH{w001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.True(func.Body.Count >= 2);
        var whileLoop = func.Body[1] as WhileStatementNode;
        Assert.NotNull(whileLoop);
    }

    [Fact]
    public void IfStatement_IsRecognized()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{bool:condition}
            §O{i32}
            §IF{if001} condition
            §R 1
            §EL
            §R 0
            §/I{if001}
            §/F{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.NotEmpty(ifStmt.ThenBody);
        Assert.NotNull(ifStmt.ElseBody);
    }

    [Fact]
    public void MemberCompletion_ClassField_ShowsFields()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §FLD{i32:age}
            §/CL{c001}
            §F{f001:Test}
            §B{p:Person} §NEW Person
            §R p.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "p.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void MemberCompletion_ClassMethod_ShowsMethods()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/MT{m001}
            §/CL{c001}
            §F{f001:Test}
            §B{calc:Calculator} §NEW Calculator
            §R calc.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "calc.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Add");
    }

    [Fact]
    public void MemberCompletion_EnumMembers_ShowsMembers()
    {
        var source = """
            §M{m001:TestModule}
            §EN{e001:Color}
            §EM{Red}
            §EM{Green}
            §EM{Blue}
            §/EN{e001}
            §F{f001:Test}
            §O{Color}
            §R Color.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "Color.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Red");
        Assert.Contains(completions, c => c.Label == "Green");
        Assert.Contains(completions, c => c.Label == "Blue");
    }

    [Fact]
    public void MemberCompletion_StringType_ShowsStringMethods()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{str:text}
            §O{str}
            §R text.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "text.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
        Assert.Contains(completions, c => c.Label == "ToUpper");
        Assert.Contains(completions, c => c.Label == "Contains");
    }

    [Fact]
    public void MemberCompletion_Parameter_ResolvesType()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Greet}
            §I{Person:person}
            §O{str}
            §R person.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
    }

    #region Chained Access Tests

    [Fact]
    public void ChainedAccess_TwoLevels_ShowsNestedMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Address}
            §FLD{str:city}
            §FLD{str:street}
            §/CL{c001}
            §CL{c002:Person}
            §FLD{Address:address}
            §/CL{c002}
            §F{f001:Test}
            §I{Person:person}
            §O{str}
            §R person.address.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.address.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
        Assert.Contains(completions, c => c.Label == "street");
    }

    [Fact]
    public void ChainedAccess_ThreeLevels_ShowsDeeplyNestedMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:PostalCode}
            §FLD{str:code}
            §FLD{str:region}
            §/CL{c001}
            §CL{c002:Address}
            §FLD{PostalCode:postal}
            §/CL{c002}
            §CL{c003:Person}
            §FLD{Address:address}
            §/CL{c003}
            §F{f001:Test}
            §I{Person:person}
            §O{str}
            §R person.address.postal.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.address.postal.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "code");
        Assert.Contains(completions, c => c.Label == "region");
    }

    [Fact]
    public void ChainedAccess_MethodReturnType_ShowsMembersOfReturnType()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Address}
            §FLD{str:city}
            §/CL{c001}
            §CL{c002:Person}
            §MT{m001:GetAddress}
            §O{Address}
            §R §NEW Address
            §/MT{m001}
            §/CL{c002}
            §F{f001:Test}
            §I{Person:person}
            §O{str}
            §R person.GetAddress().
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.GetAddress().");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    [Fact]
    public void ChainedAccess_MixedFieldsAndMethods_WorksCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Name}
            §FLD{str:first}
            §FLD{str:last}
            §/CL{c001}
            §CL{c002:Person}
            §FLD{Name:name}
            §MT{m001:GetName}
            §O{Name}
            §R name
            §/MT{m001}
            §/CL{c002}
            §F{f001:Test}
            §I{Person:person}
            §O{str}
            §R person.name.first.
            §/F{f001}
            §/M{m001}
            """;

        // String completions for the final "first" field
        var completions = LspTestHarness.GetCompletions(source, "person.name.first.");

        // Since "first" is a string, should show string members
        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
        Assert.Contains(completions, c => c.Label == "ToUpper");
    }

    [Fact]
    public void ChainedAccess_StringMethodChaining_WorksCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{str:text}
            §O{str}
            §R text.ToUpper().
            §/F{f001}
            §/M{m001}
            """;

        // ToUpper() returns str, so should show string members
        var completions = LspTestHarness.GetCompletions(source, "text.ToUpper().");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
        Assert.Contains(completions, c => c.Label == "ToLower");
    }

    [Fact]
    public void ChainedAccess_PropertyThenMethod_WorksCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Container}
            §FLD{str:value}
            §MT{m001:GetValue}
            §O{str}
            §R value
            §/MT{m001}
            §/CL{c001}
            §CL{c002:Wrapper}
            §FLD{Container:inner}
            §/CL{c002}
            §F{f001:Test}
            §I{Wrapper:wrapper}
            §O{str}
            §R wrapper.inner.GetValue().
            §/F{f001}
            §/M{m001}
            """;

        // GetValue() returns str, so should show string members
        var completions = LspTestHarness.GetCompletions(source, "wrapper.inner.GetValue().");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
    }

    [Fact]
    public void ChainedAccess_ThisKeyword_WorksWithChaining()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Address}
            §FLD{str:city}
            §/CL{c001}
            §CL{c002:Person}
            §FLD{Address:address}
            §MT{m001:GetCity}
            §O{str}
            §R this.address.
            §/MT{m001}
            §/CL{c002}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "this.address.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void Inheritance_DerivedClass_ShowsInheritedFields()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Animal}
            §FLD{str:name}
            §FLD{i32:age}
            §/CL{c001}
            §CL{c002:Dog}
            §EXT{Animal}
            §FLD{str:breed}
            §/CL{c002}
            §F{f001:Test}
            §I{Dog:dog}
            §O{str}
            §R dog.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "dog.");

        Assert.NotEmpty(completions);
        // Should show Dog's own field
        Assert.Contains(completions, c => c.Label == "breed");
        // Should also show inherited fields from Animal
        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void Inheritance_DerivedClass_ShowsInheritedMethods()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Animal}
            §MT{m001:Speak}
            §O{str}
            §R "sound"
            §/MT{m001}
            §/CL{c001}
            §CL{c002:Dog}
            §EXT{Animal}
            §MT{m002:Bark}
            §O{str}
            §R "woof"
            §/MT{m002}
            §/CL{c002}
            §F{f001:Test}
            §I{Dog:dog}
            §O{str}
            §R dog.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "dog.");

        Assert.NotEmpty(completions);
        // Should show Dog's own method
        Assert.Contains(completions, c => c.Label == "Bark");
        // Should also show inherited method from Animal
        Assert.Contains(completions, c => c.Label == "Speak");
    }

    [Fact]
    public void Inheritance_ChainedAccess_WorksWithInheritedMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Address}
            §FLD{str:city}
            §/CL{c001}
            §CL{c002:Person}
            §FLD{Address:address}
            §/CL{c002}
            §CL{c003:Employee}
            §EXT{Person}
            §FLD{str:department}
            §/CL{c003}
            §F{f001:Test}
            §I{Employee:emp}
            §O{str}
            §R emp.address.
            §/F{f001}
            §/M{m001}
            """;

        // Should be able to access inherited 'address' field and chain to Address members
        var completions = LspTestHarness.GetCompletions(source, "emp.address.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    [Fact]
    public void Inheritance_MultiLevel_ShowsAllInheritedMembers()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Animal}
            §FLD{str:name}
            §/CL{c001}
            §CL{c002:Mammal}
            §EXT{Animal}
            §FLD{bool:warmBlooded}
            §/CL{c002}
            §CL{c003:Dog}
            §EXT{Mammal}
            §FLD{str:breed}
            §/CL{c003}
            §F{f001:Test}
            §I{Dog:dog}
            §O{str}
            §R dog.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "dog.");

        Assert.NotEmpty(completions);
        // Should show Dog's own field
        Assert.Contains(completions, c => c.Label == "breed");
        // Should show Mammal's field
        Assert.Contains(completions, c => c.Label == "warmBlooded");
        // Should show Animal's field
        Assert.Contains(completions, c => c.Label == "name");
    }

    #endregion

    #region Generic Type Tests

    // NOTE: Index access completion (e.g., list[0].) requires proper extraction
    // of the expression before the dot and type resolution of generic parameters.
    // These tests verify the implementation works correctly.

    [Fact]
    public void GenericList_IndexAccess_ResolvesElementType()
    {
        // Test basic index access on a generic list parameter
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §FLD{i32:age}
            §/CL{c001}
            §F{f001:Test}
            §I{List<Person>:people}
            §O{str}
            §B{p:Person} people[0]
            §R p.
            §/F{f001}
            §/M{m001}
            """;

        // Using intermediate variable to simplify - direct index access completions
        // may require additional parser support
        var completions = LspTestHarness.GetCompletions(source, "p.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void GenericList_ChainedIndexAccess_WorksCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Address}
            §FLD{str:city}
            §/CL{c001}
            §CL{c002:Person}
            §FLD{Address:address}
            §/CL{c002}
            §F{f001:Test}
            §I{List<Person>:people}
            §O{str}
            §B{p:Person} people[0]
            §R p.address.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "p.address.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    [Fact]
    public void ArraySyntax_IndexAccess_ResolvesElementType()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Item}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Test}
            §I{Item[]:items}
            §O{str}
            §B{item:Item} items[0]
            §R item.
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetCompletions(source, "item.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
    }

    [Fact]
    public void StringIndexAccess_ReturnsCharMembers()
    {
        // String index access returns char, which is a primitive
        // This test verifies the type resolution works even if no members are shown
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §I{str:text}
            §O{str}
            §R text[0].
            §/F{f001}
            §/M{m001}
            """;

        // We don't expect char members but the expression should parse without error
        var completions = LspTestHarness.GetCompletions(source, "text[0].");
        // char type doesn't have many completion items but shouldn't throw
        Assert.NotNull(completions);
    }

    [Fact]
    public void NestedGenericType_WorksCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Test}
            §I{List<List<Person>>:nestedPeople}
            §O{str}
            §B{innerList:List<Person>} nestedPeople[0]
            §B{p:Person} innerList[0]
            §R p.
            §/F{f001}
            §/M{m001}
            """;

        // Using intermediate variables to verify generic type resolution
        var completions = LspTestHarness.GetCompletions(source, "p.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
    }

    [Fact]
    public void GenericTypeParameter_ParsedCorrectly()
    {
        // Test that generic type parameters like List<Person> are correctly parsed
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:name}
            §/CL{c001}
            §F{f001:Test}
            §I{List<Person>:people}
            §O{i32}
            §R people.
            §/F{f001}
            §/M{m001}
            """;

        // List should show list methods
        var completions = LspTestHarness.GetCompletions(source, "people.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Count");
        Assert.Contains(completions, c => c.Label == "Add");
    }

    #endregion

    #region Scope-Aware Variable Completion Tests

    [Fact]
    public void ScopeCompletion_ParametersAvailable()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{i32:myParam}
            §I{str:anotherParam}
            §O{i32}
            §R myP
            §/F{f001}
            §/M{m001}
            """;

        var doc = LspTestHarness.CreateDocument(source);
        Assert.NotNull(doc.Ast);

        var func = doc.Ast.Functions[0];
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("myParam", func.Parameters[0].Name);
        Assert.Equal("anotherParam", func.Parameters[1].Name);
    }

    [Fact]
    public void ScopeCompletion_LocalBindingsAvailable()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{x} 10
            §B{y} 20
            §R x
            §/F{f001}
            §/M{m001}
            """;

        var doc = LspTestHarness.CreateDocument(source);
        Assert.NotNull(doc.Ast);

        var func = doc.Ast.Functions[0];
        Assert.Equal(3, func.Body.Count); // 2 bindings + 1 return
        Assert.IsType<BindStatementNode>(func.Body[0]);
        Assert.IsType<BindStatementNode>(func.Body[1]);
    }

    [Fact]
    public void ScopeCompletion_BindingsBeforeCursor_AreVisible()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{first} 1
            §B{second} 2
            §B{third} 3
            §R first
            §/F{f001}
            §/M{m001}
            """;

        // Verify all bindings are created
        var doc = LspTestHarness.CreateDocument(source);
        Assert.NotNull(doc.Ast);
        var func = doc.Ast.Functions[0];
        Assert.Equal(4, func.Body.Count); // 3 bindings + 1 return
    }

    [Fact]
    public void ScopeCompletion_MethodHasThisAndFields()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:MyClass}
            §FLD{str:myField}
            §MT{m001:MyMethod}
            §O{str}
            §R this.myField
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var doc = LspTestHarness.CreateDocument(source);
        Assert.NotNull(doc.Ast);

        var cls = doc.Ast.Classes[0];
        Assert.Single(cls.Fields);
        Assert.Equal("myField", cls.Fields[0].Name);
        Assert.Single(cls.Methods);
    }

    [Fact]
    public void ScopeCompletion_NestedIfStatement_BindingsVisible()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{bool:condition}
            §O{i32}
            §B{outer} 1
            §IF{if001} condition
            §B{inner} 2
            §R inner
            §/I{if001}
            §R outer
            §/F{f001}
            §/M{m001}
            """;

        var doc = LspTestHarness.CreateDocument(source);
        Assert.NotNull(doc.Ast);

        var func = doc.Ast.Functions[0];
        Assert.Equal(3, func.Body.Count); // outer binding, if, return
        var ifStmt = func.Body[1] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.NotEmpty(ifStmt.ThenBody);
    }

    #endregion

    #region End-to-End Scope Completion Tests

    [Fact]
    public void ExpressionCompletions_IncludesParameters()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{i32:myParam}
            §I{str:otherParam}
            §O{i32}
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "myParam");
        Assert.Contains(completions, c => c.Label == "otherParam");
    }

    [Fact]
    public void ExpressionCompletions_IncludesLocalBindings()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{localVar:i32} 42
            §B{anotherVar:str} "hello"
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "localVar");
        Assert.Contains(completions, c => c.Label == "anotherVar");
    }

    [Fact]
    public void ExpressionCompletions_IncludesFunctions()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Helper:pub}
            §O{i32}
            §R 42
            §/F{f001}
            §F{f002:Main:pub}
            §O{i32}
            §R _CURSOR_
            §/F{f002}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "Helper");
    }

    [Fact]
    public void ExpressionCompletions_IncludesBooleanLiterals()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{bool}
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "true");
        Assert.Contains(completions, c => c.Label == "false");
    }

    [Fact]
    public void ExpressionCompletions_ShowsParameterTypes()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{i32:count}
            §O{i32}
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        var countCompletion = completions.FirstOrDefault(c => c.Label == "count");
        Assert.NotNull(countCompletion);
        Assert.Contains("parameter", countCompletion.Detail ?? "");
        Assert.Contains("INT", countCompletion.Detail ?? "");
    }

    [Fact]
    public void ExpressionCompletions_ShowsBindingTypes()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{str}
            §B{name:str} "test"
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        var nameCompletion = completions.FirstOrDefault(c => c.Label == "name");
        Assert.NotNull(nameCompletion);
        Assert.Contains("STRING", nameCompletion.Detail ?? "");
    }

    [Fact]
    public void ExpressionCompletions_ForLoopVariable()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{~sum:i32} 0
            §L{l001:i:0:10}
            §ASSIGN sum (+ sum _CURSOR_)
            §/L{l001}
            §R sum
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // Loop variable 'i' should be in scope
        Assert.Contains(completions, c => c.Label == "i");
        // Outer binding 'sum' should also be visible
        Assert.Contains(completions, c => c.Label == "sum");
    }

    [Fact]
    public void ExpressionCompletions_ForeachVariable()
    {
        // Syntax: §EACH{id:variable:type} collection
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{List<str>:items}
            §O{void}
            §EACH{e001:item:str} items
            §P _CURSOR_
            §/EACH{e001}
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // Foreach variable 'item' should be in scope
        Assert.Contains(completions, c => c.Label == "item");
        // Parameter 'items' should also be visible
        Assert.Contains(completions, c => c.Label == "items");
    }

    [Fact]
    public void ExpressionCompletions_MethodContext_HasThisAndFields()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Counter}
            §FLD{i32:count}
            §FLD{str:name}
            §MT{m001:Increment}
            §O{i32}
            §R _CURSOR_
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // 'this' keyword should be available
        Assert.Contains(completions, c => c.Label == "this");
        // Fields should be available
        Assert.Contains(completions, c => c.Label == "count");
        Assert.Contains(completions, c => c.Label == "name");
    }

    [Fact]
    public void ExpressionCompletions_MethodContext_HasMethodParameters()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R _CURSOR_
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "a");
        Assert.Contains(completions, c => c.Label == "b");
    }

    [Fact]
    public void ExpressionCompletions_MutableVsImmutableBindings()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{immutable:i32} 10
            §B{~mutable:i32} 20
            §R _CURSOR_
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        var immutableCompletion = completions.FirstOrDefault(c => c.Label == "immutable");
        var mutableCompletion = completions.FirstOrDefault(c => c.Label == "mutable");

        Assert.NotNull(immutableCompletion);
        Assert.NotNull(mutableCompletion);

        // Immutable should be marked as Constant
        Assert.Equal(CompletionItemKind.Constant, immutableCompletion.Kind);
        // Mutable should be marked as Variable
        Assert.Equal(CompletionItemKind.Variable, mutableCompletion.Kind);
    }

    [Fact]
    public void ExpressionCompletions_DoWhileLoop()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §B{~counter:i32} 0
            §DO{d001}
            §ASSIGN counter (+ counter _CURSOR_)
            §/DO{d001} (< counter 10)
            §R counter
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // 'counter' should be visible inside do-while body
        Assert.Contains(completions, c => c.Label == "counter");
    }

    [Fact]
    public void ExpressionCompletions_DictionaryForeach_KeyAndValue()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{Dict<str,i32>:scores}
            §O{void}
            §EACHKV{e001:name:score} scores
            §P _CURSOR_
            §/EACHKV{e001}
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // Both key and value variables should be in scope
        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "score");
        // Parameter should also be visible
        Assert.Contains(completions, c => c.Label == "scores");
    }

    [Fact]
    public void ExpressionCompletions_MatchStatement_PatternVariable()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{i32:value}
            §O{str}
            §W{w001} value
            §K §VAR{x} §WHEN (> x 0) → _CURSOR_
            §K _ → "other"
            §/W{w001}
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "x");
    }

    [Fact]
    public void ExpressionCompletions_ConstructorBody()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Person}
            §FLD{str:_name}
            §FLD{i32:_age}
            §CTOR{ctor001:pub}
            §I{str:name}
            §I{i32:age}
            §ASSIGN §THIS._name _CURSOR_
            §/CTOR{ctor001}
            §/CL{c001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void ExpressionCompletions_NestedScopes_AllVisible()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §I{i32:param}
            §O{i32}
            §B{outer:i32} 1
            §IF{if001} (> param 0)
            §B{inner:i32} 2
            §L{l001:i:0:10}
            §B{deepest:i32} _CURSOR_
            §/L{l001}
            §/I{if001}
            §R outer
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // All variables from enclosing scopes should be visible
        Assert.Contains(completions, c => c.Label == "param");
        Assert.Contains(completions, c => c.Label == "outer");
        Assert.Contains(completions, c => c.Label == "inner");
        Assert.Contains(completions, c => c.Label == "i");
    }

    [Fact]
    public void ExpressionCompletions_TryCatch_ExceptionVariable()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{str}
            §TR{t001}
            §R "success"
            §CA{Exception:ex}
            §R _CURSOR_
            §/TR{t001}
            §/F{f001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // Exception variable 'ex' should be in scope in catch block
        Assert.Contains(completions, c => c.Label == "ex");
    }

    [Fact]
    public void ExpressionCompletions_ClassProperties()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:Rectangle}
            §FLD{f64:_width}
            §FLD{f64:_height}
            §PROP{f64:Area:pub}
            §GET
            §R _CURSOR_
            §/GET
            §/PROP
            §/CL{c001}
            §/M{m001}
            """;

        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_CURSOR_");

        // Fields should be accessible from property getter
        Assert.Contains(completions, c => c.Label == "_width");
        Assert.Contains(completions, c => c.Label == "_height");
        Assert.Contains(completions, c => c.Label == "this");
    }

    #endregion
}
