namespace TupleTypes
{
    public static class TupleTypeExamples
    {
        public static (int, string) GetPair()
        {
            return (42, "hello");
        }

        public static (int x, string y) GetNamedPair()
        {
            return (x: 1, y: "world");
        }

        public static (int, (string, bool)) GetNested()
        {
            return (1, ("nested", true));
        }

        public static int SumTuple((int, int) pair)
        {
            return pair.Item1 + pair.Item2;
        }

        public static string DescribePerson((string Name, int Age) person)
        {
            return person.Name + " is " + person.Age;
        }
    }
}
