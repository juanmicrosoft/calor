// C# equivalent - no effect system
public class StateService
{
    private int counter = 0;

    // Effect: state mutation (not tracked in C#)
    public int IncrementCounter() => ++counter;
    public int ResetCounter() { counter = 0; return counter; }

    // Pure read
    public int ReadCounter() => counter;
}
