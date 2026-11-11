using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxCapabilityClassifier =
    Calor.Compiler.Migration.FeatureSupport.SyntaxCapabilityClassifier;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Integration tests for the feature-check command functionality.
/// Tests the FeatureSupport registry for completeness and correctness.
/// </summary>
public class FeatureCheckCommandTests
{
    #region Feature Lookup

    [Theory]
    [InlineData("public class C { public void M() { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } } }", SyntaxKind.ForStatement)]
    [InlineData("public class C { ~C() { } }", SyntaxKind.DestructorDeclaration)]
    [InlineData("public record struct R(int X);", SyntaxKind.RecordStructDeclaration)]
    [InlineData("namespace A { class C { } }\r\nnamespace B { class D { } }", SyntaxKind.CompilationUnit)]
    [InlineData("using System.Reflection;\r\n[assembly: AssemblyTitle(\"\u03c0\")]", SyntaxKind.CompilationUnit)]
    public void PreservationReceipt_ResolvesActualOriginalStatementMemberAndUnit(string source, SyntaxKind kind)
    {
        var options = ReceiptOptions();
        var converted = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source, "original.cs");
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var receipt = ConversionReceipt.Capture(converted, options, []);
        var restored = ConversionReceipt.Deserialize(receipt.Serialize());
        var frame = Assert.Single(restored.Frames);
        var boundary = restored.ResolveBoundary(0, converted.CalorSource!);

        Assert.Equal(source, restored.Original.Text);
        Assert.Equal(converted.CalorSource, restored.GeneratedCalor);
        Assert.True(boundary.IsKind(kind));
        Assert.Equal(source, boundary.SyntaxTree.GetText().ToString());
        Assert.Equal(frame.Start, boundary.SpanStart);
        Assert.Equal(frame.Length, boundary.Span.Length);
        Assert.Null(frame.Unresolved);
        Assert.Equal(receipt.Serialize(), restored.Serialize());
        Assert.NotEmpty(restored.ReplayLimitations);
        AssertReceiptCaptureInvariant(source, options, converted);
        if (kind == SyntaxKind.RecordStructDeclaration)
            Assert.Equal(FeatureSupport.PreservationDecisionKind.StillRequired,
                FeatureSupport.AssessPreservation("record-struct", boundary, boundary).Kind);
    }

    [Fact]
    public void PreservationReceipt_IdenticalPayloadsKeepDistinctOriginalPaths()
    {
        const string loop = "for (int i = 0; int.TryParse(\"0\", out var n); i++) { }";
        const string source = "public class C { public void A() { " + loop + " } public void B() { " + loop + " } }";
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        Assert.Equal(2, receipt.Frames.Count);
        Assert.Equal(receipt.Frames[0].Payload, receipt.Frames[1].Payload);
        var a = receipt.ResolveBoundary(0, result.CalorSource!);
        var b = receipt.ResolveBoundary(1, result.CalorSource!);
        Assert.NotEqual(a.SpanStart, b.SpanStart);
        Assert.Equal("A", a.Ancestors().OfType<MethodDeclarationSyntax>().Single().Identifier.ValueText);
        Assert.Equal("B", b.Ancestors().OfType<MethodDeclarationSyntax>().Single().Identifier.ValueText);
    }

    [Fact]
    public void PreservationReceipt_OpaqueInactiveTextIsNotInventedOriginalSyntax()
    {
        const string payload = "  malformed \" \u03c0\U0001f600 }§/CSHARP\r\n";
        const string source = "public class C {\r\nvoid M() {\r\n#if NEVER\r\n" + payload + "#endif\r\n}\r\n}";
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        Assert.Equal(payload, Assert.Single(receipt.Frames).Payload);
        Assert.Equal(source, receipt.Original.Text);
        Assert.Null(receipt.Frames[0].BoundaryPath);
        AssertReceiptCaptureInvariant(source, options, result);
        Assert.Contains("exact original boundary", Assert.Throws<InvalidDataException>(
            () => receipt.ResolveBoundary(0, result.CalorSource!)).Message);
    }

    [Fact]
    public void PreservationReceipt_DirectivesRemainExemptAndDoNotShiftFrameAssociation()
    {
        const string source = "#nullable enable\r\npublic record struct R(int X);";
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        Assert.Single(receipt.Frames);
        Assert.True(receipt.ResolveBoundary(0, result.CalorSource!).IsKind(SyntaxKind.RecordStructDeclaration));
    }

    [Theory]
    [InlineData("alias-qualified-value")]
    [InlineData("explicit-interface")]
    [InlineData("null-forgiving-expression")]
    [InlineData("conditional-expression-hoisting")]
    [InlineData("initializer-interop")]
    [InlineData("linq-query")]
    [InlineData("dictionary-initializer")]
    [InlineData("default-expression")]
    public void PreservationReceipt_ContextAssociationIsNotSemanticOrNativeProof(string feature)
    {
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext("public record struct R;");
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        var boundary = receipt.ResolveBoundary(0, result.CalorSource!);
        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext,
            FeatureSupport.AssessPreservation(feature, boundary, boundary).Kind);
        Assert.Equal(FeatureSupport.PreservationDecisionKind.UnknownOrMismatch,
            FeatureSupport.AssessPreservation("invented-feature", boundary, boundary).Kind);
    }

    [Fact]
    public void PreservationReceipt_RejectsEditedOutputAndCorruptOrIncompleteAssociations()
    {
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext("public record struct R;");
        var receipt = ConversionReceipt.Capture(result, options, []);
        Assert.Throws<InvalidDataException>(() => receipt.ResolveBoundary(0, result.CalorSource + "\n"));
        Assert.Throws<InvalidDataException>(() => receipt.ResolveBoundary(-1, result.CalorSource!));
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize((receipt with { Version = 0 }).Serialize()));
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize((receipt with { Frames = [] }).Serialize()));
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize("{"));
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize("{}"));
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize(
            (receipt with { ReplayLimitations = [] }).Serialize()));
        var frame = Assert.Single(receipt.Frames);
        Assert.Throws<InvalidDataException>(() => ConversionReceipt.Deserialize(
            (receipt with { Frames = [frame with { Payload = frame.Payload + " " }] }).Serialize()));
        foreach (var corrupt in new[]
        {
            frame with { BoundaryPath = [-1] },
            frame with { RawKind = (int)SyntaxKind.ClassDeclaration },
            frame with { Start = frame.Start + 1 },
            frame with { Length = frame.Length + 1 }
        })
            Assert.Throws<InvalidDataException>(() =>
                (receipt with { Frames = [corrupt] }).ResolveBoundary(0, result.CalorSource!));
    }

    [Fact]
    public async Task PreservationReceipt_SaveRefreshAndFacadeRetainExactGenerationBase()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            const string original = "\uFEFFnamespace A { class C { } }\r\nnamespace B { class D { } }// \u03c0\U0001f600";
            await File.WriteAllBytesAsync(input, System.Text.Encoding.UTF8.GetBytes(original));
            var options = ReceiptOptions();
            options.ValidateRoundTripCSharp = true;
            var first = await Converter.ConvertCSharpToCalorAsync(input, output, options);
            Assert.True(first.Success, string.Join("\n", first.Issues));
            Assert.Equal(ConversionRoundTrip.Verified, first.Context.RoundTrip);
            var receipt = ConversionReceipt.Deserialize(await File.ReadAllTextAsync(output + ".conversion.json"));
            Assert.Equal(original, receipt.Original.Text);
            Assert.Equal(original, Assert.Single(receipt.Frames).Payload);
            Assert.Equal(first.CalorSource, await File.ReadAllTextAsync(output));
            Assert.True(receipt.ResolveBoundary(0, first.CalorSource!).IsKind(SyntaxKind.CompilationUnit));
            var again = await new CSharpToCalorConverter(options).ConvertFileAndSaveAsync(input, output);
            Assert.Equal(first.CalorSource, again.CalorSource);

            var edited = first.CalorSource + "\n// retain my edit";
            await File.WriteAllTextAsync(output, edited);
            var savedReceipt = await File.ReadAllTextAsync(output + ".conversion.json");
            Assert.Contains("edited", (await Assert.ThrowsAsync<IOException>(() =>
                new CSharpToCalorConverter(options).ConvertFileAndSaveAsync(input, output))).Message);
            Assert.Equal(edited, await File.ReadAllTextAsync(output));
            Assert.Equal(savedReceipt, await File.ReadAllTextAsync(output + ".conversion.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PreservationReceipt_StaleInputsAndConcurrentOutputOrReceiptCannotBeCommitted()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-cas-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            await File.WriteAllTextAsync(input, "public record struct R;");
            var options = ReceiptOptions();
            var result = await new CSharpToCalorConverter(options).ConvertFileAndSaveAsync(input, output);
            Assert.True(result.Success, string.Join("\n", result.Issues));
            var saved = await File.ReadAllTextAsync(output + ".conversion.json");
            var receipt = ConversionReceipt.Deserialize(saved);
            var bytes = await File.ReadAllBytesAsync(input);
            await File.AppendAllTextAsync(input, "// changed");
            Assert.Throws<IOException>(() => receipt.ResolveBoundary(0, result.CalorSource!));
            await Assert.ThrowsAsync<IOException>(() =>
                ConversionFileWriter.WriteGenerationAsync(output, result.CalorSource, saved, receipt));
            Assert.Equal(result.CalorSource, await File.ReadAllTextAsync(output));
            await File.WriteAllBytesAsync(input, bytes);

            await File.AppendAllTextAsync(output, "// concurrent output");
            await Assert.ThrowsAsync<IOException>(() =>
                ConversionFileWriter.WriteGenerationAsync(output, result.CalorSource, saved, receipt));
            Assert.EndsWith("// concurrent output", await File.ReadAllTextAsync(output));
            await File.WriteAllTextAsync(output, result.CalorSource);
            await File.AppendAllTextAsync(output + ".conversion.json", " ");
            await Assert.ThrowsAsync<IOException>(() =>
                ConversionFileWriter.WriteGenerationAsync(output, result.CalorSource, saved, receipt));
            Assert.Equal(result.CalorSource, await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PreservationReceipt_MissingLegacyBaseAndInterruptedReceiptFailClosed()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-missing-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            await File.WriteAllTextAsync(input, "public record struct R;");
            await File.WriteAllTextAsync(output, "// unassociated original output");
            var converter = new CSharpToCalorConverter(ReceiptOptions());
            Assert.Contains("new destination", (await Assert.ThrowsAsync<IOException>(() =>
                converter.ConvertFileAndSaveAsync(input, output))).Message);
            Assert.Equal("// unassociated original output", await File.ReadAllTextAsync(output));
            File.Delete(output);
            var result = await converter.ConvertFileAndSaveAsync(input, output);
            Assert.True(result.Success, string.Join("\n", result.Issues));
            await File.WriteAllTextAsync(output, result.CalorSource + "\n// interrupted newer generation");
            await Assert.ThrowsAsync<IOException>(() => converter.ConvertFileAndSaveAsync(input, output));
            Assert.EndsWith("// interrupted newer generation", await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ConversionOptions ReceiptOptions() => new()
    {
        ModuleName = "Original",
        ValidateRoundTripCSharp = false,
        UserManifestRoot = ""
    };

    [Fact]
    public void PreservationReceipt_PreservesEachSiblingParseContextAndReferenceAliases()
    {
        var options = ReceiptOptions();
        options.ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp13,
            DocumentationMode.Diagnose, SourceCodeKind.Regular, ["ORIGINAL"])
            .WithFeatures([new KeyValuePair<string, string>("original-feature", "original-value")]);
        var sibling = CSharpSyntaxTree.ParseText(
            "#if SIBLING\r\npublic class S { }\r\n#endif",
            new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Parse,
                SourceCodeKind.Regular, ["SIBLING"]), "sibling.cs");
        options.AdditionalSemanticSyntaxTrees = [sibling];
        options.References = [new(typeof(object).Assembly.Location, ["Root"])];
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext("public record struct R;", "original.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        var originalOptions = (CSharpParseOptions)receipt.Original.Parse().Options;
        var siblingOptions = (CSharpParseOptions)Assert.Single(receipt.Siblings).Parse().Options;
        Assert.Equal(LanguageVersion.CSharp13, originalOptions.LanguageVersion);
        Assert.Equal(DocumentationMode.Diagnose, originalOptions.DocumentationMode);
        Assert.Equal(["ORIGINAL"], originalOptions.PreprocessorSymbolNames);
        Assert.Equal("original-value", originalOptions.Features["original-feature"]);
        Assert.Equal(LanguageVersion.CSharp12, siblingOptions.LanguageVersion);
        Assert.Equal(["SIBLING"], siblingOptions.PreprocessorSymbolNames);
        Assert.Equal(sibling.GetText().ToString(), receipt.Siblings[0].Text);
        Assert.Equal(["Root"], Assert.Single(receipt.Metadata.References).Aliases);
    }

    [Fact]
    public void PreservationReceipt_ReferenceBytesAreFreshnessInputsNotOriginProof()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-reference-").FullName;
        try
        {
            var source = Path.Combine(directory, "Original.cs");
            var reference = Path.Combine(directory, "reference.dll");
            var sibling = Path.Combine(directory, "Sibling.cs");
            File.WriteAllText(source, "public record struct R;");
            File.Copy(typeof(object).Assembly.Location, reference);
            File.WriteAllText(sibling, "public class S { }");
            var options = ReceiptOptions();
            options.AdditionalSemanticSyntaxTrees = [CSharpSyntaxTree.ParseText(File.ReadAllText(sibling), path: sibling)];
            var inputs = ConversionReceipt.CaptureInputs(source,
                [MetadataReference.CreateFromFile(reference)], options);
            Assert.Equal(3, inputs.Count);
            ConversionReceipt.VerifyInputs(inputs);
            File.AppendAllText(sibling, "// changed");
            Assert.Contains(sibling, Assert.Throws<IOException>(
                () => ConversionReceipt.VerifyInputs(inputs)).Message);
            File.WriteAllText(sibling, "public class S { }");
            using (var stream = new FileStream(reference, FileMode.Append))
                stream.WriteByte(0);
            Assert.Contains(reference, Assert.Throws<IOException>(
                () => ConversionReceipt.VerifyInputs(inputs)).Message);
            File.Delete(reference);
            Assert.Throws<IOException>(() => ConversionReceipt.VerifyInputs(inputs));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PreservationReceipt_SourceAliasAndConcurrentGenerationAreRefused()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-lock-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            const string original = "public record struct R;";
            await File.WriteAllTextAsync(input, original);
            var converter = new CSharpToCalorConverter(ReceiptOptions());
            await Assert.ThrowsAsync<IOException>(() => converter.ConvertFileAndSaveAsync(input, input));
            Assert.Equal(original, await File.ReadAllTextAsync(input));
            using (var held = new FileStream(output + ".conversion.lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                await Assert.ThrowsAsync<IOException>(() => converter.ConvertFileAndSaveAsync(input, output));
            Assert.False(File.Exists(output));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                converter.ConvertFileAndSaveAsync(input, output, canceled.Token));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("public static object Run() => global::System.EventArgs.Empty;", "alias-qualified-value")]
    [InlineData("public static object Run() { string? text = null; return text!.Length; }", "null-forgiving-expression")]
    [InlineData("public static object Run() { System.Text.StringBuilder value; value = new System.Text.StringBuilder(); return value.Length; }", "default-expression")]
    [InlineData("public static object Run() { var values = new System.Collections.Generic.Dictionary<int,int> { [1] = 2, [1] = 3 }; return values[1]; }", "dictionary-initializer")]
    [InlineData("public static bool Enabled { get; } = !System.AppContext.TryGetSwitch(\"Calor.Shape.Absent\", out var enabled) || enabled;", "conditional-expression-hoisting")]
    [InlineData("public static object Run() { int[] values = {1, 2}; var query = from value in values let next = value + 1 select next; return System.Linq.Enumerable.Sum(query); }", "linq-query")]
    [InlineData("public System.Collections.Generic.List<int> Values = new System.Collections.Generic.List<int> { 4 };", "initializer-interop")]
    public void PreservationReceipt_ActualContextualProducersRetainLossPayloadAndStatus(string member, string feature)
    {
        var source = "using System.Linq;\npublic class C { " + member + " }";
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Losses, loss => loss.Feature == feature);
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        Assert.NotEmpty(receipt.Frames);
        foreach (var index in Enumerable.Range(0, receipt.Frames.Count))
        {
            var original = receipt.ResolveBoundary(index, result.CalorSource!);
            Assert.Equal(source, original.SyntaxTree.GetText().ToString());
            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext,
                FeatureSupport.AssessPreservation(feature, original, original).Kind);
        }
        AssertReceiptCaptureInvariant(source, options, result);
    }

    [Fact]
    public void PreservationReceipt_ExplicitInterfaceProducerRetainsRealEnclosingType()
    {
        const string source = "public interface I<T> { int Get(T value); } public class C : I<int> { int I<int>.Get(int value) => value + 1; }";
        var options = ReceiptOptions();
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(result.Losses, loss => loss.Feature == "explicit-interface");
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        var original = receipt.ResolveBoundary(0, result.CalorSource!);
        Assert.IsType<MethodDeclarationSyntax>(original);
        Assert.Equal("C", original.Ancestors().OfType<ClassDeclarationSyntax>().Single().Identifier.ValueText);
        AssertReceiptCaptureInvariant(source, options, result);
    }

    [Fact]
    public async Task PreservationReceipt_ChangedOriginalCanRefreshOnlyAnUneditedGeneration()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-refresh-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            await File.WriteAllTextAsync(input, "public record struct R;");
            var converter = new CSharpToCalorConverter(ReceiptOptions());
            var first = await converter.ConvertFileAndSaveAsync(input, output);
            Assert.True(first.Success, string.Join("\n", first.Issues));
            var oldReceipt = ConversionReceipt.Deserialize(await File.ReadAllTextAsync(output + ".conversion.json"));
            const string changed = "public record struct R(int X);";
            await File.WriteAllTextAsync(input, changed);
            Assert.Throws<IOException>(() => oldReceipt.ResolveBoundary(0, first.CalorSource!));
            var refreshed = await converter.ConvertFileAndSaveAsync(input, output);
            Assert.True(refreshed.Success, string.Join("\n", refreshed.Issues));
            var receipt = ConversionReceipt.Deserialize(await File.ReadAllTextAsync(output + ".conversion.json"));
            Assert.Equal(changed, receipt.Original.Text);
            Assert.Equal(refreshed.CalorSource, await File.ReadAllTextAsync(output));
            Assert.Equal(changed, receipt.ResolveBoundary(0, refreshed.CalorSource!).ToString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PreservationReceipt_FailedFinalReceiptWriteCannotAuthorizeAnotherRefresh()
    {
        var directory = Directory.CreateTempSubdirectory("calor-receipt-interrupted-").FullName;
        try
        {
            var input = Path.Combine(directory, "Original.cs");
            var output = Path.ChangeExtension(input, ".calr");
            await File.WriteAllTextAsync(input, "public record struct R;");
            Directory.CreateDirectory(output + ".conversion.json");
            var converter = new CSharpToCalorConverter(ReceiptOptions());
            await Assert.ThrowsAsync<IOException>(() => converter.ConvertFileAndSaveAsync(input, output));
            Assert.True(File.Exists(output));
            var retained = await File.ReadAllTextAsync(output);
            Assert.Contains("no matching", (await Assert.ThrowsAsync<IOException>(
                () => converter.ConvertFileAndSaveAsync(input, output))).Message);
            Assert.Equal(retained, await File.ReadAllTextAsync(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PreservationReceipt_TransformedSelectedBranchIsNotMappedByCoincidentSpans()
    {
        const string original = "#if NEVER\r\npublic class Inactive { }\r\n#else\r\npublic record struct R;\r\n#endif";
        var options = ReceiptOptions();
        options.Fidelity = ConversionFidelity.Lossy;
        options.PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy;
        var result = new CSharpToCalorConverter(options).ConvertWithPreservationContext(original);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var receipt = ConversionReceipt.Deserialize(ConversionReceipt.Capture(result, options, []).Serialize());
        Assert.Equal(original, receipt.Original.Text);
        Assert.Null(Assert.Single(receipt.Frames).BoundaryPath);
        Assert.Throws<InvalidDataException>(() => receipt.ResolveBoundary(0, result.CalorSource!));
        AssertReceiptCaptureInvariant(original, options, result);
    }

    private static void AssertReceiptCaptureInvariant(string source, ConversionOptions options, ConversionResult captured)
    {
        var normal = new CSharpToCalorConverter(options).Convert(source, captured.Context.SourceFile);
        Assert.Equal(Observe(normal), Observe(captured));
        Assert.Null(normal.Context.OriginalSyntaxTree);
        Assert.Empty(normal.Context.EmittedPreservations);
        Assert.Throws<InvalidOperationException>(() => ConversionReceipt.Capture(normal, options, []));

        static string Observe(ConversionResult result) => System.Text.Json.JsonSerializer.Serialize(new
        {
            result.Success, result.CalorSource, result.Losses, result.Issues,
            result.Context.Stats, result.Metadata, result.Paths, result.Context.RoundTrip,
            GeneratedCSharp = result.Ast == null ? null : new CodeGen.CSharpEmitter().Emit(result.Ast)
        });
    }

    [Theory]
    [InlineData("class", SupportLevel.Full)]
    [InlineData("async-await", SupportLevel.Full)]
    [InlineData("lambda", SupportLevel.Full)]
    [InlineData("generics", SupportLevel.Full)]
    [InlineData("operator-overload", SupportLevel.Full)]
    [InlineData("implicit-conversion", SupportLevel.Full)]
    [InlineData("explicit-conversion", SupportLevel.Full)]
    [InlineData("equals-operator", SupportLevel.Full)]
    [InlineData("linq-method", SupportLevel.Full)]
    [InlineData("goto", SupportLevel.Full)]
    [InlineData("labeled-statement", SupportLevel.Full)]
    [InlineData("postfix-operator", SupportLevel.Full)]
    [InlineData("is-type-pattern", SupportLevel.Full)]
    [InlineData("declaration-pattern", SupportLevel.Full)]
    [InlineData("throw-expression", SupportLevel.Full)]
    [InlineData("nested-generic-type", SupportLevel.Full)]
    [InlineData("binary pattern (and/or)", SupportLevel.Full)]
    [InlineData("unary pattern (not)", SupportLevel.Full)]
    [InlineData("record", SupportLevel.Full)]
    public void FeatureCheck_FullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("ref-parameter", SupportLevel.Partial)]
    [InlineData("dynamic", SupportLevel.Partial)]
    [InlineData("interface", SupportLevel.Partial)]
    [InlineData("linq-query", SupportLevel.Partial)]
    [InlineData("local-function", SupportLevel.Partial)] // 0.25 F3 (#847): the non-capturing slice
    public void FeatureCheck_PartiallySupported_ReturnsPartialLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("await-foreach", SupportLevel.NotSupported)]
    [InlineData("file-scoped-type", SupportLevel.NotSupported)]
    [InlineData("scoped-parameter", SupportLevel.NotSupported)]
    [InlineData("using-declaration", SupportLevel.NotSupported)]
    [InlineData("standalone-block", SupportLevel.NotSupported)]
    public void FeatureCheck_NotSupported_ReturnsNotSupportedLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.False(FeatureSupport.IsFullySupported(feature));
        Assert.False(FeatureSupport.IsSupported(feature));
    }

    [Theory]
    [InlineData("yield-return", SupportLevel.Full)]
    [InlineData("extension-method", SupportLevel.Full)]
    public void FeatureCheck_NewlyFullySupported_ReturnsFullLevel(string feature, SupportLevel expected)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal(expected, info.Support);
        Assert.True(FeatureSupport.IsFullySupported(feature));
        Assert.True(FeatureSupport.IsSupported(feature));
    }

    #endregion

    #region Unknown Features

    [Fact]
    public void FeatureCheck_UnknownFeature_ReturnsNull()
    {
        var info = FeatureSupport.GetFeatureInfo("some-made-up-feature");

        Assert.Null(info);
    }

    [Fact]
    public void FeatureCheck_UnknownFeature_DefaultsToNotSupported()
    {
        // Unknown features default to NotSupported to prevent silent suppression of blockers
        var level = FeatureSupport.GetSupportLevel("unknown-feature");

        Assert.Equal(SupportLevel.NotSupported, level);
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("ASYNC-AWAIT")]
    [InlineData("Async-Await")]
    [InlineData("async-AWAIT")]
    public void FeatureCheck_CaseInsensitive_FindsFeature(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.Equal("async-await", info.Name);
    }

    #endregion

    #region Workarounds

    [Theory]
    [InlineData("await-foreach")]
    public void FeatureCheck_UnsupportedFeature_HasWorkaround(string feature)
    {
        var info = FeatureSupport.GetFeatureInfo(feature);

        Assert.NotNull(info);
        Assert.NotNull(info.Workaround);
        Assert.NotEmpty(info.Workaround);
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_ReturnsWorkaroundText()
    {
        var workaround = FeatureSupport.GetWorkaround("await-foreach");

        Assert.Equal("Keep the containing member as exact C# interop", workaround);
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_DefaultDoesNotRecommendHandwrittenInterop()
    {
        var feature = new FeatureInfo
        {
            Name = "unsupported-without-workaround",
            Support = SupportLevel.NotSupported
        };

        var workaround = FeatureSupport.GetWorkaroundText(feature);

        Assert.NotNull(workaround);
        Assert.DoesNotContain("explicit interop", workaround);
        Assert.Contains("calor convert", workaround);
        Assert.Contains("original C#", workaround);
        Assert.Contains("converter-marked fallback", workaround);
        Assert.Contains("do not handwrite passthrough", workaround);
    }

    [Theory]
    [InlineData(SupportLevel.Full)]
    [InlineData(SupportLevel.Partial)]
    [InlineData(SupportLevel.ManualRequired)]
    public void FeatureCheck_GetWorkaround_NoDefaultForOtherSupportLevels(SupportLevel support)
    {
        var feature = new FeatureInfo { Name = "without-workaround", Support = support };

        Assert.Null(FeatureSupport.GetWorkaroundText(feature));
    }

    [Fact]
    public void FeatureCheck_GetWorkaround_PreservesExplicitWorkarounds()
    {
        foreach (var feature in FeatureSupport.GetAllFeatures().Where(feature => feature.Workaround != null))
        {
            Assert.Equal(feature.Workaround, FeatureSupport.GetWorkaround(feature.Name));
        }

        Assert.Null(FeatureSupport.GetWorkaround("unknown-feature"));
    }

    #endregion

    #region Feature Listing

    [Fact]
    public void FeatureCheck_GetAllFeatures_ReturnsNonEmpty()
    {
        var features = FeatureSupport.GetAllFeatures().ToList();

        Assert.NotEmpty(features);
        Assert.True(features.Count > 50, $"Expected > 50 features, got {features.Count}");
    }

    [Fact]
    public void FeatureCheck_GetFeaturesBySupport_FiltersCorrectly()
    {
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(notSupportedFeatures);
        Assert.All(fullFeatures, f => Assert.Equal(SupportLevel.Full, f.Support));
        Assert.All(notSupportedFeatures, f => Assert.Equal(SupportLevel.NotSupported, f.Support));
    }

    [Fact]
    public void FeatureCheck_AllLevelsHaveFeatures()
    {
        // At least the main levels (Full, Partial, NotSupported) should have features
        var fullFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Full).ToList();
        var partialFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.Partial).ToList();
        var notSupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported).ToList();

        Assert.NotEmpty(fullFeatures);
        Assert.NotEmpty(partialFeatures);
        Assert.NotEmpty(notSupportedFeatures);
    }

    #endregion

    #region Blocker Name Consistency

    [Theory]
    [InlineData("relational-pattern")]
    [InlineData("compound-pattern")]
    [InlineData("range-expression")]
    [InlineData("index-from-end")]
    [InlineData("target-typed-new")]
    [InlineData("null-conditional-method")]
    [InlineData("named-argument")]
    [InlineData("declaration-pattern")]
    [InlineData("throw-expression")]
    [InlineData("nested-generic-type")]
    [InlineData("out-var")]
    [InlineData("in-parameter")]
    [InlineData("checked-block")]
    [InlineData("with-expression")]
    [InlineData("init-accessor")]
    [InlineData("required-member")]
    [InlineData("list-pattern")]
    [InlineData("static-abstract-member")]
    [InlineData("ref-struct")]
    [InlineData("lock-statement")]
    [InlineData("await-foreach")]
    [InlineData("await-using")]
    [InlineData("using-declaration")]
    [InlineData("standalone-block")]
    [InlineData("scoped-parameter")]
    [InlineData("collection-expression")]
    [InlineData("readonly-struct")]
    [InlineData("default-lambda-parameter")]
    [InlineData("file-scoped-type")]
    [InlineData("utf8-string-literal")]
    [InlineData("generic-attribute")]
    [InlineData("using-type-alias")]
    public void FeatureCheck_BlockerName_ExistsInRegistry(string blockerName)
    {
        // All blocker names from MigrationAnalyzer should exist in FeatureSupport
        var info = FeatureSupport.GetFeatureInfo(blockerName);

        Assert.NotNull(info);
        Assert.Equal(blockerName, info.Name);
    }

    [Fact]
    public void FeatureCheck_AllBlockerNames_UseKebabCase()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            // All names should be lowercase kebab-case
            Assert.Equal(feature.Name.ToLowerInvariant(), feature.Name);
            Assert.DoesNotContain("_", feature.Name);
            Assert.DoesNotMatch(@"[A-Z]", feature.Name);
        }
    }

    [Theory]
    [InlineData("await foreach (var item in items) { }", "await-foreach")]
    [InlineData("await using (resource) { }", "await-using")]
    [InlineData("await using var resource = value;", "await-using")]
    [InlineData("using var resource = value;", "using-declaration")]
    [InlineData("public ref struct Buffer { }", "ref-struct")]
    [InlineData("file class LocalType { }", "file-scoped-type")]
    [InlineData("public void M(scoped ref int value) { }", "scoped-parameter")]
    [InlineData("public interface I { void M() { } }", "interface-method-semantics")]
    [InlineData("public interface I { int P { get => 1; } }", "interface-property-semantics")]
    [InlineData("public interface I { int this[int i] { get => i; } }", "interface-indexer-semantics")]
    [InlineData("public interface I { event System.Action Changed; }", "interface-member")]
    [InlineData("public void M() { { int x = 1; } }", "standalone-block")]
    [InlineData("public delegate T Factory<T>(T value);", "delegate-semantics")]
    [InlineData("[System.Obsolete] public delegate void Legacy();", "delegate-semantics")]
    [InlineData("public void M() { int value = 7; var reference = __makeref(value); }", "typed-reference")]
    [InlineData("public int M(System.TypedReference reference) => __refvalue(reference, int);", "typed-reference")]
    [InlineData("public System.Type M(System.TypedReference reference) => __reftype(reference);", "typed-reference")]
    [InlineData("public void M(__arglist) { }", "varargs")]
    [InlineData("public void M() { var arguments = __arglist; }", "varargs")]
    public void SyntaxCapabilityClassifier_DetectsRequiredUnsupportedFeature(
        string source,
        string expectedFeature)
    {
        var root = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();

        var detection = Assert.Single(
            SyntaxCapabilityClassifier.Detect(root)
                .Where(item => item.FeatureName == expectedFeature));

        Assert.Equal(expectedFeature, detection.FeatureName);
        Assert.True(detection.Line > 0);
        Assert.True(detection.Column > 0);
        Assert.True(detection.SpanLength > 0);
        Assert.Contains(expectedFeature, SyntaxCapabilityClassifier.RequiredUnsupportedFeatures);
    }

    [Fact]
    public void SyntaxCapabilityClassifier_InternalInterfaceConvertsNativelyWithItsVisibility()
    {
        const string source = "internal interface I { void M(); }";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        Assert.Empty(SyntaxCapabilityClassifier.Detect(root));

        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var definition = Assert.Single(converted.Ast!.Interfaces);
        Assert.Equal(Calor.Compiler.Ast.Visibility.Internal, definition.Visibility);
        Assert.Empty(converted.Ast.InteropBlocks);
        Assert.Empty(converted.Losses);

        var compiled = Program.Compile(converted.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("internal interface I", compiled.GeneratedCode);
    }

    [Theory]
    [InlineData("public interface I { void M() { } }", "interface-method-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { int P { get => 1; } }", "interface-property-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { int this[int i] { get => i; } }", "interface-indexer-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public interface I { event System.Action E; }", "interface-member", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public partial interface I { void M(); }", "interface-semantics", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public delegate T Factory<T>(T value);", "delegate-semantics", SyntaxKind.DelegateDeclaration)]
    [InlineData("public ref struct S { }", "ref-struct", SyntaxKind.StructDeclaration)]
    [InlineData("file class C { }", "file-scoped-type", SyntaxKind.ClassDeclaration)]
    [InlineData("public class C { public void M(scoped ref int x) { } }", "scoped-parameter", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public void M() { { int x = 1; } } }", "standalone-block", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public async System.Threading.Tasks.Task M() { await foreach (var x in xs) { } } }", "await-foreach", SyntaxKind.MethodDeclaration)]
    [InlineData("public class C { public async System.Threading.Tasks.Task M() { await using (resource) { } } }", "await-using", SyntaxKind.MethodDeclaration)]
    [InlineData("public record struct R(int X);", "record-struct", SyntaxKind.RecordStructDeclaration)]
    [InlineData("public partial record R(int X);", "record-partial", SyntaxKind.RecordDeclaration)]
    [InlineData("public unsafe record R(int X);", "unsafe", SyntaxKind.RecordDeclaration)]
    [InlineData("public record R([System.Obsolete] int X);", "record-positional-parameter-metadata", SyntaxKind.RecordDeclaration)]
    [InlineData("public class C { public void M() { for (int i = 0; i < 1; (i, i) = (1, 2)) { } } }", "tuple-deconstruction", SyntaxKind.ForStatement)]
    [InlineData("public class C { public void M() { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } } }", "for", SyntaxKind.ForStatement)]
    public void PreservationAuthority_ExistingGuardRequiresExactOriginalBoundary(
        string source, string feature, SyntaxKind guardKind)
    {
        var root = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var original = Assert.Single(root.DescendantNodes().Where(node => node.IsKind(guardKind)));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.StillRequired, decision.Kind);
        Assert.Equal(feature, decision.RequiredFeature);
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false
        }).Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Contains(converted.Losses, loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == feature);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invented-feature")]
    [InlineData("RECORD-STRUCT")]
    [InlineData("record-member-InventedSyntaxKind")]
    [InlineData("record-member-SimpleAssignmentExpression")]
    [InlineData("record-member-0")]
    [InlineData("binary pattern (xor)")]
    public void PreservationAuthority_UnknownReasonCannotBecomeApplicable(string? feature)
    {
        var original = CSharpSyntaxTree.ParseText("public record struct R;")
            .GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.UnknownOrMismatch, decision.Kind);
        Assert.Contains("calor convert", decision.Explanation);
        Assert.Null(decision.RequiredFeature);
    }

    [Theory]
    [InlineData("record-struct", "public record R;", SyntaxKind.RecordDeclaration)]
    [InlineData("record-partial", "public record R;", SyntaxKind.RecordDeclaration)]
    [InlineData("file-scoped-type", "public class C { }", SyntaxKind.ClassDeclaration)]
    [InlineData("interface-semantics", "internal interface I { void M(); }", SyntaxKind.InterfaceDeclaration)]
    [InlineData("await-using", "public class C { public void M() { } }", SyntaxKind.MethodDeclaration)]
    [InlineData("ref-struct", "file struct S { }", SyntaxKind.StructDeclaration)]
    public void PreservationAuthority_MissingOrDifferentGuardIsNotNativeProof(
        string feature, string source, SyntaxKind guardKind)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().Single(node => node.IsKind(guardKind));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.UnknownOrMismatch, decision.Kind);
        Assert.Contains("native support is not proven", decision.Explanation);
    }

    [Theory]
    [InlineData("await-foreach")]
    [InlineData("for")]
    [InlineData("record-struct")]
    [InlineData("preprocessor-disabled")]
    [InlineData("conversion-error")]
    [InlineData("default-expression")]
    [InlineData("whole-file-preservation")]
    public void PreservationAuthority_MissingOriginalContextIsExplicit(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public record struct R;")
            .GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        foreach (var (node, boundary) in new (SyntaxNode?, SyntaxNode?)[]
        {
            (null, null), (original, null), (null, original)
        })
        {
            var decision = FeatureSupport.AssessPreservation(feature, node, boundary);
            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
            Assert.Contains("calor convert", decision.Explanation);
        }
    }

    [Theory]
    [InlineData("checked-block")]
    [InlineData("conversion-error")]
    [InlineData("post-validation-fallback")]
    [InlineData("whole-file-preservation")]
    [InlineData("async-return-signature")]
    [InlineData("alias-qualified-value")]
    [InlineData("default-expression")]
    [InlineData("null-forgiving-expression")]
    [InlineData("dictionary-initializer")]
    [InlineData("explicit-interface")]
    [InlineData("record-unresolved-base-kind")]
    [InlineData("class-unresolved-base-kind")]
    [InlineData("preprocessor-disabled")]
    [InlineData("preprocessor-member")]
    [InlineData("preprocessor-ownership-fallback")]
    [InlineData("preprocessor-unparsed-remainder")]
    [InlineData("iterator-accessor")]
    [InlineData("partial-property")]
    [InlineData("compound-assignment-order")]
    [InlineData("binary pattern (and)")]
    [InlineData("record-member-EventFieldDeclaration")]
    [InlineData("method")]
    [InlineData("localfunction")]
    public void PreservationAuthority_ContextualAndDynamicProducersRemainUnresolved(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public class C { }")
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.True(FeatureSupport.IsKnownPreservationReason(feature));
        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        Assert.Contains("context", decision.Explanation);
    }

    [Fact]
    public void PreservationAuthority_SourceTreeAndBoundaryIdentityCannotBeReconstructedFromPayload()
    {
        const string source = "public record struct R;";
        var original = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();
        var copy = CSharpSyntaxTree.ParseText(source).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();
        var detached = SyntaxFactory.ParseMemberDeclaration(source)!;

        foreach (var (node, boundary) in new[]
        {
            (original, copy), (detached, detached)
        })
        {
            var decision = FeatureSupport.AssessPreservation("record-struct", node, boundary);
            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        }
    }

    [Fact]
    public void PreservationAuthority_ChangedBoundaryIsNotAValidReasonOrNativeProof()
    {
        var root = CSharpSyntaxTree.ParseText(
            "public class C { public void M(System.IDisposable d) { using var item = d; } }").GetRoot();
        var guard = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var oldBoundary = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", guard, oldBoundary);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("using-declaration", decision.RequiredFeature);
        Assert.Contains("calor convert", decision.Explanation);
    }

    [Theory]
    [InlineData("#if OFF\nnot C#\n#endif\npublic record struct R;")]
    [InlineData("[assembly: System.CLSCompliant(true)] public record struct R;")]
    [InlineData("namespace A { public record struct R; } namespace B { class C { } }")]
    [InlineData("public record struct R(")]
    public void PreservationAuthority_ConditionalMalformedAndUnitContextsStayUnresolved(string source)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().OfType<RecordDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("record-struct", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Fact]
    public void PreservationAuthority_LocalFunctionPreflightNeedsItsActualConversionDecision()
    {
        var root = CSharpSyntaxTree.ParseText(
            "public class C { public void M(System.IDisposable d) { using var item = d; void Local() { } } }")
            .GetRoot();
        var original = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Theory]
    [InlineData("public class C { public void M(ref int n) { for (ref int i = ref n; i < 1; i++) { } } }", "for", SyntaxKind.ForStatement)]
    [InlineData("public unsafe class C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public record C { public void M(System.IDisposable d) { using var item = d; } }", "using-declaration", SyntaxKind.MethodDeclaration)]
    [InlineData("public interface I { void M(); }", "interface-member", SyntaxKind.InterfaceDeclaration)]
    [InlineData("public class C { public record struct R; }", "record-struct", SyntaxKind.RecordStructDeclaration)]
    [InlineData("public class C { public void M() { System.Action run = () => { for (int i = 0; int.TryParse(\"0\", out var n); i++) { } }; } }", "for", SyntaxKind.ForStatement)]
    public void PreservationAuthority_EnclosingPreflightAndRescueCannotBeAssumed(
        string source, string feature, SyntaxKind guardKind)
    {
        var original = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
            .DescendantNodes().Single(node => node.IsKind(guardKind));

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    [Fact]
    public void PreservationAuthority_EnclosingUnsupportedOwnerChangesTheBoundary()
    {
        var original = CSharpSyntaxTree.ParseText(
                "file class C { public void M(System.IDisposable d) { using var item = d; } }")
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("using-declaration", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("file-scoped-type", decision.RequiredFeature);
    }

    [Fact]
    public void PreservationAuthority_RefLocalPreflightChangesLoopToMemberBoundary()
    {
        var original = CSharpSyntaxTree.ParseText(
                "public class C { private int n; public void M() { for (ref int i = ref n; i < 1; i++) { } } }")
            .GetRoot().DescendantNodes().OfType<ForStatementSyntax>().Single();

        var decision = FeatureSupport.AssessPreservation("for", original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.ChangedBoundary, decision.Kind);
        Assert.Equal("ref-parameter", decision.RequiredFeature);
    }

    [Fact]
    public void PreservationAuthority_FullRegistrySupportNeverProvesNativeConversion()
    {
        foreach (var feature in new[] { "class", "record", "postfix-operator", "explicit-interface" })
        {
            Assert.Equal(SupportLevel.Full, FeatureSupport.GetSupportLevel(feature));
            var original = CSharpSyntaxTree.ParseText("public class C { }").GetRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

            var decision = FeatureSupport.AssessPreservation(feature, original, original);

            Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
        }
    }

    [Theory]
    [InlineData("whole-file-preservation")]
    [InlineData("namespace-topology")]
    [InlineData("post-validation-fallback")]
    public void PreservationAuthority_UnitScopeDoesNotSupplyMissingPipelineContext(string feature)
    {
        var original = CSharpSyntaxTree.ParseText("public class C { }").GetCompilationUnitRoot();

        var decision = FeatureSupport.AssessPreservation(feature, original, original);

        Assert.Equal(FeatureSupport.PreservationDecisionKind.InsufficientContext, decision.Kind);
    }

    #endregion

    #region Description Quality

    [Fact]
    public void FeatureCheck_AllFeatures_HaveDescription()
    {
        var features = FeatureSupport.GetAllFeatures();

        foreach (var feature in features)
        {
            Assert.NotNull(feature.Description);
            Assert.NotEmpty(feature.Description);
        }
    }

    [Fact]
    public void FeatureCheck_UnsupportedFeatures_HaveWorkaround()
    {
        var unsupportedFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.NotSupported);

        foreach (var feature in unsupportedFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    [Fact]
    public void FeatureCheck_ManualRequiredFeatures_HaveWorkaround()
    {
        var manualFeatures = FeatureSupport.GetFeaturesBySupport(SupportLevel.ManualRequired);

        foreach (var feature in manualFeatures)
        {
            Assert.NotNull(feature.Workaround);
            Assert.NotEmpty(feature.Workaround);
        }
    }

    #endregion
}
