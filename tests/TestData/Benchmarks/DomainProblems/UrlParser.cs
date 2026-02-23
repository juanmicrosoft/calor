using System;

namespace DomainProblems
{
    public static class UrlParser
    {
        public static bool HasProtocol(string url) => url.StartsWith("http://") || url.StartsWith("https://");
        public static bool IsSecure(string url) => url.StartsWith("https://");
        public static int DefaultPort(bool isHttps) => isHttps ? 443 : 80;
        public static bool IsValidPort(int port) => port > 0 && port <= 65535;
    }
}
