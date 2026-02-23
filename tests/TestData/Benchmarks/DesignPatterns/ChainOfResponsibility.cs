using System;

namespace DesignPatterns
{
    public abstract class Approver
    {
        protected Approver? next;
        public void SetNext(Approver approver) { next = approver; }

        public abstract bool Approve(double amount);
    }

    public class Manager : Approver
    {
        public override bool Approve(double amount)
        {
            if (amount <= 1000) return true;
            return next?.Approve(amount) ?? false;
        }
    }

    public class Director : Approver
    {
        public override bool Approve(double amount)
        {
            if (amount <= 10000) return true;
            return next?.Approve(amount) ?? false;
        }
    }

    public class VP : Approver
    {
        public override bool Approve(double amount) => amount <= 100000;
    }
}
