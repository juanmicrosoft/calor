using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public class TodoList
    {
        private List<(string task, bool done)> tasks = new List<(string, bool)>();

        public void Add(string task) => tasks.Add((task, false));

        public void Complete(int index)
        {
            if (index < 0 || index >= tasks.Count)
                throw new ArgumentOutOfRangeException();
            tasks[index] = (tasks[index].task, true);
        }

        public int PendingCount() { int c = 0; foreach (var t in tasks) if (!t.done) c++; return c; }
        public bool AllDone() => PendingCount() == 0;
        public int Count => tasks.Count;
    }
}
