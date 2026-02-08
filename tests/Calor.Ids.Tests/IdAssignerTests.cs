using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

public class IdAssignerTests
{
    [Fact]
    public void AssignIds_AssignsMissingModuleId()
    {
        var content = "§M{:TestModule}\n§/M{}";

        var (newContent, assignments) = IdAssigner.AssignIds(content, "test.calr");

        Assert.Single(assignments);
        Assert.Equal(IdKind.Module, assignments[0].Kind);
        Assert.Equal("TestModule", assignments[0].Name);
        Assert.True(string.IsNullOrEmpty(assignments[0].OldId));
        Assert.StartsWith("m_", assignments[0].NewId);
        Assert.Contains(assignments[0].NewId, newContent);
    }

    [Fact]
    public void AssignIds_AssignsMissingFunctionId()
    {
        var content = "§F{:TestFunc:pub}\n§O{void}\n§/F{}";

        var (newContent, assignments) = IdAssigner.AssignIds(content, "test.calr");

        Assert.Single(assignments);
        Assert.Equal(IdKind.Function, assignments[0].Kind);
        Assert.Equal("TestFunc", assignments[0].Name);
        Assert.StartsWith("f_", assignments[0].NewId);
    }

    [Fact]
    public void AssignIds_PreservesExistingIds()
    {
        var content = "§F{f001:TestFunc:pub}\n§O{void}\n§/F{f001}";
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (newContent, assignments) = IdAssigner.AssignIds(content, "test.calr", false, existingIds);

        Assert.Empty(assignments);
        Assert.Equal(content, newContent);
        Assert.Contains("f001", existingIds); // ID should be tracked
    }

    [Fact]
    public void AssignIds_FixesDuplicates()
    {
        // Content with duplicate f001 IDs - first occurrence should be kept, second should be reassigned
        var content = "§F{f001:Func1:pub}\n§O{void}\n§/F{f001}\n§F{f001:Func2:pub}\n§O{void}\n§/F{f001}";
        // Start with empty existingIds - duplicates are detected within the file
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var (newContent, assignments) = IdAssigner.AssignIds(content, "test.calr", fixDuplicates: true, existingIds);

        // Only the second occurrence (Func2) should get a new ID
        Assert.Single(assignments);
        Assert.Equal("Func2", assignments[0].Name);
        Assert.Equal("f001", assignments[0].OldId);
        Assert.StartsWith("f_", assignments[0].NewId);
        Assert.NotEqual("f001", assignments[0].NewId);
    }

    [Fact]
    public void AssignIds_GeneratesUniqueIds()
    {
        var content = "§F{:Func1:pub}\n§O{void}\n§/F{}\n§F{:Func2:pub}\n§O{void}\n§/F{}";

        var (_, assignments) = IdAssigner.AssignIds(content, "test.calr");

        Assert.Equal(2, assignments.Count);
        Assert.NotEqual(assignments[0].NewId, assignments[1].NewId);
    }

    [Fact]
    public void AssignIds_HandlesAllDeclarationTypes()
    {
        var content = """
            §M{:Module}
            §F{:Func:pub}
            §O{void}
            §/F{}
            §CL{:Class}
            §MT{:Method:pub}
            §O{void}
            §/MT{}
            §PROP{:Prop:i32:pub}
            §GET
            §/PROP{}
            §CTOR{:pub}
            §/CTOR{}
            §/CL{}
            §IFACE{:IFace}
            §/IFACE{}
            §ENUM{:Status}
            Active
            §/ENUM{}
            §/M{}
            """;

        var (_, assignments) = IdAssigner.AssignIds(content, "test.calr");

        // Should assign IDs for: Module, Function, Class, Method, Property, Constructor, Interface, Enum
        Assert.True(assignments.Count >= 7);
        Assert.Contains(assignments, a => a.Kind == IdKind.Module);
        Assert.Contains(assignments, a => a.Kind == IdKind.Function);
        Assert.Contains(assignments, a => a.Kind == IdKind.Class);
        Assert.Contains(assignments, a => a.Kind == IdKind.Method);
        Assert.Contains(assignments, a => a.Kind == IdKind.Property);
        Assert.Contains(assignments, a => a.Kind == IdKind.Constructor);
        Assert.Contains(assignments, a => a.Kind == IdKind.Interface);
    }

    [Fact]
    public void UpdateClosingTags_UpdatesModuleClosingTag()
    {
        var content = "§M{old_id:Module}\n§/M{old_id}";
        var assignments = new List<IdAssignment>
        {
            new("test.calr", IdKind.Module, "Module", 1, "old_id", "m_01J5X7K9M2NPQRSTABWXYZ1234")
        };

        var result = IdAssigner.UpdateClosingTags(content, assignments);

        Assert.Contains("§/M{m_01J5X7K9M2NPQRSTABWXYZ1234}", result);
    }

    [Fact]
    public void UpdateClosingTags_UpdatesFunctionClosingTag()
    {
        var content = "§F{old_id:Func:pub}\n§O{void}\n§/F{old_id}";
        var assignments = new List<IdAssignment>
        {
            new("test.calr", IdKind.Function, "Func", 1, "old_id", "f_01J5X7K9M2NPQRSTABWXYZ1234")
        };

        var result = IdAssigner.UpdateClosingTags(content, assignments);

        Assert.Contains("§/F{f_01J5X7K9M2NPQRSTABWXYZ1234}", result);
    }

    [Fact]
    public void AssignIds_RecordsLineNumber()
    {
        var content = "§M{m001:Module}\n§F{:Func:pub}\n§O{void}\n§/F{}";

        var (_, assignments) = IdAssigner.AssignIds(content, "test.calr");

        Assert.Single(assignments);
        Assert.Equal(2, assignments[0].Line); // Function is on line 2
    }

    [Fact]
    public void AssignIds_RecordsFilePath()
    {
        var content = "§F{:Func:pub}\n§O{void}\n§/F{}";

        var (_, assignments) = IdAssigner.AssignIds(content, "/path/to/test.calr");

        Assert.Single(assignments);
        Assert.Equal("/path/to/test.calr", assignments[0].FilePath);
    }
}
