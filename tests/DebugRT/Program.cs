using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;

var converter = new CSharpToCalorConverter();
var emitter = new CSharpEmitter();

void TestRoundTrip(string label, string csharpInput)
{
    Console.WriteLine($"\n=== {label} ===");
    var result = converter.Convert(csharpInput);
    Console.WriteLine("--- CALOR ---");
    Console.WriteLine(result.CalorSource);
    var diag = new DiagnosticBag();
    diag.SetFilePath("test.calr");
    var lexer = new Lexer(result.CalorSource!, diag);
    var tokens = lexer.TokenizeAll();
    var parser = new Parser(tokens, diag);
    var module = parser.Parse();
    if (diag.HasErrors)
    {
        Console.WriteLine("--- PARSE ERRORS ---");
        foreach (var d in diag) Console.WriteLine(d.Message);
    }
    var csharp = emitter.Emit(module);
    Console.WriteLine("--- EMITTED C# ---");
    Console.WriteLine(csharp);
}

TestRoundTrip("SizeOf", @"
public static class Test
{
    public static int GetSizeOfInt()
    {
        return sizeof(int);
    }
}");

TestRoundTrip("UnsafeBlock", @"
public static class Test
{
    public static void DoUnsafe()
    {
        unsafe
        {
            int x = 42;
        }
    }
}");

TestRoundTrip("StackAlloc", @"
public static class Test
{
    public static unsafe int Sum()
    {
        int* ptr = stackalloc int[3];
        return 0;
    }
}");

TestRoundTrip("FixedStatement", @"
public static class Test
{
    public static unsafe void DoFixed(int[] arr)
    {
        fixed (int* ptr = arr)
        {
            int val = *ptr;
        }
    }
}");

TestRoundTrip("AddressOf", @"
public static class Test
{
    public static unsafe void TakeAddr()
    {
        int x = 42;
        int* ptr = &x;
    }
}");

TestRoundTrip("PointerDeref", @"
public static class Test
{
    public static unsafe int Deref(int* ptr)
    {
        return *ptr;
    }
}");

TestRoundTrip("MultiDimCreate", @"
public static class Test
{
    public static int[,] CreateGrid(int rows, int cols)
    {
        int[,] grid = new int[rows, cols];
        return grid;
    }
}");

TestRoundTrip("MultiDimAccess", @"
public static class Test
{
    public static int GetElement(int[,] grid, int row, int col)
    {
        return grid[row, col];
    }
}");

TestRoundTrip("StackAllocInit", @"
public static class Test
{
    public static unsafe int Sum()
    {
        int* ptr = stackalloc int[] { 1, 2, 3 };
        return 0;
    }
}");

TestRoundTrip("MultiDimInit", @"
public static class Test
{
    public static int[,] CreateInitialized()
    {
        int[,] matrix = new int[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 }
        };
        return matrix;
    }
}");

TestRoundTrip("UnsafeMethod", @"
public static class Test
{
    public static unsafe int* Alloc()
    {
        return (int*)0;
    }
}");
