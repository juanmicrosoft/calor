using System;
namespace OOPGenerics
{
    public class Wallet
    {
        private double balance;
        public Wallet(double initial) { balance = initial >= 0 ? initial : 0; }
        public double Balance => balance;
        public void Deposit(double amount) { if (amount > 0) balance += amount; }
        public bool Withdraw(double amount) { if (amount > 0 && amount <= balance) { balance -= amount; return true; } return false; }
    }
}
