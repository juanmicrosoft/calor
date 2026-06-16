using System;

namespace FormatHeader
{
    public static class FormatHeaderModule
    {
        public static string Format(string title)
        {
            return title;
        }

        public static string Render(string title)
        {
            var header = Format(title);
            return header;
        }
    }
}
