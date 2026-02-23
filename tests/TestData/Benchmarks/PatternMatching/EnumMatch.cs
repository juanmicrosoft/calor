using System;
namespace PatternMatching
{
    public static class EnumMatch
    {
        public static string HttpStatus(int code) => code switch
        {
            200 => "OK", 404 => "Not Found", 500 => "Server Error",
            301 => "Redirect", _ => "Unknown"
        };
    }
}
