using System;
namespace OOPGenerics
{
    public class Counter
    {
        private static int instanceCount = 0;
        private static int nextId = 0;
        public int Id { get; }
        public Counter() { Id = nextId++; instanceCount++; }
        public static int InstanceCount => instanceCount;
    }
}
