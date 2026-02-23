using System;

namespace DesignPatterns
{
    public interface IPricing { double GetPrice(); }

    public class BasePrice : IPricing
    {
        private double price;
        public BasePrice(double price) { this.price = price; }
        public double GetPrice() => price;
    }

    public class TaxDecorator : IPricing
    {
        private IPricing inner;
        private double taxRate;
        public TaxDecorator(IPricing inner, double taxRate) { this.inner = inner; this.taxRate = taxRate; }
        public double GetPrice() => inner.GetPrice() * (1 + taxRate);
    }

    public class DiscountDecorator : IPricing
    {
        private IPricing inner;
        private double discount;
        public DiscountDecorator(IPricing inner, double discount) { this.inner = inner; this.discount = discount; }
        public double GetPrice() => inner.GetPrice() * (1 - discount);
    }
}
