using System;
using System.Text;

namespace DesignPatterns
{
    /// <summary>
    /// Visitor interface for expressions.
    /// </summary>
    public interface IExpressionVisitor<T>
    {
        T Visit(NumberExpression expr);
        T Visit(AddExpression expr);
        T Visit(MultiplyExpression expr);
    }

    /// <summary>
    /// Abstract base class for expressions in an expression tree.
    /// </summary>
    public abstract class Expression
    {
        /// <summary>
        /// Accept a visitor and return the result.
        /// </summary>
        public abstract T Accept<T>(IExpressionVisitor<T> visitor);
    }

    /// <summary>
    /// Represents a numeric literal.
    /// </summary>
    public class NumberExpression : Expression
    {
        public double Value { get; }

        public NumberExpression(double value)
        {
            Value = value;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    /// <summary>
    /// Represents addition of two expressions.
    /// </summary>
    public class AddExpression : Expression
    {
        public Expression Left { get; }
        public Expression Right { get; }

        public AddExpression(Expression left, Expression right)
        {
            Left = left;
            Right = right;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    /// <summary>
    /// Represents multiplication of two expressions.
    /// </summary>
    public class MultiplyExpression : Expression
    {
        public Expression Left { get; }
        public Expression Right { get; }

        public MultiplyExpression(Expression left, Expression right)
        {
            Left = left;
            Right = right;
        }

        public override T Accept<T>(IExpressionVisitor<T> visitor)
        {
            return visitor.Visit(this);
        }
    }

    /// <summary>
    /// Visitor that evaluates expressions to a numeric result.
    /// </summary>
    public class EvaluatorVisitor : IExpressionVisitor<double>
    {
        public double Visit(NumberExpression expr)
        {
            return expr.Value;
        }

        public double Visit(AddExpression expr)
        {
            return expr.Left.Accept(this) + expr.Right.Accept(this);
        }

        public double Visit(MultiplyExpression expr)
        {
            return expr.Left.Accept(this) * expr.Right.Accept(this);
        }
    }

    /// <summary>
    /// Visitor that prints expressions as strings.
    /// </summary>
    public class PrinterVisitor : IExpressionVisitor<string>
    {
        public string Visit(NumberExpression expr)
        {
            return expr.Value.ToString();
        }

        public string Visit(AddExpression expr)
        {
            return $"({expr.Left.Accept(this)} + {expr.Right.Accept(this)})";
        }

        public string Visit(MultiplyExpression expr)
        {
            return $"({expr.Left.Accept(this)} * {expr.Right.Accept(this)})";
        }
    }

    /// <summary>
    /// Helper class to demonstrate the Visitor pattern.
    /// </summary>
    public static class VisitorDemo
    {
        /// <summary>
        /// Evaluate an expression.
        /// </summary>
        public static double Evaluate(Expression expr)
        {
            var visitor = new EvaluatorVisitor();
            return expr.Accept(visitor);
        }

        /// <summary>
        /// Print an expression as a string.
        /// </summary>
        public static string Print(Expression expr)
        {
            var visitor = new PrinterVisitor();
            return expr.Accept(visitor);
        }

        /// <summary>
        /// Build a sample expression: (2 + 3) * 4
        /// </summary>
        public static Expression BuildSampleExpression()
        {
            return new MultiplyExpression(
                new AddExpression(
                    new NumberExpression(2),
                    new NumberExpression(3)
                ),
                new NumberExpression(4)
            );
        }
    }
}
