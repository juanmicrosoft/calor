using System;

namespace DataStructures
{
    public class TrieNode
    {
        public TrieNode?[] Children = new TrieNode?[26];
        public bool IsEndOfWord = false;
    }

    public class Trie
    {
        private TrieNode root = new TrieNode();

        public void Insert(string word)
        {
            var node = root;
            foreach (char c in word)
            {
                int idx = c - 'a';
                if (node.Children[idx] == null) node.Children[idx] = new TrieNode();
                node = node.Children[idx]!;
            }
            node.IsEndOfWord = true;
        }

        public bool Search(string word)
        {
            var node = root;
            foreach (char c in word)
            {
                int idx = c - 'a';
                if (node.Children[idx] == null) return false;
                node = node.Children[idx]!;
            }
            return node.IsEndOfWord;
        }

        public bool StartsWith(string prefix)
        {
            var node = root;
            foreach (char c in prefix)
            {
                int idx = c - 'a';
                if (node.Children[idx] == null) return false;
                node = node.Children[idx]!;
            }
            return true;
        }
    }
}
