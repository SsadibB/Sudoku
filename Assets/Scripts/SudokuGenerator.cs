using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SudokuGenerator
{
    public const int GridSize = 9;

    // No more shared unseeded Random - each GeneratePuzzle call builds its
    // own Random seeded from (difficulty, level), so the exact same level
    // always produces the exact same puzzle (needed for Restart to give you
    // back the identical board instead of a fresh random one).
    public static (int[,] puzzle, int[,] solution) GeneratePuzzle(UIManager.Difficulty difficulty, int level)
    {
        int seed = ((int)difficulty * 100000) + level;
        System.Random rand = new System.Random(seed);

        int[,] solution = new int[GridSize, GridSize];
        FillGrid(solution, rand);

        int[,] puzzle = (int[,])solution.Clone();

        int cluesToKeep;
        switch (difficulty)
        {
            case UIManager.Difficulty.Easy:
                cluesToKeep = 42;
                break;
            case UIManager.Difficulty.Medium:
                cluesToKeep = 34;
                break;
            case UIManager.Difficulty.Hard:
                cluesToKeep = 26;
                break;
            default:
                cluesToKeep = 40;
                break;
        }

        RemoveNumbers(puzzle, GridSize * GridSize - cluesToKeep, rand);

        return (puzzle, solution);
    }

    private static bool FillGrid(int[,] grid, System.Random rand)
    {
        for (int r = 0; r < GridSize; r++)
        {
            for (int c = 0; c < GridSize; c++)
            {
                if (grid[r, c] == 0)
                {
                    List<int> numbers = GetShuffledNumbers(rand);
                    foreach (int num in numbers)
                    {
                        if (IsValidPlacement(grid, r, c, num))
                        {
                            grid[r, c] = num;
                            if (FillGrid(grid, rand))
                                return true;
                            grid[r, c] = 0;
                        }
                    }
                    return false;
                }
            }
        }
        return true;
    }

    private static List<int> GetShuffledNumbers(System.Random rand)
    {
        List<int> numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        for (int i = numbers.Count - 1; i > 0; i--)
        {
            int k = rand.Next(i + 1);
            int value = numbers[k];
            numbers[k] = numbers[i];
            numbers[i] = value;
        }
        return numbers;
    }

    private static bool IsValidPlacement(int[,] grid, int row, int col, int num)
    {
        for (int i = 0; i < GridSize; i++)
        {
            if (grid[row, i] == num || grid[i, col] == num)
                return false;
        }

        int startRow = (row / 3) * 3;
        int startCol = (col / 3) * 3;
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                if (grid[startRow + r, startCol + c] == num)
                    return false;
            }
        }

        return true;
    }

    private static void RemoveNumbers(int[,] grid, int countToRemove, System.Random rand)
    {
        List<int> indices = new List<int>();
        for (int i = 0; i < GridSize * GridSize; i++)
        {
            indices.Add(i);
        }

        // Shuffle indices
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int k = rand.Next(i + 1);
            int temp = indices[k];
            indices[k] = indices[i];
            indices[i] = temp;
        }

        int removed = 0;
        foreach (int index in indices)
        {
            if (removed >= countToRemove) break;
            int r = index / GridSize;
            int c = index % GridSize;

            if (grid[r, c] != 0)
            {
                grid[r, c] = 0;
                removed++;
            }
        }
    }
}