using Quotes;

AssertEqual(5, QuotesModule.Add(2, 3), "Add positive");
AssertEqual(-1, QuotesModule.Add(2, -3), "Add negative");
AssertEqual(100, QuotesModule.ClampToCap(250, 100), "Clamp above cap");
AssertEqual(50, QuotesModule.ClampToCap(50, 100), "Clamp below cap");

// The contract on this function is deliberately refutable (the verify canary);
// use inputs satisfying the runtime guard.
AssertEqual(
    50,
    QuotesModule.QuoteWithSurchargeDefective(30, 20, 60),
    "Refutable verification canary remains callable");

Console.WriteLine("SDK consumer assertions passed.");

static void AssertEqual(int expected, int actual, string scenario)
{
    if (expected != actual)
    {
        throw new InvalidOperationException(
            $"{scenario}: expected {expected}, actual {actual}");
    }
}
