namespace MultiDimArray
{
    public static class MultiDimExamples
    {
        public static int[,] CreateGrid(int rows, int cols)
        {
            int[,] grid = new int[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    grid[i, j] = i * cols + j;
                }
            }
            return grid;
        }

        public static int GetElement(int[,] grid, int row, int col)
        {
            return grid[row, col];
        }

        public static int[,] CreateInitialized()
        {
            int[,] matrix = new int[,]
            {
                { 1, 2, 3 },
                { 4, 5, 6 }
            };
            return matrix;
        }

        public static int GetLength(int[,] grid)
        {
            return grid.GetLength(0);
        }
    }
}
