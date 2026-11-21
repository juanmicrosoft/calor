using Calor.Compiler.Ast;
using Calor.Compiler.Ids;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Refactoring;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30: project passes (index, cross-module map) attach identities as one batch. Attaching
/// file by file reconciled the whole store once per file, re-parsing every registered source
/// each time, so indexing the 106-file fixture-10k project took ~85s instead of well under 1s.
/// The batch must hand every file exactly the identities the per-file call does.
/// </summary>
public sealed class DeclarationIdentityBatchAttachTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-identity-batch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Module(int index, string extra = "") => $$"""
        module M{{index}} M{{index}}
          func Add{{index}} (int:x) → int
            !
            return x + {{index}}
          class Box{{index}}
            field int:Value
            func Get () → int
              !
              return Value
        {{extra}}
        """;

    private string Write(string relative, string source)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private static ModuleNode Parse(string file, string source)
    {
        var parsed = CalorSourceHelper.Parse(source);
        Assert.True(parsed.IsSuccess, string.Join("; ", parsed.Errors));
        return parsed.Ast!;
    }

    private static IReadOnlyList<(string Name, string Id)> Ids(ModuleNode module, string file) =>
        new IdScanner().Scan(module, file).Select(entry => (entry.Name, entry.Id)).ToList();

    [Fact]
    public void BatchAttach_GivesEveryFileThePerFileIdentities()
    {
        var files = Enumerable.Range(1, 6).Select(index => Write($"src/m{index}.calr", Module(index))).ToList();
        var persisted = new DeclarationIdentityStore(_root).Sync(files).Declarations.Select(d => d.Id).ToHashSet();
        // Registered elsewhere but not an input of this pass, and an unregistered input.
        var outsider = Write("src/unregistered.calr", Module(99));
        var inputs = files.Append(outsider).ToList();
        // An in-memory buffer with an unsaved addition: the addition gets no persisted identity.
        var sources = inputs.ToDictionary(file => file, File.ReadAllText);
        sources[files[2]] = Module(3, "  func Unsaved () → int\n    !\n    return 0\n");

        var perFile = inputs.Select(file => (File: file, Module: Parse(file, sources[file]))).ToList();
        foreach (var (file, module) in perFile)
            DeclarationIdentityStore.AttachIfRegistered(file, sources[file], module);
        var batched = inputs.Select(file => (File: file, Module: Parse(file, sources[file]))).ToList();
        DeclarationIdentityStore.AttachAllIfRegistered(batched.Select(input => (input.File, sources[input.File], input.Module)));

        for (var i = 0; i < inputs.Count; i++)
            Assert.Equal(Ids(perFile[i].Module, inputs[i]), Ids(batched[i].Module, inputs[i]));
        // Unattached declarations only carry scanner placeholders, never a persisted identity.
        Assert.All(Ids(batched[0].Module, inputs[0]), entry => Assert.Contains(entry.Id, persisted));
        Assert.All(Ids(batched[^1].Module, outsider), entry => Assert.DoesNotContain(entry.Id, persisted));
        Assert.Contains(Ids(batched[2].Module, files[2]), entry => entry.Name == "Unsaved" && !persisted.Contains(entry.Id));
        Assert.Contains(Ids(batched[2].Module, files[2]), entry => entry.Name == "Add3" && persisted.Contains(entry.Id));
    }

    [Fact]
    public void ProjectSymbolIndex_AttachesTheSameIdentitiesAsTheStore()
    {
        var files = Enumerable.Range(1, 4).Select(index => Write($"m{index}.calr", Module(index))).ToList();
        var index = ProjectSymbolIndex.Build(files, out var skipped, initializeIdentityRoot: _root);
        Assert.Empty(skipped);
        var state = new DeclarationIdentityStore(_root).Sync();
        Assert.Equal(4, state.Files.Count);
        foreach (var document in index.Documents)
        {
            var expected = Parse(document.FilePath, document.Source);
            DeclarationIdentityStore.AttachIfRegistered(document.FilePath, document.Source, expected);
            var ids = Ids(document.Ast, document.FilePath);
            Assert.Equal(Ids(expected, document.FilePath), ids);
            Assert.All(ids, entry => Assert.Contains(state.Declarations, declaration => declaration.Id == entry.Id));
        }
    }
}
