public static class SurrogateValues
{
    public static int Verify()
    {
        char high = '\ud800';
        char low = '\udfff';
        return (int)high * 2 + (int)low;
    }
}
