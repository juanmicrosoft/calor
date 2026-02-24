using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for C# attribute preservation during C# → Calor → C# conversion.
/// </summary>
public class AttributeConversionTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region C# to Calor Attribute Conversion Tests

    [Fact]
    public void Convert_ClassWithRouteAttribute_PreservesAttribute()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/[controller]")]
            [ApiController]
            public class TestController : ControllerBase
            {
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Equal("TestController", cls.Name);
        Assert.Equal(2, cls.CSharpAttributes.Count);

        var routeAttr = cls.CSharpAttributes.FirstOrDefault(a => a.Name == "Route");
        Assert.NotNull(routeAttr);
        Assert.Single(routeAttr.Arguments);
        Assert.Equal("api/[controller]", routeAttr.Arguments[0].Value);

        var apiControllerAttr = cls.CSharpAttributes.FirstOrDefault(a => a.Name == "ApiController");
        Assert.NotNull(apiControllerAttr);
        Assert.Empty(apiControllerAttr.Arguments);
    }

    [Fact]
    public void Convert_MethodWithHttpPostAttribute_PreservesAttribute()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            public class TestController : ControllerBase
            {
                [HttpPost]
                public void Post()
                {
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Single(cls.Methods);

        var method = cls.Methods[0];
        Assert.Equal("Post", method.Name);
        Assert.Single(method.CSharpAttributes);

        var httpPostAttr = method.CSharpAttributes[0];
        Assert.Equal("HttpPost", httpPostAttr.Name);
        Assert.Empty(httpPostAttr.Arguments);
    }

    [Fact]
    public void Convert_AttributeWithPositionalArgs_PreservesArgs()
    {
        var csharpSource = """
            using System.ComponentModel.DataAnnotations;

            public class TestModel
            {
                [StringLength(100, MinimumLength = 10)]
                public string Name { get; set; }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Single(cls.Properties);

        var prop = cls.Properties[0];
        Assert.Equal("Name", prop.Name);
        Assert.Single(prop.CSharpAttributes);

        var attr = prop.CSharpAttributes[0];
        Assert.Equal("StringLength", attr.Name);
        Assert.Equal(2, attr.Arguments.Count);

        // First arg is positional (100)
        Assert.Null(attr.Arguments[0].Name);
        Assert.Equal(100, attr.Arguments[0].Value);

        // Second arg is named (MinimumLength = 10)
        Assert.Equal("MinimumLength", attr.Arguments[1].Name);
        Assert.Equal(10, attr.Arguments[1].Value);
    }

    [Fact]
    public void Convert_MultipleAttributes_PreservesAll()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Authorization;

            public class TestController : ControllerBase
            {
                [HttpGet("status")]
                [Authorize]
                [ProducesResponseType(200)]
                public string Get()
                {
                    return "ok";
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Single(cls.Methods);

        var method = cls.Methods[0];
        Assert.Equal(3, method.CSharpAttributes.Count);

        Assert.Contains(method.CSharpAttributes, a => a.Name == "HttpGet");
        Assert.Contains(method.CSharpAttributes, a => a.Name == "Authorize");
        Assert.Contains(method.CSharpAttributes, a => a.Name == "ProducesResponseType");
    }

    [Fact]
    public void Convert_FieldWithAttribute_PreservesAttribute()
    {
        var csharpSource = """
            using System.Text.Json.Serialization;

            public class TestModel
            {
                [JsonIgnore]
                private string _internalData;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Single(cls.Fields);

        var field = cls.Fields[0];
        Assert.Equal("_internalData", field.Name);
        Assert.Single(field.CSharpAttributes);

        var attr = field.CSharpAttributes[0];
        Assert.Equal("JsonIgnore", attr.Name);
    }

    [Fact]
    public void Convert_InterfaceWithAttribute_PreservesAttribute()
    {
        var csharpSource = """
            using System;

            [Obsolete("Use INewService instead")]
            public interface IOldService
            {
                void DoWork();
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Interfaces);

        var iface = result.Ast.Interfaces[0];
        Assert.Equal("IOldService", iface.Name);
        Assert.Single(iface.CSharpAttributes);

        var attr = iface.CSharpAttributes[0];
        Assert.Equal("Obsolete", attr.Name);
        Assert.Single(attr.Arguments);
        Assert.Equal("Use INewService instead", attr.Arguments[0].Value);
    }

    #endregion

    #region Calor Emitter Tests

    [Fact]
    public void CalorEmitter_EmitsClassAttributes()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/test")]
            [ApiController]
            public class TestController : ControllerBase
            {
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calorCode = emitter.Emit(result.Ast!);

        Assert.Contains("[@Route(\"api/test\")]", calorCode);
        Assert.Contains("[@ApiController]", calorCode);
    }

    [Fact]
    public void CalorEmitter_EmitsMethodAttributes()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            public class TestController : ControllerBase
            {
                [HttpPost]
                public void Post() { }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calorCode = emitter.Emit(result.Ast!);

        Assert.Contains("[@HttpPost]", calorCode);
    }

    [Fact]
    public void CalorEmitter_EmitsAttributeWithNamedArgs()
    {
        var csharpSource = """
            using System.ComponentModel.DataAnnotations;

            public class TestModel
            {
                [Range(1, 100, ErrorMessage = "Value out of range")]
                public int Value { get; set; }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var calorCode = emitter.Emit(result.Ast!);

        Assert.Contains("[@Range(1, 100, ErrorMessage=\"Value out of range\")]", calorCode);
    }

    #endregion

    #region C# Emitter Tests

    [Fact]
    public void CSharpEmitter_EmitsClassAttributes()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/test")]
            [ApiController]
            public class TestController : ControllerBase
            {
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var outputCode = emitter.Emit(result.Ast!);

        Assert.Contains("[Route(\"api/test\")]", outputCode);
        Assert.Contains("[ApiController]", outputCode);
    }

    [Fact]
    public void CSharpEmitter_EmitsMethodAttributes()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            public class TestController : ControllerBase
            {
                [HttpPost]
                public void Post() { }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var outputCode = emitter.Emit(result.Ast!);

        Assert.Contains("[HttpPost]", outputCode);
    }

    [Fact]
    public void CSharpEmitter_EmitsAttributeWithNamedArgs()
    {
        var csharpSource = """
            using System.ComponentModel.DataAnnotations;

            public class TestModel
            {
                [Range(1, 100, ErrorMessage = "Value out of range")]
                public int Value { get; set; }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var outputCode = emitter.Emit(result.Ast!);

        Assert.Contains("[Range(1, 100, ErrorMessage = \"Value out of range\")]", outputCode);
    }

    #endregion

    #region Calor Parser Tests

    [Fact]
    public void Parser_ParsesClassWithAttributes()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:TestController:ControllerBase}[@Route("api/test")][@ApiController]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Classes);

        var cls = module.Classes[0];
        Assert.Equal("TestController", cls.Name);
        Assert.Equal(2, cls.CSharpAttributes.Count);

        Assert.Contains(cls.CSharpAttributes, a => a.Name == "Route");
        Assert.Contains(cls.CSharpAttributes, a => a.Name == "ApiController");
    }

    [Fact]
    public void Parser_ParsesMethodWithAttributes()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:TestController:ControllerBase}
                §MT{m001:Post:pub}[@HttpPost]
                  §O{void}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Classes);

        var cls = module.Classes[0];
        Assert.Single(cls.Methods);

        var method = cls.Methods[0];
        Assert.Equal("Post", method.Name);
        Assert.Single(method.CSharpAttributes);
        Assert.Equal("HttpPost", method.CSharpAttributes[0].Name);
    }

    [Fact]
    public void Parser_ParsesAttributeWithArgs()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:TestModel}
                §PROP{p001:Value:int:pub}[@Range(1, 100, ErrorMessage="Invalid")]
                  §GET
                  §SET
                §/PROP{p001}
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Classes);

        var cls = module.Classes[0];
        Assert.Single(cls.Properties);

        var prop = cls.Properties[0];
        Assert.Single(prop.CSharpAttributes);

        var attr = prop.CSharpAttributes[0];
        Assert.Equal("Range", attr.Name);
        Assert.Equal(3, attr.Arguments.Count);

        // First two are positional
        Assert.Equal(1, attr.Arguments[0].Value);
        Assert.Equal(100, attr.Arguments[1].Value);

        // Third is named
        Assert.Equal("ErrorMessage", attr.Arguments[2].Name);
        Assert.Equal("Invalid", attr.Arguments[2].Value);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_CSharpToCalorToCSharp_AttributesPreserved()
    {
        var csharpSource = """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/[controller]")]
            [ApiController]
            public class JoinController : ControllerBase
            {
                [HttpPost]
                public void Post()
                {
                }
            }
            """;

        // C# → Calor
        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor → Calor text
        var calorEmitter = new CalorEmitter();
        var calorCode = calorEmitter.Emit(result.Ast!);

        // Parse the Calor text
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorCode, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var parsedModule = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        // Calor → C#
        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(parsedModule);

        // Verify attributes are preserved
        Assert.Contains("[Route(\"api/[controller]\")]", outputCode);
        Assert.Contains("[ApiController]", outputCode);
        Assert.Contains("[HttpPost]", outputCode);
    }

    #endregion

    #region Bitwise Expressions in Attribute Arguments

    [Fact]
    public void Parser_ParsesBitwiseOrInAttribute()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyAttribute}[@AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Classes);

        var cls = module.Classes[0];
        Assert.Single(cls.CSharpAttributes);

        var attr = cls.CSharpAttributes[0];
        Assert.Equal("AttributeUsage", attr.Name);
        Assert.Single(attr.Arguments);

        var value = attr.Arguments[0].Value;
        var bbe = Assert.IsType<BitwiseBinaryExpression>(value);
        Assert.Equal(BitwiseOperator.Or, bbe.Operator);
    }

    [Fact]
    public void Parser_ParsesThreeOperandBitwiseOr()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyAttribute}[@Flags][@AttributeUsage(A.X | A.Y | A.Z)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var cls = module.Classes[0];
        var attrUsage = cls.CSharpAttributes.First(a => a.Name == "AttributeUsage");
        // Left-associative: (A.X | A.Y) | A.Z
        var outer = Assert.IsType<BitwiseBinaryExpression>(attrUsage.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.Or, outer.Operator);
        var inner = Assert.IsType<BitwiseBinaryExpression>(outer.Left);
        Assert.Equal(BitwiseOperator.Or, inner.Operator);
    }

    [Fact]
    public void RoundTrip_BitwiseOrAttribute_Preserved()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyAttribute}[@AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(module);

        Assert.Contains("AttributeTargets.Class | AttributeTargets.Struct", outputCode);
    }

    [Fact]
    public void Parser_ParsesNamedArgWithBitwiseOr()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyAttribute}[@AttributeUsage(ValidOn = AttributeTargets.Class | AttributeTargets.Struct)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        Assert.Equal("AttributeUsage", attr.Name);
        Assert.Single(attr.Arguments);
        Assert.Equal("ValidOn", attr.Arguments[0].Name);

        var bbe = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.Or, bbe.Operator);
    }

    [Fact]
    public void Parser_ParsesMixedArgsWithBitwiseOr()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:TestClass}[@SomeAttr("text", Flags = A.X | A.Y)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        Assert.Equal("SomeAttr", attr.Name);
        Assert.Equal(2, attr.Arguments.Count);

        Assert.Null(attr.Arguments[0].Name);
        Assert.Equal("text", attr.Arguments[0].Value);

        Assert.Equal("Flags", attr.Arguments[1].Name);
        var bbe = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[1].Value);
        Assert.Equal(BitwiseOperator.Or, bbe.Operator);
    }

    [Fact]
    public void RoundTrip_CSharpToCalorToCSharp_BitwiseOrPreserved()
    {
        var csharpSource = """
            using System;

            [Flags]
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public class MyAttribute : Attribute
            {
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes[0];
        var attrUsage = cls.CSharpAttributes.First(a => a.Name == "AttributeUsage");
        Assert.Single(attrUsage.Arguments);
        Assert.IsType<BitwiseBinaryExpression>(attrUsage.Arguments[0].Value);

        var calorEmitter = new CalorEmitter();
        var calorCode = calorEmitter.Emit(result.Ast!);
        Assert.Contains("AttributeTargets.Class | AttributeTargets.Struct", calorCode);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorCode, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var parsedModule = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(parsedModule);
        Assert.Contains("AttributeTargets.Class | AttributeTargets.Struct", outputCode);
    }

    [Fact]
    public void Parser_TrailingOperatorInAttribute_ReportsError()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.X | )]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.Code == DiagnosticCode.UnexpectedToken);
        Assert.Contains("attribute value", error.Message);
        Assert.Contains("CloseParen", error.Message);
    }

    [Fact]
    public void Parser_ParsesBitwiseAndInAttribute()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.All & A.Mask)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        var bbe = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.And, bbe.Operator);
    }

    [Fact]
    public void Parser_ParsesBitwiseXorInAttribute()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.X ^ A.Y)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        var bbe = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.Xor, bbe.Operator);
    }

    [Fact]
    public void Parser_ParsesBitwiseNotInAttribute()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(~A.X)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        var bne = Assert.IsType<BitwiseNotExpression>(attr.Arguments[0].Value);
        Assert.IsType<MemberAccessReference>(bne.Operand);
    }

    [Fact]
    public void Parser_ParsesAndWithNotInAttribute()
    {
        // Common pattern: AttributeTargets.All & ~AttributeTargets.Delegate
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.All & ~A.Delegate)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        var bbe = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.And, bbe.Operator);
        Assert.IsType<MemberAccessReference>(bbe.Left);
        Assert.IsType<BitwiseNotExpression>(bbe.Right);
    }

    [Fact]
    public void Parser_ParsesParenthesizedBitwiseExpr()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr((A.X | A.Y) & A.Z)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        // (A.X | A.Y) & A.Z → And at top
        var outer = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.And, outer.Operator);
        // Left side is the parenthesized OR
        var inner = Assert.IsType<BitwiseBinaryExpression>(outer.Left);
        Assert.Equal(BitwiseOperator.Or, inner.Operator);
    }

    [Fact]
    public void Parser_PrecedenceAndBindsTighterThanOr()
    {
        // A | B & C should parse as A | (B & C) per C# precedence
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.X | A.Y & A.Z)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var attr = module.Classes[0].CSharpAttributes[0];
        var outer = Assert.IsType<BitwiseBinaryExpression>(attr.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.Or, outer.Operator);
        // Right side is the higher-precedence & expression
        Assert.IsType<MemberAccessReference>(outer.Left);
        var right = Assert.IsType<BitwiseBinaryExpression>(outer.Right);
        Assert.Equal(BitwiseOperator.And, right.Operator);
    }

    [Fact]
    public void RoundTrip_ParenthesizedExpr_PreservesParens()
    {
        // (A | B) & C must keep parens because | is lower precedence than &
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr((A.X | A.Y) & A.Z)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(module);

        Assert.Contains("(A.X | A.Y) & A.Z", outputCode);
    }

    [Fact]
    public void RoundTrip_NoPrecedenceConflict_OmitsParens()
    {
        // A | B & C — no parens needed, & binds tighter naturally
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.X | A.Y & A.Z)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(module);

        Assert.Contains("A.X | A.Y & A.Z", outputCode);
    }

    [Fact]
    public void RoundTrip_BitwiseNotAndOr_Preserved()
    {
        var calorSource = """
            §M{m001:TestModule}
              §CL{c001:MyClass}[@SomeAttr(A.All & ~A.Delegate)]
              §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(module);

        Assert.Contains("A.All & ~A.Delegate", outputCode);
    }

    [Fact]
    public void RoundTrip_CSharpToCalorToCSharp_BitwiseAndNotPreserved()
    {
        var csharpSource = """
            using System;

            [AttributeUsage(AttributeTargets.All & ~AttributeTargets.Delegate)]
            public class MyAttribute : Attribute
            {
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes[0];
        var attrUsage = cls.CSharpAttributes.First(a => a.Name == "AttributeUsage");
        var bbe = Assert.IsType<BitwiseBinaryExpression>(attrUsage.Arguments[0].Value);
        Assert.Equal(BitwiseOperator.And, bbe.Operator);
        Assert.IsType<BitwiseNotExpression>(bbe.Right);

        var calorEmitter = new CalorEmitter();
        var calorCode = calorEmitter.Emit(result.Ast!);

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorCode, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var parsedModule = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var csharpEmitter = new CSharpEmitter();
        var outputCode = csharpEmitter.Emit(parsedModule);
        Assert.Contains("AttributeTargets.All & ~AttributeTargets.Delegate", outputCode);
    }

    #endregion

    #region Attribute Target Prefixes

    [Fact]
    public void Convert_ReturnAttribute_PreservesTarget()
    {
        var csharp = """
            using System.Diagnostics.CodeAnalysis;
            public class Service
            {
                [return: NotNullIfNotNull("input")]
                public string? Process(string? input)
                {
                    return input;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("[@return:NotNullIfNotNull", result.CalorSource);
    }

    [Fact]
    public void Parser_ReturnAttributeTarget_RoundtripsToCS()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service}
            §MT{m1:Process:pub}
              [@return:MaybeNull]
              §I{str:input}
              §O{str}
              §R input
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("[return: MaybeNull]", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Parser_ParamAttributeTarget_RoundtripsToCS()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service}
            §MT{m1:Process:pub}
              [@param:NotNull]
              §I{str:input}
              §O{str}
              §R input
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("[param: NotNull]", compilationResult.GeneratedCode);
    }

    #endregion

    #region nameof Roundtrip

    [Fact]
    public void Convert_Nameof_PreservesInCalor()
    {
        var csharp = """
            using System;
            public class Guard
            {
                public void Check(string name)
                {
                    if (name == null) throw new ArgumentNullException(nameof(name));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("(nameof name)", result.CalorSource);
    }

    [Fact]
    public void Parser_Nameof_RoundtripsToCS()
    {
        var calorSource = """
            §M{m1:Test}
            §F{f1:GetName}
              §I{str:value}
              §O{str}
              §R (nameof value)
            §/F{f1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("nameof(value)", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Parser_Nameof_DottedName_RoundtripsToCS()
    {
        var calorSource = """
            §M{m1:Test}
            §F{f1:GetName}
              §I{str:value}
              §O{str}
              §R (nameof obj.Property)
            §/F{f1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("nameof(obj.Property)", compilationResult.GeneratedCode);
    }

    #endregion

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return "";
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }
}
