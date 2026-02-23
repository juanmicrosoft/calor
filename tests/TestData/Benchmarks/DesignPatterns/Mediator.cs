using System;
using System.Collections.Generic;

namespace DesignPatterns
{
    public class ChatRoom
    {
        private Dictionary<int, List<string>> messages = new Dictionary<int, List<string>>();

        public void Register(int userId)
        {
            if (!messages.ContainsKey(userId)) messages[userId] = new List<string>();
        }

        public void Send(int fromId, int toId, string message)
        {
            if (fromId == toId) return;
            if (!messages.ContainsKey(toId))
                throw new ArgumentException("Recipient not registered");
            messages[toId].Add($"From {fromId}: {message}");
        }

        public List<string> GetMessages(int userId) =>
            messages.ContainsKey(userId) ? messages[userId] : new List<string>();
    }
}
