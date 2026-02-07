using System;

namespace DataStructures
{
    public class BinaryTree
    {
        private class TreeNode
        {
            public int Value { get; set; }
            public TreeNode Left { get; set; }
            public TreeNode Right { get; set; }

            public TreeNode(int value)
            {
                Value = value;
                Left = null;
                Right = null;
            }
        }

        private TreeNode root = null;

        public void Insert(int value)
        {
            if (root == null)
            {
                root = new TreeNode(value);
            }
            else
            {
                InsertRecursive(root, value);
            }
        }

        private void InsertRecursive(TreeNode node, int value)
        {
            if (value < node.Value)
            {
                if (node.Left == null)
                    node.Left = new TreeNode(value);
                else
                    InsertRecursive(node.Left, value);
            }
            else
            {
                if (node.Right == null)
                    node.Right = new TreeNode(value);
                else
                    InsertRecursive(node.Right, value);
            }
        }

        public bool Contains(int value)
        {
            if (root == null)
                return false;
            return ContainsRecursive(root, value);
        }

        private bool ContainsRecursive(TreeNode node, int value)
        {
            if (value == node.Value)
                return true;

            if (value < node.Value)
            {
                if (node.Left == null)
                    return false;
                return ContainsRecursive(node.Left, value);
            }
            else
            {
                if (node.Right == null)
                    return false;
                return ContainsRecursive(node.Right, value);
            }
        }

        public int FindMin()
        {
            if (root == null)
                throw new InvalidOperationException("Tree is empty");

            var current = root;
            while (current.Left != null)
                current = current.Left;
            return current.Value;
        }

        public int FindMax()
        {
            if (root == null)
                throw new InvalidOperationException("Tree is empty");

            var current = root;
            while (current.Right != null)
                current = current.Right;
            return current.Value;
        }
    }
}
