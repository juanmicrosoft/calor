using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public class CartItem
    {
        public string ProductId { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public int Quantity { get; set; }

        public CartItem(string productId, string name, double price, int quantity)
        {
            ProductId = productId;
            Name = name;
            Price = price;
            Quantity = quantity;
        }
    }

    public class ShoppingCart
    {
        private List<CartItem> items = new List<CartItem>();

        public void AddItem(string productId, string name, double price, int quantity)
        {
            if (string.IsNullOrEmpty(productId))
                throw new ArgumentException("Product ID cannot be empty");
            if (price <= 0)
                throw new ArgumentException("Price must be positive");
            if (quantity <= 0)
                throw new ArgumentException("Quantity must be positive");

            var existingIndex = FindItemIndex(items, productId);
            if (existingIndex >= 0)
            {
                items[existingIndex].Quantity += quantity;
            }
            else
            {
                items.Add(new CartItem(productId, name, price, quantity));
            }
        }

        public bool RemoveItem(string productId)
        {
            var index = FindItemIndex(items, productId);
            if (index >= 0)
            {
                items.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool UpdateQuantity(string productId, int newQuantity)
        {
            if (newQuantity < 0)
                throw new ArgumentException("Quantity cannot be negative");

            var index = FindItemIndex(items, productId);
            if (index >= 0)
            {
                if (newQuantity == 0)
                    items.RemoveAt(index);
                else
                    items[index].Quantity = newQuantity;
                return true;
            }
            return false;
        }

        public double GetSubtotal()
        {
            double total = 0.0;
            for (int i = 0; i < items.Count; i++)
            {
                total += items[i].Price * items[i].Quantity;
            }
            return total;
        }

        public double GetTotalWithTax(double taxRate)
        {
            if (taxRate < 0 || taxRate > 1)
                throw new ArgumentException("Tax rate must be between 0 and 1");

            var subtotal = GetSubtotal();
            return subtotal + (subtotal * taxRate);
        }

        public int GetItemCount()
        {
            return items.Count;
        }

        public void Clear()
        {
            items.Clear();
        }

        private int FindItemIndex(List<CartItem> items, string productId)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ProductId == productId)
                    return i;
            }
            return -1;
        }
    }
}
