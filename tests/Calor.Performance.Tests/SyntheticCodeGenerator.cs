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

        sb.AppendLine("module PerformanceTestModule PerformanceTestModule block");
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

        sb.AppendLine("module TaintTestModule TaintTestModule block");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            sb.AppendLine($"  func TaintFunc{f} (string:user_input) → string");
            // Public boundaries declare their row (Calor0124): database writes for the two
            // sink shapes, console writes for the print shape.
            sb.AppendLine(f % 3 == 2 ? "    ! cw" : "    ! db:w");

            // Create a chain of data flow
            sb.AppendLine($"    let string:var1 = user_input");
            for (var v = 2; v <= dataFlowDepth; v++)
            {
                sb.AppendLine($"    let string:var{v} = var{v - 1} + \"x\"");
            }

            // Add a sink at the end (some functions are vulnerable, some are not)
            if (f % 3 == 0)
            {
                // Vulnerable: direct use of tainted data. ExecuteSql is the built-in bare-name
                // SQL sink: native source cannot call through the undeclared `db` receiver the
                // legacy `db.execute` target named, and the sink category is the same.
                sb.AppendLine($"    ExecuteSql(var{dataFlowDepth})");
            }
            else if (f % 3 == 1)
            {
                // Safe: sanitized
                sb.AppendLine($"    let string:safe = sanitize(var{dataFlowDepth})");
                sb.AppendLine($"    ExecuteSql(safe)");
            }
            else
            {
                // Safe: no sink
                sb.AppendLine($"    print(var{dataFlowDepth})");
            }

            sb.AppendLine($"    return var{dataFlowDepth}");
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

        sb.AppendLine("module LoopTestModule LoopTestModule block");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            sb.AppendLine($"  func LoopFunc{f} (int:n) → int");
            sb.AppendLine("    !");
            sb.AppendLine($"    let int:result = 0");

            for (var l = 1; l <= loopsPerFunction; l++)
            {
                GenerateNestedLoop(sb, nestingDepth, 2);
            }

            sb.AppendLine($"    return result");
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

        sb.AppendLine("module ControlFlowTestModule ControlFlowTestModule block");
        sb.AppendLine();

        for (var f = 1; f <= functions; f++)
        {
            sb.AppendLine($"  func ControlFlow{f} (int:x, int:y) → int");
            sb.AppendLine("    !");
            sb.AppendLine($"    let int:result = 0");

            GenerateNestedConditionals(sb, branchDepth, 2);

            sb.AppendLine($"    return result");
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
        sb.AppendLine($"  func TestFunc{functionNumber} (int:x, int:y) → int");
        sb.AppendLine("    !");

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
                    sb.AppendLine($"    let int:v{varCounter} = x + {varCounter}");
                    remainingStatements--;
                    break;

                case 1:
                    // Binary operation
                    varCounter++;
                    sb.AppendLine($"    let int:v{varCounter} = y * {varCounter}");
                    remainingStatements--;
                    break;

                case 2 when includeConditionals && remainingStatements >= 3:
                    // Conditional
                    varCounter++;
                    sb.AppendLine($"    if x > 0");
                    sb.AppendLine($"      let int:v{varCounter} = x + y");
                    sb.AppendLine($"    else");
                    varCounter++;
                    sb.AppendLine($"      let int:v{varCounter} = x - y");
                    remainingStatements -= 3;
                    break;

                case 3 when includeLoops && remainingStatements >= 4:
                    // For loop. The body re-binds sum as the legacy workload did, so the bound
                    // tree (and the registered pipeline counters) stay those of the legacy shape.
                    sb.AppendLine($"    let int:sum{varCounter} = 0");
                    sb.AppendLine($"    for i from 1 to 10 step 1");
                    sb.AppendLine($"      let int:sum{varCounter} = sum{varCounter} + i");
                    remainingStatements -= 4;
                    break;

                case 4:
                    // Function call: an unresolved external call, as the legacy `math.abs`
                    // target was (a resolved System.Math.Abs would add metadata binding work).
                    varCounter++;
                    sb.AppendLine($"    let int:v{varCounter} = abs(x)");
                    remainingStatements--;
                    break;

                default:
                    // Default: simple binding
                    varCounter++;
                    sb.AppendLine($"    let int:v{varCounter} = {remainingStatements}");
                    remainingStatements--;
                    break;
            }
        }

        // Final result
        if (varCounter > 0)
        {
            sb.AppendLine($"    return v{varCounter}");
        }
        else
        {
            sb.AppendLine($"    return x + y");
        }
    }

    private static void GenerateNestedLoop(StringBuilder sb, int depth, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        sb.AppendLine($"{indentStr}for i{depth} from 0 to n step 1");

        if (depth > 1)
        {
            GenerateNestedLoop(sb, depth - 1, indent + 1);
        }
        else
        {
            sb.AppendLine($"{indentStr}  let int:result = result + i{depth}");
        }
    }

    private static void GenerateNestedConditionals(StringBuilder sb, int depth, int indent)
    {
        var indentStr = new string(' ', indent * 2);

        if (depth <= 0)
        {
            sb.AppendLine($"{indentStr}let int:result = x + y");
            return;
        }

        sb.AppendLine($"{indentStr}if x > {depth}");
        GenerateNestedConditionals(sb, depth - 1, indent + 1);
        sb.AppendLine($"{indentStr}else if x < -{depth}");
        sb.AppendLine($"{indentStr}  let int:result = x - y");
        sb.AppendLine($"{indentStr}else");
        sb.AppendLine($"{indentStr}  let int:result = x * y");
    }
}
