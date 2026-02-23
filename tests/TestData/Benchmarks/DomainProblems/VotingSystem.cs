using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public class VotingSystem
    {
        private Dictionary<string, int> votes = new Dictionary<string, int>();
        private int totalVotes = 0;

        public void Vote(string candidate)
        {
            votes[candidate] = votes.GetValueOrDefault(candidate, 0) + 1;
            totalVotes++;
        }

        public bool HasMajority(string candidate) =>
            votes.ContainsKey(candidate) && votes[candidate] * 2 > totalVotes;

        public double Percentage(string candidate) =>
            totalVotes > 0 ? (double)votes.GetValueOrDefault(candidate, 0) / totalVotes * 100 : 0;
    }
}
