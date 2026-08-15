using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using DG.Tweening;

public class SudokuGameManager : MonoBehaviour
{
    public static SudokuGameManager Instance { get; private set; }

    [Header("Core Components")]
    [SerializeField] private SudokuGridLayout gridLayout;
    [SerializeField] private HeartManager heartManager;

    [Header("Output Panel (Game Over / Victory)")]
    [SerializeField] private GameObject outputPanel;
    [SerializeField] private CanvasGroup outputPanelCanvasGroup;
    [SerializeField] private GameObject gameOverText;
    [SerializeField] private GameObject victoryText;
    [SerializeField] private Button outputRestartButton;
    [SerializeField] private float outputPanelAnimDuration = 0.4f;

    [Header("In-Game Header Buttons")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button restartHeaderButton;

    [Header("Keypad Number Buttons (1 - 9)")]
    [SerializeField] private Button[] numberButtons;

    [Header("Keypad Visual Feedback")]
    [SerializeField] private Color notesModeButtonColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Header("Notes Button Visual Feedback")]
    [SerializeField] private float notesButtonSelectedScale = 1.15f;
    [SerializeField] private float notesButtonScaleAnimDuration = 0.15f;

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

    [Header("Restart Confirmation Panel (Header Restart Button Only)")]
    [SerializeField] private GameObject restartConfirmationPanel;
    [SerializeField] private Button restartConfirmYesButton;
    [SerializeField] private Button restartConfirmNoButton;

    private int[,] solutionGrid;
    private int[,] puzzleGrid;
    private bool isGameActive;
    private bool notesMode;
    private UIManager.Difficulty currentDifficulty;

    private Sequence outputPanelSequence;

    // Each keypad button's original tint, so Note Mode's color swap can be
    // reverted cleanly when it's turned off.
    private Color[] numberButtonDefaultColors;

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

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return; // no keyboard device connected (e.g. mobile)

        // Process Keyboard inputs (new Input System)
        for (int i = 1; i <= 9; i++)
        {
            Key numberKey = Key.Digit1 + (i - 1);
            Key numpadKey = Key.Numpad1 + (i - 1);

            if (keyboard[numberKey].wasPressedThisFrame || keyboard[numpadKey].wasPressedThisFrame)
            {
                OnNumberEntered(i);
                break;
            }
        }

        if (keyboard[Key.Backspace].wasPressedThisFrame || keyboard[Key.Delete].wasPressedThisFrame)
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
            numberButtonDefaultColors = new Color[numberButtons.Length];

            for (int i = 0; i < numberButtons.Length; i++)
            {
                int num = i + 1;

                if (numberButtons[i] != null)
                {
                    if (numberButtons[i].image != null)
                        numberButtonDefaultColors[i] = numberButtons[i].image.color;

                    numberButtons[i].onClick.AddListener(() => OnNumberEntered(num));
                }
            }
        }

        // Action Buttons
        if (eraseButton != null) eraseButton.onClick.AddListener(OnEraseClicked);
        if (undoButton != null) undoButton.onClick.AddListener(OnUndoClicked);
        if (hintButton != null) hintButton.onClick.AddListener(OnHintClicked);
        if (notesButton != null) notesButton.onClick.AddListener(OnNotesToggleClicked);

        // Output Panel Restart Button
        if (outputPanel != null)
            outputPanel.SetActive(false);

        if (outputRestartButton != null) outputRestartButton.onClick.AddListener(OnOutputRestartClicked);

        // Heart Manager Event
        if (heartManager != null)
        {
            heartManager.OnGameOver += HandleGameOver;
        }

        // Difficulty selection buttons in GameScene (if present)
        if (easyPosterBtn != null) easyPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Easy));
        if (mediumPosterBtn != null) mediumPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Medium));
        if (hardPosterBtn != null) hardPosterBtn.onClick.AddListener(() => SelectDifficultyAndStart(UIManager.Difficulty.Hard));

        // Restart Confirmation Panel (header restart button only)
        if (restartConfirmationPanel != null)
            restartConfirmationPanel.SetActive(false);

        if (restartConfirmYesButton != null) restartConfirmYesButton.onClick.AddListener(OnRestartConfirmYesClicked);
        if (restartConfirmNoButton != null) restartConfirmNoButton.onClick.AddListener(OnRestartConfirmNoClicked);
    }

    private void OnDestroy()
    {
        outputPanelSequence?.Kill();

        if (heartManager != null)
            heartManager.OnGameOver -= HandleGameOver;

        if (outputRestartButton != null) outputRestartButton.onClick.RemoveListener(OnOutputRestartClicked);
        if (restartConfirmYesButton != null) restartConfirmYesButton.onClick.RemoveListener(OnRestartConfirmYesClicked);
        if (restartConfirmNoButton != null) restartConfirmNoButton.onClick.RemoveListener(OnRestartConfirmNoClicked);
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

        if (outputPanel != null)
        {
            outputPanelSequence?.Kill();
            outputPanel.SetActive(false);
        }

        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(false);
        }

        notesMode = false;
        UpdateKeypadColorsForNotesMode();
        ResetNotesButtonScale();

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
            selected.GlowNumber();
            CheckWinCondition();
        }
        else
        {
            // Only wrong entries are undo-able / erasable.
            undoStack.Push(new CellMove(row, col, selected.GetNumber(), selected.IsFixed, selected.IsCorrect));
            selected.SetUserNumber(number, false);
            selected.GlowNumber();

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

    // The color a button should show normally: its own default tint, or
    // the Note Mode tint while notes are on.
    private Color GetKeypadIdleColor(int index)
    {
        Color defaultColor = (numberButtonDefaultColors != null && index < numberButtonDefaultColors.Length)
            ? numberButtonDefaultColors[index]
            : Color.white;

        return notesMode ? notesModeButtonColor : defaultColor;
    }

    private void OnNotesToggleClicked()
    {
        notesMode = !notesMode;
        UpdateKeypadColorsForNotesMode();
        UpdateNotesButtonScale();
    }

    // Swaps every keypad button's tint to notesModeButtonColor while Note
    // Mode is active, reverting to each button's own original color when
    // it's turned off.
    private void UpdateKeypadColorsForNotesMode()
    {
        if (numberButtons == null) return;

        for (int i = 0; i < numberButtons.Length; i++)
        {
            Button btn = numberButtons[i];
            if (btn == null || btn.image == null) continue;

            btn.image.color = GetKeypadIdleColor(i);
        }
    }

    // The Note button itself scales up while Note Mode is active, and
    // shrinks back to normal when it's turned off.
    private void UpdateNotesButtonScale()
    {
        if (notesButton == null) return;

        notesButton.transform.DOKill();
        notesButton.transform.DOScale(notesMode ? notesButtonSelectedScale : 1f, notesButtonScaleAnimDuration)
            .SetEase(Ease.OutBack)
            .SetLink(notesButton.gameObject);
    }

    private void ResetNotesButtonScale()
    {
        if (notesButton == null) return;

        notesButton.transform.DOKill();
        notesButton.transform.localScale = Vector3.one;
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
        ShowOutputPanel(isVictory: true);
    }

    private void HandleGameOver()
    {
        // Guard against double-invocation: this can now fire both from the
        // direct check in OnNumberEntered() and from heartManager's
        // OnGameOver event, if something else is also subscribed to it.
        if (!isGameActive) return;

        isGameActive = false;
        ShowOutputPanel(isVictory: false);
    }

    private void ShowOutputPanel(bool isVictory)
    {
        if (outputPanel == null) return;

        if (gameOverText != null) gameOverText.SetActive(!isVictory);
        if (victoryText != null) victoryText.SetActive(isVictory);

        outputPanelSequence?.Kill();

        outputPanel.SetActive(true);
        outputPanel.transform.localScale = Vector3.one * 0.7f;

        if (outputPanelCanvasGroup != null) outputPanelCanvasGroup.alpha = 0f;

        outputPanelSequence = DOTween.Sequence()
            .Append(outputPanel.transform.DOScale(1f, outputPanelAnimDuration).SetEase(Ease.OutBack))
            .Join(outputPanelCanvasGroup != null ? outputPanelCanvasGroup.DOFade(1f, outputPanelAnimDuration) : null)
            .SetLink(outputPanel);
    }

    private void HideOutputPanel()
    {
        if (outputPanel == null) return;

        outputPanelSequence?.Kill();

        if (outputPanelCanvasGroup != null)
        {
            outputPanelSequence = DOTween.Sequence()
                .Append(outputPanelCanvasGroup.DOFade(0f, outputPanelAnimDuration))
                .Join(outputPanel.transform.DOScale(0.8f, outputPanelAnimDuration))
                .OnComplete(() => outputPanel.SetActive(false))
                .SetLink(outputPanel);
        }
        else
        {
            outputPanel.SetActive(false);
        }
    }

    private void OnOutputRestartClicked()
    {
        HideOutputPanel();
        PromptDifficultySelection();
    }

    private void OnRestartHeaderClicked()
    {
        if (restartConfirmationPanel != null)
        {
            restartConfirmationPanel.SetActive(true);
        }
        else
        {
            // No confirmation panel assigned, fall back to old behavior.
            PromptDifficultySelection();
        }
    }

    private void OnRestartConfirmYesClicked()
    {
        if (restartConfirmationPanel != null)
            restartConfirmationPanel.SetActive(false);

        PromptDifficultySelection();
    }

    private void OnRestartConfirmNoClicked()
    {
        if (restartConfirmationPanel != null)
            restartConfirmationPanel.SetActive(false);
    }

    private void PromptDifficultySelection()
    {
        // Stop the game (and keyboard input processing in Update()) the
        // instant the difficulty panel opens, so the old board can't keep
        // reacting to input while the player is choosing a new difficulty.
        isGameActive = false;

        if (gridLayout != null)
        {
            gridLayout.ClearSelection();
        }

        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(true);
        }
        else
        {
            // Return to Main Menu scene to select difficulty. Flag that the
            // MainMenu scene should skip straight to the difficulty panel
            // instead of booting into the main menu buttons.
            PlayerPrefs.SetInt(UIManager.OpenDifficultyOnLoadKey, 1);
            PlayerPrefs.Save();
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