#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class RewardedAdHintTests
{
    [MenuItem("Tools/Sudoku/Run Hint System Tests")]
    public static string RunTests()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== STARTING REWARDED AD HINT SYSTEM TESTS ===");

        // Ensure GameScene is active
        if (!Application.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameScene")
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        var gm = Object.FindAnyObjectByType<SudokuGameManager>();
        if (gm == null)
        {
            return "FAILED: SudokuGameManager not found in scene.";
        }

        // Initialize board
        gm.StartNewGame(UIManager.Difficulty.Easy, 1);

        // Get private fields and methods via reflection
        var gmType = typeof(SudokuGameManager);
        var registerListenersMethod = gmType.GetMethod("RegisterListeners", BindingFlags.NonPublic | BindingFlags.Instance);
        registerListenersMethod?.Invoke(gm, null);

        var gridLayoutField = gmType.GetField("gridLayout", BindingFlags.NonPublic | BindingFlags.Instance);
        var hintBtnField = gmType.GetField("hintButton", BindingFlags.NonPublic | BindingFlags.Instance);
        var isHintProcessingField = gmType.GetField("isHintProcessing", BindingFlags.NonPublic | BindingFlags.Instance);
        var solutionGridField = gmType.GetField("solutionGrid", BindingFlags.NonPublic | BindingFlags.Instance);

        var gridLayout = (SudokuGridLayout)gridLayoutField.GetValue(gm);
        var hintBtn = (Button)hintBtnField.GetValue(gm);
        var solutionGrid = (int[,])solutionGridField.GetValue(gm);

        RewardedAdManager.EnsureInstance();
        var ram = RewardedAdManager.Instance;
        ram.UseEditorSimulation = true;

        // Helper to find an empty cell
        SudokuCell FindEmptyCell(int skip = 0)
        {
            int count = 0;
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    var cell = gridLayout.Cells[r, c];
                    if (cell.GetNumber() == 0 && !cell.IsFixed)
                    {
                        if (count == skip) return cell;
                        count++;
                    }
                }
            }
            return null;
        }

        // ----------------------------------------------------
        // CASE 1: Normal Rewarded Ad flow (complete & reward)
        // ----------------------------------------------------
        var cell1 = FindEmptyCell(0);
        int r1 = cell1.Row;
        int c1 = cell1.Col;
        int expectedSolution1 = solutionGrid[r1, c1];

        gridLayout.SelectCell(r1, c1);
        ram.SimulateAdAvailable = true;
        ram.SimulateRewardEarned = true;

        hintBtn.onClick.Invoke();

        if (cell1.GetNumber() == expectedSolution1 && cell1.IsFixed && cell1.IsCorrect)
        {
            sb.AppendLine($"[PASS] Case 1 (Normal Rewarded Ad): Cell ({r1}, {c1}) revealed correct number {expectedSolution1}.");
        }
        else
        {
            sb.AppendLine($"[FAIL] Case 1: Expected {expectedSolution1}, got {cell1.GetNumber()}. IsFixed: {cell1.IsFixed}");
        }

        // ----------------------------------------------------
        // CASE 2: Player closes ad early (no reward)
        // ----------------------------------------------------
        var cell2 = FindEmptyCell(0);
        int r2 = cell2.Row;
        int c2 = cell2.Col;

        gridLayout.SelectCell(r2, c2);
        ram.SimulateAdAvailable = true;
        ram.SimulateRewardEarned = false; // Closed early!

        hintBtn.onClick.Invoke();

        if (cell2.GetNumber() == 0 && !cell2.IsFixed)
        {
            sb.AppendLine($"[PASS] Case 2 (Ad Closed Early): Cell ({r2}, {c2}) remained empty (no reward granted).");
        }
        else
        {
            sb.AppendLine($"[FAIL] Case 2: Expected empty cell, but got {cell2.GetNumber()}.");
        }

        // ----------------------------------------------------
        // CASE 3: Ad unavailable
        // ----------------------------------------------------
        var cell3 = FindEmptyCell(0);
        int r3 = cell3.Row;
        int c3 = cell3.Col;

        gridLayout.SelectCell(r3, c3);
        ram.SimulateAdAvailable = false; // Not available!

        hintBtn.onClick.Invoke();

        bool isProcessing = (bool)isHintProcessingField.GetValue(gm);
        if (cell3.GetNumber() == 0 && !isProcessing && hintBtn.interactable)
        {
            sb.AppendLine($"[PASS] Case 3 (Ad Unavailable): Cell unchanged, Hint button interactable is restored, toast shown.");
        }
        else
        {
            sb.AppendLine($"[FAIL] Case 3: Cell: {cell3.GetNumber()}, isProcessing: {isProcessing}, interactable: {hintBtn.interactable}");
        }

        // ----------------------------------------------------
        // CASE 4: Multiple clicks prevention
        // ----------------------------------------------------
        var cell4 = FindEmptyCell(0);
        gridLayout.SelectCell(cell4.Row, cell4.Col);
        ram.SimulateAdAvailable = true;
        ram.SimulateRewardEarned = true;

        // Force processing flag true to simulate clicking while in progress
        isHintProcessingField.SetValue(gm, true);
        hintBtn.interactable = false;

        // Click again
        hintBtn.onClick.Invoke();

        // Check that cell4 is still 0 because isHintProcessing blocked it
        if (cell4.GetNumber() == 0)
        {
            sb.AppendLine($"[PASS] Case 4 (Multiple Clicks): Secondary clicks ignored while hint is processing.");
        }
        else
        {
            sb.AppendLine($"[FAIL] Case 4: Secondary click was not blocked!");
        }

        // Reset processing flag
        isHintProcessingField.SetValue(gm, false);
        hintBtn.interactable = true;

        // ----------------------------------------------------
        // CASE 5: Cell selection during ad (preserve original cell)
        // ----------------------------------------------------
        var cellA = FindEmptyCell(0);
        var cellB = FindEmptyCell(1);
        int rA = cellA.Row;
        int cA = cellA.Col;
        int rB = cellB.Row;
        int cB = cellB.Col;
        int expectedSolutionA = solutionGrid[rA, cA];

        // 1. Select Cell A
        gridLayout.SelectCell(rA, cA);

        ram.SimulateAdAvailable = true;
        ram.SimulateRewardEarned = true;
        ram.OnAdShowingMiddleCallback = () =>
        {
            // Simulate user clicking Cell B while ad is showing
            gridLayout.SelectCell(rB, cB);
        };

        // Click hint
        hintBtn.onClick.Invoke();

        ram.OnAdShowingMiddleCallback = null;

        if (cellA.GetNumber() == expectedSolutionA && cellB.GetNumber() == 0)
        {
            sb.AppendLine($"[PASS] Case 5 (Selection Changed During Ad): Cell A ({rA}, {cA}) received hint, Cell B ({rB}, {cB}) remained unchanged.");
        }
        else
        {
            sb.AppendLine($"[FAIL] Case 5: Cell A has {cellA.GetNumber()} (expected {expectedSolutionA}), Cell B has {cellB.GetNumber()} (expected 0).");
        }

        // Revert editor simulation to false for default behavior
        ram.UseEditorSimulation = false;

        sb.AppendLine("=== ALL TESTS COMPLETED ===");
        Debug.Log(sb.ToString());
        return sb.ToString();
    }
}
#endif
