using System.Text;

namespace Calor.Performance.Tests;

/// <summary>
/// Generates synthetic Calor code for performance testing.
/// Creates modules with configurable numbers of functions and statements.
/// </summary>
public static class SyntheticCodeGenerator
{
    /// <summary>
    /// Generates a synthetic Calor module with the specified complexity.
    /// </summary>
    /// <param name="functions">Number of functions to generate.</param>
    /// <param name="statementsPerFunction">Number of statements per function.</param>
    /// <param name="includeLoops">Whether to include loop constructs.</param>
    /// <param name="includeConditionals">Whether to include if/else constructs.</param>
    /// <returns>Generated Calor source code.</returns>
    public static string Generate(
        int functions = 10,
        int statementsPerFunction = 50,
        bool includeLoops = true,
        bool includeConditionals = true)
    {
        var sb = new StringBuilder();

        sb.AppendLine("§M{m001:PerformanceTestModule}");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            GenerateFunction(sb, f, statementsPerFunction, includeLoops, includeConditionals);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a module optimized for taint analysis testing.
    /// </summary>
    public static string GenerateTaintModule(int functions = 10, int dataFlowDepth = 10)
    {
        var sb = new StringBuilder();

        sb.AppendLine("§M{m001:TaintTestModule}");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            var funcId = $"f{f:D3}";
            sb.AppendLine($"  §F{{{funcId}:TaintFunc{f}:pub}}");
            sb.AppendLine($"    §I{{string:user_input}}");
            sb.AppendLine($"    §O{{string}}");

            // Create a chain of data flow
            sb.AppendLine($"    §B{{var1:string}} user_input");
            for (var v = 2; v <= dataFlowDepth; v++)
            {
                sb.AppendLine($"    §B{{var{v}:string}} (+ var{v - 1} STR:\"x\")");
            }

            // Add a sink at the end (some functions are vulnerable, some are not)
            if (f % 3 == 0)
            {
                // Vulnerable: direct use of tainted data
                sb.AppendLine($"    §C{{db.execute}} §A var{dataFlowDepth} §/C");
            }
            else if (f % 3 == 1)
            {
                // Safe: sanitized
                sb.AppendLine($"    §B{{safe:string}} §C{{sanitize}} §A var{dataFlowDepth} §/C");
                sb.AppendLine($"    §C{{db.execute}} §A safe §/C");
            }
            else
            {
                // Safe: no sink
                sb.AppendLine($"    §C{{print}} §A var{dataFlowDepth} §/C");
            }

            sb.AppendLine($"    §R var{dataFlowDepth}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a module optimized for loop analysis testing.
    /// </summary>
    public static string GenerateLoopModule(int functions = 10, int loopsPerFunction = 5, int nestingDepth = 2)
    {
        var sb = new StringBuilder();

        sb.AppendLine("§M{m001:LoopTestModule}");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            var funcId = $"f{f:D3}";
            sb.AppendLine($"  §F{{{funcId}:LoopFunc{f}:pub}}");
            sb.AppendLine($"    §I{{i32:n}}");
            sb.AppendLine($"    §O{{i32}}");
            sb.AppendLine($"    §B{{result:i32}} INT:0");

            for (var l = 1; l <= loopsPerFunction; l++)
            {
                GenerateNestedLoop(sb, l, nestingDepth, 2);
            }

            sb.AppendLine($"    §R result");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a module with complex control flow.
    /// </summary>
    public static string GenerateControlFlowModule(int functions = 10, int branchDepth = 5)
    {
        var sb = new StringBuilder();

        sb.AppendLine("§M{m001:ControlFlowTestModule}");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            var funcId = $"f{f:D3}";
            sb.AppendLine($"  §F{{{funcId}:ControlFlow{f}:pub}}");
            sb.AppendLine($"    §I{{i32:x}}");
            sb.AppendLine($"    §I{{i32:y}}");
            sb.AppendLine($"    §O{{i32}}");
            sb.AppendLine($"    §B{{result:i32}} INT:0");

            GenerateNestedConditionals(sb, branchDepth, 2);

            sb.AppendLine($"    §R result");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void GenerateFunction(
        StringBuilder sb,
        int functionNumber,
        int statements,
        bool includeLoops,
        bool includeConditionals)
    {
        var funcId = $"f{functionNumber:D3}";
        sb.AppendLine($"  §F{{{funcId}:TestFunc{functionNumber}:pub}}");
        sb.AppendLine($"    §I{{i32:x}}");
        sb.AppendLine($"    §I{{i32:y}}");
        sb.AppendLine($"    §O{{i32}}");

        var varCounter = 0;
        var remainingStatements = statements;

        while (remainingStatements > 0)
        {
            var stmtType = remainingStatements % 5;

            switch (stmtType)
            {
                case 0:
                    // Simple binding
                    varCounter++;
                    sb.AppendLine($"    §B{{v{varCounter}:i32}} (+ x INT:{varCounter})");
                    remainingStatements--;
                    break;

                case 1:
                    // Binary operation
                    varCounter++;
                    sb.AppendLine($"    §B{{v{varCounter}:i32}} (* y INT:{varCounter})");
                    remainingStatements--;
                    break;

                case 2 when includeConditionals && remainingStatements >= 3:
                    // Conditional
                    varCounter++;
                    sb.AppendLine($"    §IF{{if{functionNumber}_{varCounter}}} (> x INT:0)");
                    sb.AppendLine($"      §B{{v{varCounter}:i32}} (+ x y)");
                    sb.AppendLine($"    §EL");
                    varCounter++;
                    sb.AppendLine($"      §B{{v{varCounter}:i32}} (- x y)");
                    remainingStatements -= 3;
                    break;

                case 3 when includeLoops && remainingStatements >= 4:
                    // For loop
                    var loopId = $"l{varCounter}";
                    sb.AppendLine($"    §B{{sum{varCounter}:i32}} INT:0");
                    sb.AppendLine($"    §L{{{loopId}:i:1:10:1}}");
                    sb.AppendLine($"      §B{{sum{varCounter}:i32}} (+ sum{varCounter} i)");
                    remainingStatements -= 4;
                    break;

                case 4:
                    // Function call
                    varCounter++;
                    sb.AppendLine($"    §B{{v{varCounter}:i32}} §C{{math.abs}} §A x §/C");
                    remainingStatements--;
                    break;

                default:
                    // Default: simple binding
                    varCounter++;
                    sb.AppendLine($"    §B{{v{varCounter}:i32}} INT:{remainingStatements}");
                    remainingStatements--;
                    break;
            }
        }

        // Final result
        if (varCounter > 0)
        {
            sb.AppendLine($"    §R v{varCounter}");
        }
        else
        {
            sb.AppendLine($"    §R (+ x y)");
        }
    }

    private static void GenerateNestedLoop(StringBuilder sb, int loopNum, int depth, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var loopId = $"l{loopNum}d{depth}";

        sb.AppendLine($"{indentStr}§L{{{loopId}:i{depth}:0:n:1}}");

        if (depth > 1)
        {
            GenerateNestedLoop(sb, loopNum, depth - 1, indent + 1);
        }
        else
        {
            sb.AppendLine($"{indentStr}  §B{{result:i32}} (+ result i{depth})");
        }
    }

    private static void GenerateNestedConditionals(StringBuilder sb, int depth, int indent)
    {
        var indentStr = new string(' ', indent * 2);

        if (depth <= 0)
        {
            sb.AppendLine($"{indentStr}§B{{result:i32}} (+ x y)");
            return;
        }

        sb.AppendLine($"{indentStr}§IF{{if{depth}}} (> x INT:{depth})");
        GenerateNestedConditionals(sb, depth - 1, indent + 1);
        sb.AppendLine($"{indentStr}§EI (< x INT:-{depth})");
        sb.AppendLine($"{indentStr}  §B{{result:i32}} (- x y)");
        sb.AppendLine($"{indentStr}§EL");
        sb.AppendLine($"{indentStr}  §B{{result:i32}} (* x y)");
    }
}
