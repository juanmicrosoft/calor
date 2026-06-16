using System;

namespace NamedConfig
{
    public static class NamedConfigModule
    {
        public static int Build(int port, string host)
        {
            return port;
        }

        public static int Run()
        {
            return Build(port: 8080, host: "localhost");
        }
    }
}
