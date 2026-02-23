using System;
using System.Collections.Generic;

namespace DesignPatterns
{
    public interface IObserver { void Update(string message); }

    public class EventBus
    {
        private List<IObserver> subscribers = new List<IObserver>();

        public void Subscribe(IObserver observer) => subscribers.Add(observer);
        public void Unsubscribe(IObserver observer) => subscribers.Remove(observer);

        public void Notify(string message)
        {
            foreach (var sub in subscribers) sub.Update(message);
        }
    }

    public class Logger : IObserver
    {
        public string LastMessage { get; private set; } = "";
        public void Update(string message) { LastMessage = message; }
    }
}
