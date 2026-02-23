using System;
using System.Collections.Generic;

namespace DesignPatterns
{
    public interface IFileSystemItem { int GetSize(); string Name { get; } }

    public class File : IFileSystemItem
    {
        public string Name { get; }
        private int size;
        public File(string name, int size) { Name = name; this.size = size; }
        public int GetSize() => size;
    }

    public class Folder : IFileSystemItem
    {
        public string Name { get; }
        private List<IFileSystemItem> children = new List<IFileSystemItem>();
        public Folder(string name) { Name = name; }
        public void Add(IFileSystemItem item) => children.Add(item);
        public int GetSize() { int total = 0; foreach (var c in children) total += c.GetSize(); return total; }
    }
}
