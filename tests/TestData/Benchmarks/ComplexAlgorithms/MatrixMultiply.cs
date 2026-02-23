using System;

namespace ComplexAlgorithms
{
    public static class MatrixMultiply
    {
        public static int[,] Multiply(int[,] a, int[,] b)
        {
            int aRows = a.GetLength(0), aCols = a.GetLength(1);
            int bRows = b.GetLength(0), bCols = b.GetLength(1);
            if (aCols != bRows)
                throw new ArgumentException("Incompatible dimensions");
            var result = new int[aRows, bCols];
            for (int i = 0; i < aRows; i++)
                for (int j = 0; j < bCols; j++)
                    for (int k = 0; k < aCols; k++)
                        result[i, j] += a[i, k] * b[k, j];
            return result;
        }
    }
}
