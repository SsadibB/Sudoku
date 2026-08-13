using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class SudokuGameManager : MonoBehaviour
{
    public static SudokuGameManager Instance { get; private set; }

    [Header("Core Components")]
    [SerializeField] private SudokuGridLayout gridLayout;
    [SerializeField] private HeartManager heartManager;
    [SerializeField] private SudokuGameOverPanel gameOverPanel;

    [Header("In-Game Header Buttons")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button restartHeaderButton;

    [Header("Keypad Number Buttons (1 - 9)")]
    [SerializeField] private Button[] numberButtons;

    [Header("Action Controls")]
    [SerializeField] private Button undoButton;
    [SerializeField] private Button eraseButton;
    [SerializeField] private Button notesButton;
    [SerializeField] private Button hintButton;

    [Header("Difficulty Modal / Selection Panel in GameScene (Optional)")]
    [SerializeField] private GameObject difficultySelectionPanel;
    [SerializeField] private Button easyPosterBtn;
    [SerializeField] private Button mediumPosterBtn;
    [SerializeField] private Button hardPosterBtn;

    private int[,] solutionGrid;
    private int[,] puzzleGrid;
    private bool isGameActive;
    private bool notesMode;
    private UIManager.Difficulty currentDifficulty;

    private struct CellMove
    {
        public int Row;
        public int Col;
        public int PreviousNumber;
        public bool PreviousWasFixed;
        public bool PreviousWasCorrect;

        public CellMove(int r, int c, int num, bool fixedNum, bool correctNum)
        {
            Row = r;
            Col = c;
            PreviousNumber = num;
            PreviousWasFixed = fixedNum;
            PreviousWasCorrect = correctNum;
        }
    }

    private readonly Stack<CellMove> undoStack = new Stack<CellMove>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        RegisterListeners();

        // Load difficulty set from Main Menu, default to Easy
        string prefDiff = PlayerPrefs.GetString(UIManager.DifficultyPrefKey, "Easy");
        if (Enum.TryParse(prefDiff, out UIManager.Difficulty diff))
        {
            currentDifficulty = diff;
        }
        else
        {
            currentDifficulty = UIManager.Difficulty.Easy;
        }

        StartNewGame(currentDifficulty);
    }

    private void Update()
    {
        if (!isGameActive) return;

        // Process Keyboard inputs
        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
            {
                OnNumberEntered(i);
                break;
            }
        }

        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            OnEraseClicked();
        }
    }

    private void RegisterListeners()
    {
        // Header Buttons
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        if (restartHeaderButton != null) restartHeaderButton.onClick.AddListener(OnRestartHeaderClicked);

        // Keypad Buttons
        if (numberButtons != null)
        {
            for (int i = 0; i < numberButtons.Length; i++)
            {
                int num = i + 1;
                if (numberButtons[i] != null)
                    numberButtons[i].onClick.AddListener(() => OnNumberEntered(num));
            }
        }

        // Action Buttons
        if (eraseButton != null) eraseButton.onClick.AddListener(OnEraseClicked);
        if (undoButton != null) undoButton.onClick.AddListener(OnUndoClicked);
        if (hintButton != null) hintButton.onClick.AddListener(OnHintClicked);
        if (notesButton != null) notesButton.onClick.AddListener(OnNotesToggleClicked);

        // GameOver Panel Event
        if (gameOverPanel != null)
        {
            gameOverPanel.OnRestartClicked += OnGameOverRestartRequested;
        }

        // Heart Manager Event
        if (heartManager != null)
        {
            heartManager.OnGameOver += HandleGameOver;
        }

        // Difficulty selection buttons in GameScene (if present)
        if (easyPosterBtn != null) easyPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Easy));
        if (mediumPosterBtn != null) mediumPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Medium));
        if (hardPosterBtn != null) hardPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Hard));
    }

    private void OnDestroy()
    {
        if (gameOverPanel != null)
            gameOverPanel.OnRestartClicked -= OnGameOverRestartRequested;

        if (heartManager != null)
            heartManager.OnGameOver -= HandleGameOver;
    }

    public void StartNewGame(UIManager.Difficulty difficulty)
    {
        currentDifficulty = difficulty;
        undoStack.Clear();

        (puzzleGrid, solutionGrid) = SudokuGenerator.GeneratePuzzle(difficulty);

        if (gridLayout != null)
        {
            gridLayout.PopulateBoard(puzzleGrid);
            gridLayout.ClearSelection();
        }

        if (heartManager != null)
        {
            heartManager.ResetHearts();
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.HidePanel();
        }

        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(false);
        }

        isGameActive = true;
    }

    private void OnNumberEntered(int number)
    {
        if (!isGameActive || gridLayout == null) return;

        SudokuCell selected = gridLayout.SelectedCell;
        if (selected == null || selected.IsFixed) return;

        // Notes mode: toggle a pencil-mark instead of placing the real
        // number. Doesn't touch the undo stack or hearts.
        if (notesMode)
        {
            selected.ToggleNote(number);
            return;
        }

        int row = selected.Row;
        int col = selected.Col;

        bool isCorrect = (number == solutionGrid[row, col]);

        if (isCorrect)
        {
            // Correct placements lock permanently — not pushed to the undo
            // stack (nothing to undo back to) and can't be erased.
            selected.LockAsCorrect(number);
            CheckWinCondition();
        }
        else
        {
            // Only wrong entries are undo-able / erasable.
            undoStack.Push(new CellMove(row, col, selected.GetNumber(), selected.IsFixed, selected.IsCorrect));
            selected.SetUserNumber(number, false);

            // Wrong number entry -> Deduct half heart!
            if (heartManager != null)
            {
                bool justRanOut = heartManager.DeductHalfHeart();
                if (justRanOut)
                {
                    // Trigger game over directly off the deduction result,
                    // rather than relying solely on the OnGameOver event —
                    // guarantees the game ends the instant the 6th half-heart
                    // (3rd full life) is lost even if the event isn't wired
                    // to anything else.
                    HandleGameOver();
                }
            }
        }
    }

    private void OnNotesToggleClicked()
    {
        notesMode = !notesMode;
    }

    private void OnEraseClicked()
    {
        if (!isGameActive || gridLayout == null) return;

        SudokuCell selected = gridLayout.SelectedCell;
        if (selected == null || selected.IsFixed) return;

        if (selected.GetNumber() != 0)
        {
            undoStack.Push(new CellMove(selected.Row, selected.Col, selected.GetNumber(), selected.IsFixed, selected.IsCorrect));
            selected.ClearCell();
        }
    }

    private void OnUndoClicked()
    {
        if (!isGameActive || undoStack.Count == 0 || gridLayout == null) return;

        CellMove lastMove = undoStack.Pop();
        SudokuCell cell = gridLayout.Cells[lastMove.Row, lastMove.Col];
        if (cell == null || cell.IsFixed) return;

        if (lastMove.PreviousNumber == 0)
        {
            cell.ClearCell();
        }
        else
        {
            cell.SetUserNumber(lastMove.PreviousNumber, lastMove.PreviousWasCorrect);
        }
    }

    private void OnHintClicked()
    {
        if (!isGameActive || gridLayout == null) return;

        SudokuCell selected = gridLayout.SelectedCell;
        if (selected == null || selected.IsFixed) return;

        int correctVal = solutionGrid[selected.Row, selected.Col];
        selected.SetFixedNumber(correctVal);
        CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        if (gridLayout == null || solutionGrid == null) return;

        for (int r = 0; r < 9; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                SudokuCell cell = gridLayout.Cells[r, c];
                if (cell == null || cell.GetNumber() != solutionGrid[r, c] || !cell.IsCorrect)
                {
                    return; // Puzzle not fully/correctly solved yet
                }
            }
        }

        // Victory!
        isGameActive = false;
        if (gameOverPanel != null)
        {
            gameOverPanel.ShowWin();
        }
    }

    private void HandleGameOver()
    {
        // Guard against double-invocation: this can now fire both from the
        // direct check in OnNumberEntered() and from heartManager's
        // OnGameOver event, if something else is also subscribed to it.
        if (!isGameActive) return;

        isGameActive = false;
        if (gameOverPanel != null)
        {
            gameOverPanel.ShowGameOver();
        }
    }

    private void OnRestartHeaderClicked()
    {
        PromptDifficultySelection();
    }

    private void OnGameOverRestartRequested()
    {
        PromptDifficultySelection();
    }

    private void PromptDifficultySelection()
    {
        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(true);
        }
        else
        {
            // Return to Main Menu scene to select difficulty
            SceneManager.LoadScene("MainMenu");
        }
    }

    private void SelectDifficultyAndStart(UIManager.Difficulty difficulty)
    {
        PlayerPrefs.SetString(UIManager.DifficultyPrefKey, difficulty.ToString());
        PlayerPrefs.Save();
        StartNewGame(difficulty);
    }

    private void OnBackClicked()
    {
        SceneManager.LoadScene("MainMenu");
    }
}