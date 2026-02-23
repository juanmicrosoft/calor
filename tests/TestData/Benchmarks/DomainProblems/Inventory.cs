using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public class Inventory
    {
        private Dictionary<string, int> stock = new Dictionary<string, int>();

        public void Restock(string item, int quantity)
        {
            if (quantity <= 0) throw new ArgumentException("Quantity must be positive");
            stock[item] = stock.GetValueOrDefault(item, 0) + quantity;
        }

        public void Sell(string item, int quantity)
        {
            if (!stock.ContainsKey(item) || stock[item] < quantity)
                throw new InvalidOperationException("Insufficient stock");
            stock[item] -= quantity;
        }

        public bool InStock(string item) => stock.ContainsKey(item) && stock[item] > 0;
        public bool NeedsReorder(string item, int threshold) => stock.GetValueOrDefault(item, 0) <= threshold;
    }
}
