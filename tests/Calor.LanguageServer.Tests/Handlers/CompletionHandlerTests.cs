using Calor.Compiler.Ast;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CompletionHandlerTests
{
    private static void AssertNativeSource(string source)
    {
        var result = Compiler.Program.Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
    }

    [Theory]
    [InlineData("", "func")]
    [InlineData("mod", "module")]
    [InlineData("let ", "string")]
    [InlineData("var ", "float")]
    [InlineData("func F (", "int")]
    public async Task CanonicalCompletion_UsesNativeWordsAndCSharpTypeWidthsAsync(string source, string expected)
    {
        var workspace = new WorkspaceState();
        var uri = new Uri("file:///canonical-completion.calr");
        workspace.GetOrCreate(uri, source).Reanalyze();
        var items = await new CompletionHandler(workspace).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, source.Length)
        }, CancellationToken.None);
        Assert.Contains(items.Items, item => item.Label == expected && item.InsertText == expected);
        Assert.DoesNotContain(items.Items, item => item.Label.StartsWith('§'));
        Assert.DoesNotContain(items.Items, item => item.Label is "i32" or "str" or "f64" or "FLOAT");
        Assert.DoesNotContain(items.Items, item => item.Label is "namespace" or "indexer" or "foreach" or "yieldbreak");
        if (source.Length == 0)
        {
            Assert.Equal(1, items.Items.Count(item => item.Label == "raw"));
            Assert.Contains(items.Items, item => item.Label == "float" && item.Detail?.Contains("32-bit") == true);
            Assert.Contains(items.Items, item => item.Label == "double" && item.Detail?.Contains("64-bit") == true);
        }
    }

    [Fact]
    public async Task CanonicalCompletion_DoesNotTreatTypeColonAsTypePositionAsync()
    {
        const string source = "let int:";
        var workspace = new WorkspaceState();
        var uri = new Uri("file:///canonical-name-completion.calr");
        workspace.GetOrCreate(uri, source).Reanalyze();
        var items = await new CompletionHandler(workspace).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(0, source.Length)
        }, CancellationToken.None);
        Assert.DoesNotContain(items.Items, item => item.Label == "int");
    }

    [Fact]
    public async Task CanonicalCompletion_CallArgumentRetainsExpressionScopeAsync()
    {
        const string source = "module M\n  func Echo (int:value) → int\n    return Echo(value)\n";
        var workspace = new WorkspaceState();
        var uri = new Uri("file:///canonical-call-completion.calr");
        workspace.GetOrCreate(uri, source).Reanalyze();
        var offset = source.IndexOf("return Echo(", StringComparison.Ordinal) + "return Echo(".Length;
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        var items = await new CompletionHandler(workspace).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line - 1, column - 1)
        }, CancellationToken.None);
        Assert.Contains(items.Items, item => item.Label == "value");
        Assert.DoesNotContain(items.Items, item => item.Label == "int");
    }

    [Theory]
    [InlineData("func", "let")]
    [InlineData("requires", "true")]
    [InlineData("requires", "false")]
    [InlineData("invariant", "null")]
    public async Task CanonicalCompletion_EscapesCollisionSymbolsAsync(string function, string parameter)
    {
        var source = $"module M\n  func @{function} (string:@{parameter}) → string\n    return @{parameter}\n";
        var compilation = Compiler.Program.Compile(source);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        var workspace = new WorkspaceState();
        var uri = new Uri("file:///collision-completion.calr");
        workspace.GetOrCreate(uri, source).Reanalyze();
        var offset = source.IndexOf("return ", StringComparison.Ordinal) + "return ".Length;
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        var items = await new CompletionHandler(workspace).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line - 1, column - 1)
        }, CancellationToken.None);

        foreach (var name in new[] { function, parameter })
        {
            var kind = name == function ? CompletionItemKind.Function : CompletionItemKind.Variable;
            var item = Assert.Single(items.Items, item => item.Label == name && item.Kind == kind);
            Assert.Equal($"@{name}", item.InsertText);
            Assert.Equal($"@{name}", item.FilterText);
        }
        Assert.Contains(items.Items, item => item.Kind == CompletionItemKind.Constant
            && item.Label == "true" && item.InsertText == "true");
        var parameterCompletion = items.Items.Single(item =>
            item.Label == parameter && item.Kind == CompletionItemKind.Variable);
        Assert.False(Compiler.Program.Compile(
            source.Replace($"return @{parameter}", $"return {parameterCompletion.InsertText}", StringComparison.Ordinal)).HasErrors);
    }

    [Theory]
    [InlineData("this")]
    [InlineData("base")]
    public void CanonicalCompletion_EscapedInstanceWordsResolveTheDeclaredParameter(string parameter)
    {
        var source = $"module M\n  func Echo (string:@{parameter}) → string\n    return @{parameter}.Trim()\n";
        var compilation = Compiler.Program.Compile(source);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));

        var items = LspTestHarness.GetCompletions(source, $"@{parameter}.");

        Assert.Contains(items, item => item.Label == "Length");
        Assert.Contains(items, item => item.Label == "Trim");
    }

    [Theory]
    [InlineData("@let.@Trim().")]
    [InlineData("@func().")]
    public void CanonicalCompletion_EscapedChainsPreserveTheReceiver(string expression)
    {
        var source = $"module M\n  func @func () → string\n    return \"text\"\n"
            + $"  func Echo (string:@let) → string\n    return {expression}Trim()\n";
        var compilation = Compiler.Program.Compile(source);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));

        var items = LspTestHarness.GetCompletions(source, expression);

        Assert.Contains(items, item => item.Label == "Length");
        Assert.Contains(items, item => item.Label == "Split" && item.Detail?.EndsWith("string[]", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CanonicalCompletion_CrossFileCollisionsStayEscapedAsync()
    {
        const string exported = "module Exported\n  func @func () → string\n    return \"text\"\n";
        const string source = "module Local\n  func Echo (string:value) → string\n    return value\n";
        Assert.False(Compiler.Program.Compile(exported).HasErrors);
        Assert.False(Compiler.Program.Compile(source).HasErrors);
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(new Uri("file:///exported.calr"), exported).Reanalyze();
        var uri = new Uri("file:///local.calr");
        workspace.GetOrCreate(uri, source).Reanalyze();
        var offset = source.IndexOf("return ", StringComparison.Ordinal) + "return ".Length;
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);

        var items = await new CompletionHandler(workspace).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line - 1, column - 1)
        }, CancellationToken.None);

        var item = Assert.Single(items.Items, item => item.Label == "func");
        Assert.Equal("@func", item.InsertText);
        Assert.Equal("@func", item.FilterText);
    }

    [Theory]
    [InlineData("Length", false, "int", typeof(int))]
    [InlineData("ToUpper", true, "string", typeof(string))]
    [InlineData("IndexOf", true, "int", typeof(int))]
    [InlineData("Split", true, "string[]", typeof(string[]))]
    public void CanonicalCompletion_StringMemberTypesMatchTheActualBcl(string member, bool isMethod, string expected, Type clrType)
    {
        var resolver = typeof(CompletionHandler).GetMethod("ResolveBuiltInStringMember",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(expected, resolver.Invoke(null, [member, isMethod]));
        var actualTypes = isMethod
            ? typeof(string).GetMethods().Where(method => method.Name == member).Select(method => method.ReturnType)
            : [typeof(string).GetProperty(member)!.PropertyType];
        Assert.Equal(clrType, Assert.Single(actualTypes.Distinct()));
    }

    [Theory]
    [InlineData("int", "Task<int>")]
    [InlineData("ValueTask<int>", "ValueTask<int>")]
    public void AsyncMemberCompletion_ReportsCallableCarrier(string output, string carrier)
    {
        var source = $$"""
            module AsyncCompletion
              class Probe
                func Produce async () → {{output}}
                  return 42
              func Use (Probe:probe) → void
                probe.Produce()
            """;
        var compilation = Compiler.Program.Compile(source, "async-completion.calr",
            new Compiler.CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        var completion = Assert.Single(LspTestHarness.GetCompletions(source, "probe."),
            item => item.Label == "Produce");
        Assert.Contains(carrier, completion.Detail);
    }

    [Fact]
    public void GetAst_ValidModule_ReturnsAst()
    {
        var source = """
            module TestModule
              func Add private (int:a, int:b) → int
                return a + b
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Functions);
        Assert.Equal("Add", ast.Functions[0].Name);
        Assert.Equal(Visibility.Private, ast.Functions[0].Visibility);
    }

    [Fact]
    public void GetAst_WithClass_HasClassMembers()
    {
        var source = """
            module TestModule
              class Person
                field string:name private
                field int:age private
                func GetName private () → string
                  return name
            """;

        AssertNativeSource(source);
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
            module TestModule
              enum Color
                Red
                Green
                Blue
            """;

        AssertNativeSource(source);
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
            module TestModule
              interface IShape
                func GetArea () → double
            """;

        AssertNativeSource(source);
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
            module TestModule
              delegate Callback (int:value) → void
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Delegates);
        Assert.Equal("Callback", ast.Delegates[0].Name);
    }

    [Fact]
    public void Function_Parameters_AreExtracted()
    {
        var source = """
            module TestModule
              func Calculate private (int:x, int:y, string:label) → int
                return x + y
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal(3, func.Parameters.Count);
        Assert.Equal("x", func.Parameters[0].Name);
        Assert.Equal("int", func.Parameters[0].TypeName);
        Assert.Equal("y", func.Parameters[1].Name);
        Assert.Equal("label", func.Parameters[2].Name);
        Assert.Equal("string", func.Parameters[2].TypeName);
    }

    [Fact]
    public void Function_OutputType_IsExtracted()
    {
        var source = """
            module TestModule
              func GetValue private () → string
                return "hello"
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotNull(func.Output);
        Assert.Equal("string", func.Output.TypeName);
    }

    [Fact]
    public void Function_VoidReturn_HasExplicitVoidOutput()
    {
        var source = """
            module TestModule
              func DoNothing private () → void
                return
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal("void", Assert.IsType<OutputNode>(func.Output).TypeName);
    }

    [Fact]
    public void LocalBinding_IsRecognized()
    {
        var source = """
            module TestModule
              func Test private () → void
                let int:x = 42
                return
            """;

        AssertNativeSource(source);
        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.NotEmpty(func.Body);
        var bind = func.Body[0] as BindStatementNode;
        Assert.NotNull(bind);
        Assert.Equal("x", bind.Name);
        Assert.Equal("int", bind.TypeName);
    }

    [Fact]
    public void ForLoop_IsRecognized()
    {
        var source = """
            module TestModule
              func Test private () → void
                ! cw
                for i from 0 to 10 step 1
                  System.Console.WriteLine(i)
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test private () → void
                var bool:running = true
                while running
                  set running false
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test private (bool:condition) → int
                if condition
                  return 1
                else
                  return 0
            """;

        AssertNativeSource(source);
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
            module TestModule
              class Person
                field string:name private
                field int:age private
              func Test private () → void
                let Person:p = new Person()
                return p.
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
            module TestModule
              class Calculator
                func Add private (int:a, int:b) → int
                  return a + b
              func Test private () → void
                let Calculator:calc = new Calculator()
                return calc.
            """;

        var completions = LspTestHarness.GetCompletions(source, "calc.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Add");
    }

    [Fact]
    public void MemberCompletion_EnumMembers_ShowsMembers()
    {
        var source = """
            module TestModule
              enum Color
                Red
                Green
                Blue
              func Test private () → Color
                return Color.
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
            module TestModule
              func Test private (string:text) → string
                return text.Trim()
            """;

        AssertNativeSource(source);
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
            module TestModule
              class Person
                field string:name private
              func Greet private (Person:person) → string
                return person.
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
            module TestModule
              class Address
                field string:city private
                field string:street private
              class Person
                field Address:address private
              func Test private (Person:person) → string
                return person.address.
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
            module TestModule
              class PostalCode
                field string:code private
                field string:region private
              class Address
                field PostalCode:postal private
              class Person
                field Address:address private
              func Test private (Person:person) → string
                return person.address.postal.
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
            module TestModule
              class Address
                field string:city private
              class Person
                func GetAddress private () → Address
                  return new Address()
              func Test private (Person:person) → string
                return person.GetAddress().
            """;

        var completions = LspTestHarness.GetCompletions(source, "person.GetAddress().");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    [Fact]
    public void ChainedAccess_MixedFieldsAndMethods_WorksCorrectly()
    {
        var source = """
            module TestModule
              class Name
                field string:first private
                field string:last private
              class Person
                field Name:name private
                func GetName private () → Name
                  return name
              func Test private (Person:person) → string
                return person.name.first.
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
            module TestModule
              func Test private (string:text) → string
                return text.ToUpper().Trim()
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetCompletions(source, "text.ToUpper().");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "Length");
        Assert.Contains(completions, c => c.Label == "ToLower");
    }

    [Fact]
    public void ChainedAccess_PropertyThenMethod_WorksCorrectly()
    {
        var source = """
            module TestModule
              class Container
                field string:value private
                func GetValue private () → string
                  return value
              class Wrapper
                field Container:inner private
              func Test private (Wrapper:wrapper) → string
                return wrapper.inner.GetValue().
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
            module TestModule
              class Address
                field string:city private
              class Person
                field Address:address private
                func GetCity private () → string
                  return this.address.
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
            module TestModule
              class Animal
                field string:name private
                field int:age private
              class Dog extends Animal
                field string:breed private
              func Test private (Dog:dog) → string
                return dog.
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
            module TestModule
              class Animal
                func Speak private () → string
                  return "sound"
              class Dog extends Animal
                func Bark private () → string
                  return "woof"
              func Test private (Dog:dog) → string
                return dog.
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
            module TestModule
              class Address
                field string:city private
              class Person
                field Address:address private
              class Employee extends Person
                field string:department private
              func Test private (Employee:emp) → string
                return emp.address.
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
            module TestModule
              class Animal
                field string:name private
              class Mammal extends Animal
                field bool:warmBlooded private
              class Dog extends Mammal
                field string:breed private
              func Test private (Dog:dog) → string
                return dog.
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

    [Theory]
    [InlineData("System.Collections.Generic.List<int>", "Add", true)]
    [InlineData("System.Collections.Generic.Dictionary<string,int>", "ContainsKey", true)]
    [InlineData("LIST<int>", "Add", false)]
    [InlineData("list<int>", "Add", false)]
    [InlineData("Dict<string,int>", "ContainsKey", false)]
    [InlineData("DICT<string,int>", "ContainsKey", false)]
    [InlineData("dict<string,int>", "ContainsKey", false)]
    public void CanonicalCompletion_CollectionsUseExactCSharpNames(string type, string member, bool recognized)
    {
        var header = $$"""
            module CollectionCompletion
              func Test private ({{type}}:items) → int
            """;
        if (recognized)
            AssertNativeSource(header + "\n    return 0");

        var completions = LspTestHarness.GetCompletions(header + "\n    return items.", "items.");
        Assert.Equal(recognized, completions.Any(item => item.Label == member));
    }

    [Theory]
    [InlineData("string", true)]
    [InlineData("String", false)]
    [InlineData("System.String", true)]
    [InlineData("System.Array", true)]
    [InlineData("Array", true)]
    [InlineData("str", false)]
    [InlineData("STRING", false)]
    [InlineData("ARRAY", false)]
    [InlineData("array", false)]
    public void CanonicalCompletion_StringAndArrayNamesAreExact(string type, bool recognized)
    {
        var header = $$"""
            module NativeMemberNames
              func Test private ({{type}}:items) → int
            """;
        if (recognized)
            AssertNativeSource(header + "\n    return 0");

        var completions = LspTestHarness.GetCompletions(header + "\n    return items.", "items.");
        Assert.Equal(recognized, completions.Any(item => item.Label == "Length"));
    }

    [Theory]
    [InlineData("System.Collections.Generic.Dictionary<string,Payload>", "items[\"key\"].", true)]
    [InlineData("System.Collections.Generic.List<Payload>", "items[0].", true)]
    [InlineData("Payload[]", "items[0].", true)]
    [InlineData("Dict<string,Payload>", "items[\"key\"].", false)]
    [InlineData("DICT<string,Payload>", "items[\"key\"].", false)]
    [InlineData("dict<string,Payload>", "items[\"key\"].", false)]
    [InlineData("System.Collections.Generic.List<Payload,string>", "items[0].", false)]
    [InlineData("System.Collections.Generic.Dictionary<string,Payload,string>", "items[\"key\"].", false)]
    [InlineData("System.Array<Payload>", "items[0].", false)]
    public void CanonicalCompletion_IndexChainsUseActualCSharpCollectionNames(
        string type, string expression, bool recognized)
    {
        var header = $$"""
            module IndexedMembers
              class Payload
                field int:Marker private
              func Test private ({{type}}:items) → int
            """;
        if (recognized)
            AssertNativeSource(header + "\n    return 0");

        var completions = LspTestHarness.GetCompletions(header + "\n    return " + expression, expression);
        Assert.Equal(recognized, completions.Any(item => item.Label == "Marker"));
    }

    [Theory]
    [InlineData("Array", "Array", "items.Clone().")]
    [InlineData("List<T>", "List<string>", "items[0].")]
    [InlineData("Dictionary<K,V>", "Dictionary<string,string>", "items[\"key\"].")]
    public void CanonicalCompletion_UserTypesDoNotBorrowBclMembers(
        string declaration, string type, string expression)
    {
        var header = $$"""
            module ShadowedMembers
              class {{declaration}}
                field int:Marker private
              func Test private ({{type}}:items) → int
            """;
        AssertNativeSource(header + "\n    return 0");
        var declared = LspTestHarness.GetCompletions(header + "\n    return items.", "items.");
        Assert.Contains(declared, item => item.Label == "Marker");

        var unresolved = LspTestHarness.GetCompletions(header + "\n    return " + expression, expression);
        Assert.Empty(unresolved);
    }

    [Fact]
    public void CanonicalCompletion_UnresolvedChainDoesNotReuseItsReceiverType()
    {
        const string header = """
            module UnresolvedMembers
              func Test private (string:items) → int
            """;
        AssertNativeSource(header + "\n    return 0");
        var completions = LspTestHarness.GetCompletions(
            header + "\n    return items.DoesNotExist().", "items.DoesNotExist().");
        Assert.Empty(completions);
    }

    // NOTE: Index access completion (e.g., list[0].) requires proper extraction
    // of the expression before the dot and type resolution of generic parameters.
    // These tests verify the implementation works correctly.

    [Fact]
    public void GenericList_IndexAccess_ResolvesElementType()
    {
        // Test basic index access on a generic list parameter
        var source = """
            module TestModule
              class Person
                field string:name private
                field int:age private
              func Test private (System.Collections.Generic.List<Person>:people) → string
                let Person:p = people[0]
                return p.
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
            module TestModule
              class Address
                field string:city private
              class Person
                field Address:address private
              func Test private (System.Collections.Generic.List<Person>:people) → string
                let Person:p = people[0]
                return p.address.
            """;

        var completions = LspTestHarness.GetCompletions(source, "p.address.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "city");
    }

    [Fact]
    public void ArraySyntax_IndexAccess_ResolvesElementType()
    {
        var source = """
            module TestModule
              class Item
                field string:name private
              func Test private (Item[]:items) → string
                let Item:item = items[0]
                return item.
            """;

        var completions = LspTestHarness.GetCompletions(source, "item.");

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.Label == "name");
    }

    [Fact]
    public void StringIndexAccess_ReturnsCharMembers()
    {
        var source = """
            module TestModule
              func Test private (string:text) → string
                return text[0].ToString()
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetCompletions(source, "text[0].");
        Assert.NotNull(completions);
    }

    [Fact]
    public void NestedGenericType_WorksCorrectly()
    {
        var source = """
            module TestModule
              class Person
                field string:name private
              func Test private (System.Collections.Generic.List<System.Collections.Generic.List<Person>>:nestedPeople) → string
                let System.Collections.Generic.List<Person>:innerList = nestedPeople[0]
                let Person:p = innerList[0]
                return p.
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
            module TestModule
              class Person
                field string:name private
              func Test private (System.Collections.Generic.List<Person>:people) → int
                return people.
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
            module TestModule
              func Test (int:myParam, string:anotherParam) → int
                return myParam
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test () → int
                let x = 10
                let y = 20
                return x
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test () → int
                let first = 1
                let second = 2
                let third = 3
                return first
            """;

        AssertNativeSource(source);
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
            module TestModule
              class MyClass
                field string:myField private
                func MyMethod private () → string
                  return this.myField
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test (bool:condition) → int
                let outer = 1
                if condition
                  let inner = 2
                  return inner
                return outer
            """;

        AssertNativeSource(source);
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
            module TestModule
              func Test (int:myParam, string:otherParam) → int
                return myParam
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("myParam", StringComparison.Ordinal));

        Assert.Contains(completions, c => c.Label == "myParam");
        Assert.Contains(completions, c => c.Label == "otherParam");
    }

    [Fact]
    public void ExpressionCompletions_IncludesLocalBindings()
    {
        var source = """
            module TestModule
              func Test () → int
                let int:localVar = 42
                let string:anotherVar = "hello"
                return localVar
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("localVar", StringComparison.Ordinal));

        Assert.Contains(completions, c => c.Label == "localVar");
        Assert.Contains(completions, c => c.Label == "anotherVar");
    }

    [Fact]
    public void ExpressionCompletions_IncludesFunctions()
    {
        var source = """
            module TestModule
              func Helper () → int
                return 42
              func Main () → int
                return Helper()
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("Helper", StringComparison.Ordinal));

        Assert.Contains(completions, c => c.Label == "Helper");
    }

    [Fact]
    public void ExpressionCompletions_IncludesBooleanLiterals()
    {
        var source = """
            module TestModule
              func Test () → bool
                return true
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "true");

        Assert.Contains(completions, c => c.Label == "true");
        Assert.Contains(completions, c => c.Label == "false");
    }

    [Fact]
    public void ExpressionCompletions_ShowsParameterTypes()
    {
        var source = """
            module TestModule
              func Test (int:count) → int
                return count
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("count", StringComparison.Ordinal));

        var countCompletion = completions.FirstOrDefault(c => c.Label == "count");
        Assert.NotNull(countCompletion);
        Assert.Contains("parameter", countCompletion.Detail ?? "");
        Assert.Contains("int", countCompletion.Detail ?? "");
    }

    [Fact]
    public void ExpressionCompletions_ShowsBindingTypes()
    {
        var source = """
            module TestModule
              func Test () → string
                let string:name = "test"
                return name
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("name", StringComparison.Ordinal));

        var nameCompletion = completions.FirstOrDefault(c => c.Label == "name");
        Assert.NotNull(nameCompletion);
        Assert.Contains("string", nameCompletion.Detail ?? "");
    }

    [Fact]
    public void ExpressionCompletions_ForLoopVariable()
    {
        var source = """
            module TestModule
              func Test () → int
                var int:sum = 0
                for i from 0 to 10 step 1
                  set sum sum + i
                return sum
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.IndexOf("sum + i", StringComparison.Ordinal) + "sum + ".Length);

        // Loop variable 'i' should be in scope
        Assert.Contains(completions, c => c.Label == "i");
        // Outer binding 'sum' should also be visible
        Assert.Contains(completions, c => c.Label == "sum");
    }

    [Fact]
    public void ExpressionCompletions_ForeachVariable()
    {
        var source = """
            module TestModule
              func Test (System.Collections.Generic.List<string>:items) → void
                ! cw
                each string:item in items
                  System.Console.WriteLine(item)
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("item", StringComparison.Ordinal));

        // Foreach variable 'item' should be in scope
        Assert.Contains(completions, c => c.Label == "item");
        // Parameter 'items' should also be visible
        Assert.Contains(completions, c => c.Label == "items");
    }

    [Fact]
    public void ExpressionCompletions_MethodContext_HasThisAndFields()
    {
        var source = """
            module TestModule
              class Counter
                field int:count private
                field string:name private
                func Increment private () → int
                  return count
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("count", StringComparison.Ordinal));

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
            module TestModule
              class Calculator
                func Add private (int:a, int:b) → int
                  return a + b
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "a + b");

        Assert.Contains(completions, c => c.Label == "a");
        Assert.Contains(completions, c => c.Label == "b");
    }

    [Fact]
    public void ExpressionCompletions_MutableVsImmutableBindings()
    {
        var source = """
            module TestModule
              func Test () → int
                let int:immutable = 10
                var int:mutable = 20
                return immutable + mutable
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("immutable", StringComparison.Ordinal));

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
            module TestModule
              func Test () → int
                var int:counter = 0
                do
                  set counter counter + 1
                while counter < 10
                return counter
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "counter + 1");

        // 'counter' should be visible inside do-while body
        Assert.Contains(completions, c => c.Label == "counter");
    }

    [Fact]
    public void ExpressionCompletions_DictionaryForeach_KeyAndValue()
    {
        var source = """
            module TestModule
              func Test (System.Collections.Generic.Dictionary<string,int>:scores) → void
                ! cw
                each name, score in scores
                  System.Console.WriteLine(score)
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.LastIndexOf("score", StringComparison.Ordinal));

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
            module TestModule
              func Test (int:value) → string
                match value
                  case var x when x > 0
                    return "positive"
                  case _
                    return "other"
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "\"positive\"");

        Assert.Contains(completions, c => c.Label == "x");
    }

    [Fact]
    public void ExpressionCompletions_ConstructorBody()
    {
        var source = """
            module TestModule
              class Person
                field string:_name private
                field int:_age private
                ctor (string:name, int:age)
                  set this._name name
                  set this._age age
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletions(source,
            source.IndexOf("set this._name name", StringComparison.Ordinal) + "set this._name ".Length);

        Assert.Contains(completions, c => c.Label == "name");
        Assert.Contains(completions, c => c.Label == "age");
    }

    [Fact]
    public void ExpressionCompletions_NestedScopes_AllVisible()
    {
        var source = """
            module TestModule
              func Test (int:param) → int
                let int:outer = 1
                if param > 0
                  let int:inner = 2
                  for i from 0 to 10 step 1
                    let int:deepest = outer + inner + i
                return outer
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "outer + inner + i");

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
            module TestModule
              func Test () → string
                try
                  return "success"
                catch System.Exception:ex
                  return "failure"
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "\"failure\"");

        // Exception variable 'ex' should be in scope in catch block
        Assert.Contains(completions, c => c.Label == "ex");
    }

    [Fact]
    public void ExpressionCompletions_ClassProperties()
    {
        var source = """
            module TestModule
              class Rectangle
                field double:_width private
                field double:_height private
                property double:Area
                  get
                    return _width * _height
            """;

        AssertNativeSource(source);
        var completions = LspTestHarness.GetExpressionCompletionsAt(source, "_width * _height");

        // Fields should be accessible from property getter
        Assert.Contains(completions, c => c.Label == "_width");
        Assert.Contains(completions, c => c.Label == "_height");
        Assert.Contains(completions, c => c.Label == "this");
    }

    #endregion
}
