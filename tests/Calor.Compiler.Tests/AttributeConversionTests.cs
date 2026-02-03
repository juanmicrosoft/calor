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
              §CLASS{c001:TestController:ControllerBase}[@Route("api/test")][@ApiController]
              §/CLASS{c001}
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
              §CLASS{c001:TestController:ControllerBase}
                §METHOD{m001:Post:pub}[@HttpPost]
                  §O{void}
                §/METHOD{m001}
              §/CLASS{c001}
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
              §CLASS{c001:TestModel}
                §PROP{p001:Value:int:pub}[@Range(1, 100, ErrorMessage="Invalid")]
                  §GET
                  §SET
                §/PROP{p001}
              §/CLASS{c001}
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

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return "";
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }
}
