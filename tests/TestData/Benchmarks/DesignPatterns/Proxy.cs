using System;

namespace DesignPatterns
{
    public interface IService { string GetData(int userId); }

    public class RealService : IService
    {
        public string GetData(int userId) => $"Data for user {userId}";
    }

    public class ProxyService : IService
    {
        private RealService real = new RealService();
        private int requiredRole;

        public ProxyService(int requiredRole) { this.requiredRole = requiredRole; }

        public string GetData(int userId)
        {
            if (userId < requiredRole) throw new UnauthorizedAccessException("Access denied");
            Console.WriteLine($"Access granted for user {userId}");
            return real.GetData(userId);
        }
    }
}
