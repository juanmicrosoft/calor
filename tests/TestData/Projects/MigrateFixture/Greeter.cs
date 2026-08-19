namespace MigrateFixture
{
    public static class Greeter
    {
        public static string Greet(string name)
        {
            return "Hello, " + name + "!";
        }

        public static string GreetLoud(string name)
        {
            return Greet(name).ToUpper();
        }
    }
}
