public record Point(int X, int Y)
{
    public int Sum() => X + Y;
}

public static class RecordFixture
{
    public static int Run()
    {
        var a = new Point(3, 4);
        var b = new Point(3, 4);
        var c = a with { Y = 10 };
        if (!(a == b) || !a.Equals(b) || a.GetHashCode() != b.GetHashCode()) return -1;
        if (a == c || c.X != 3 || c.Y != 10) return -2;
        if (a.ToString() != "Point { X = 3, Y = 4 }") return -3;
        return c.X * 1000 + c.Y * 10 + a.Sum();
    }
}
