using System;
using System.Collections.Generic;

namespace DesignPatterns
{
    public interface ICommand { void Execute(); void Undo(); }

    public class AddCommand : ICommand
    {
        private List<int> list;
        private int value;
        public AddCommand(List<int> list, int value) { this.list = list; this.value = value; }
        public void Execute() => list.Add(value);
        public void Undo() => list.RemoveAt(list.Count - 1);
    }

    public class CommandHistory
    {
        private Stack<ICommand> history = new Stack<ICommand>();

        public void Execute(ICommand cmd) { cmd.Execute(); history.Push(cmd); }
        public void Undo() { if (history.Count > 0) history.Pop().Undo(); }
        public bool CanUndo => history.Count > 0;
    }
}
