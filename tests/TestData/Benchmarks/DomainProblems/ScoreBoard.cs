using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainProblems
{
    public class ScoreBoard
    {
        private Dictionary<string, int> scores = new Dictionary<string, int>();

        public void AddScore(string player, int points) =>
            scores[player] = scores.GetValueOrDefault(player, 0) + points;

        public int GetScore(string player) => scores.GetValueOrDefault(player, 0);
        public bool IsHighScore(string player, int highScore) => GetScore(player) > highScore;

        public List<(string player, int score)> Leaderboard() =>
            scores.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
