using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;
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
    [SerializeField] private float outputPanelAnimDuration = 0.4f;

    [Header("Output Panel - Banner (BgBanner > VictoryBanner / GameOverBanner)")]
    [SerializeField] private GameObject victoryBanner;
    [SerializeField] private GameObject gameOverBanner;

    [Header("Output Panel - Avatar (single Image; sprite swapped per outcome)")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Sprite[] victoryAvatarSprites;
    [SerializeField] private Sprite[] gameOverAvatarSprites;

    [Header("Output Panel - Texts")]
    [SerializeField] private TMP_Text outputLevelText;
    [SerializeField] private TMP_Text outputStatusText;
    [SerializeField] private string victoryStatusLabel = "Complete";
    [SerializeField] private string gameOverStatusLabel = "Failed";

    [Header("Output Panel - Score / Time")]
    [SerializeField] private TMP_Text outputScoreText;
    [SerializeField] private TMP_Text outputTimeText;

    [Header("Output Panel - Stars (victory only; earned count based on hearts remaining)")]
    [SerializeField] private GameObject starsContainer;
    [Tooltip("3 star GameObjects, in order. Earned stars are left active; the rest are deactivated.")]
    [SerializeField] private GameObject[] starObjects;

    [Header("Output Panel - Buttons")]
    [Tooltip("Victory only - loads the next level. Hidden on Game Over and when already on the last level.")]
    [SerializeField] private Button outputNextButton;
    [SerializeField] private Button outputRestartButton;
    [Tooltip("Returns to the Difficulty Selection Panel (see PromptDifficultySelection()).")]
    [SerializeField] private Button outputBackButton;

    [Header("In-Game Header Buttons")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button restartHeaderButton;

    [Header("Keypad Number Buttons (1 - 9)")]
    [SerializeField] private Button[] numberButtons;

    [Header("Keypad Visual Feedback")]
    [SerializeField] private Color notesModeButtonColor = new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField] private Color completedNumberButtonColor = new Color(0.5f, 0.5f, 0.5f, 1f);

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

    [Header("Random Ambient SFX (Gameplay)")]
    [Tooltip("While a game is active, 'RandomSFX' plays repeatedly at a random interval between these two values (seconds).")]
    [SerializeField] private float randomSfxMinInterval = 15f;
    [SerializeField] private float randomSfxMaxInterval = 30f;

    // ---- NEW: Level Progression ----
    [Header("Level Progression")]
    [Tooltip("Optional in-game header label, e.g. shows 'Level 35'.")]
    [SerializeField] private TMP_Text levelHeaderText;

    // ---- NEW: Live HUD Score & Timer (updates while playing, not just on the Output Panel) ----
    [Header("In-Game HUD - Live Score & Timer")]
    [SerializeField] private TMP_Text hudScoreText;
    [SerializeField] private TMP_Text hudTimeText;

    // ---- NEW: Score (shown on the Output Panel and live HUD) ----
    [Header("Score Rewards")]
    [SerializeField] private int scorePerCorrectNumber = 5;
    [SerializeField] private int scorePerRowComplete = 10;
    [SerializeField] private int scorePerColumnComplete = 10;
    [SerializeField] private int scorePerBoxComplete = 20;
    [SerializeField] private int scorePerBoardComplete = 200;

    // ---- NEW: Profile XP Rewards ----
    [Header("Profile XP Rewards (granted once, on board complete)")]
    [SerializeField] private int profileXPEasy = 20;
    [SerializeField] private int profileXPMedium = 40;
    [SerializeField] private int profileXPHard = 80;

    private Coroutine randomSfxCoroutine;

    private int[,] solutionGrid;
    private int[,] puzzleGrid;
    private bool isGameActive;
    private bool notesMode;
    private UIManager.Difficulty currentDifficulty;

    // Which Level (1-1000) of currentDifficulty is currently being played.
    private int currentLevel;
    public int CurrentLevel => currentLevel;

    private Sequence outputPanelSequence;

    // ---- NEW: Score & Timer, tracked per level attempt ----
    private int sessionScore;
    private float levelStartTime;
    private float levelElapsedSeconds;

    // Each keypad button's original tint, so Note Mode's color swap can be
    // reverted cleanly when it's turned off.
    private Color[] numberButtonDefaultColors;

    // Index i tracks whether digit (i + 1) has all 9 of its correct
    // placements filled on the board — used to gray out that keypad button.
    private bool[] numberCompleted;

    // Row/Column/3x3-Box completion score fires once each, per game — these
    // track which sections have already paid out so re-checking an already-
    // complete section (e.g. after a later move elsewhere) doesn't double-pay.
    private bool[] rowScoreAwarded;
    private bool[] colScoreAwarded;
    private bool[] boxScoreAwarded;

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

        // Which level to open: a level-select screen may have queued a
        // specific level via LevelManager.SetSelectedLevel(); otherwise we
        // continue at the first not-yet-completed level for this difficulty.
        int level = LevelManager.Instance != null
            ? LevelManager.Instance.ConsumeSelectedLevel(currentDifficulty)
            : 1;

        StartNewGame(currentDifficulty, level);
    }

    private void Update()
    {
        if (!isGameActive) return;

        if (hudTimeText != null)
            hudTimeText.text = $"Time:{FormatElapsedTime(Time.time - levelStartTime)}";

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
            numberCompleted = new bool[numberButtons.Length];

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

        if (outputNextButton != null) outputNextButton.onClick.AddListener(OnOutputNextClicked);
        if (outputRestartButton != null) outputRestartButton.onClick.AddListener(OnOutputRestartClicked);
        if (outputBackButton != null) outputBackButton.onClick.AddListener(OnOutputBackClicked);

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
        StopRandomSfxLoop();

        if (heartManager != null)
            heartManager.OnGameOver -= HandleGameOver;

        if (outputNextButton != null) outputNextButton.onClick.RemoveListener(OnOutputNextClicked);
        if (outputRestartButton != null) outputRestartButton.onClick.RemoveListener(OnOutputRestartClicked);
        if (outputBackButton != null) outputBackButton.onClick.RemoveListener(OnOutputBackClicked);
        if (restartConfirmYesButton != null) restartConfirmYesButton.onClick.RemoveListener(OnRestartConfirmYesClicked);
        if (restartConfirmNoButton != null) restartConfirmNoButton.onClick.RemoveListener(OnRestartConfirmNoClicked);
    }

    // Starts a game at whichever level the player should "Continue" on for
    // this difficulty. Used by the in-scene difficulty posters / restart
    // flow, where no specific level was chosen.
    public void StartNewGame(UIManager.Difficulty difficulty)
    {
        int level = LevelManager.Instance != null ? LevelManager.Instance.GetContinueLevel(difficulty) : 1;
        StartNewGame(difficulty, level);
    }

    public void StartNewGame(UIManager.Difficulty difficulty, int level)
    {
        currentDifficulty = difficulty;
        currentLevel = Mathf.Clamp(level, 1, LevelManager.MaxLevel);
        undoStack.Clear();
        ResetScoreAwardTracking();

        sessionScore = 0;
        levelStartTime = Time.time;
        levelElapsedSeconds = 0f;

        if (hudScoreText != null) hudScoreText.text = $"Score: {sessionScore}";
        if (hudTimeText != null) hudTimeText.text = $"Time:{FormatElapsedTime(0f)}";

        (puzzleGrid, solutionGrid) = SudokuGenerator.GeneratePuzzle(difficulty, currentLevel);

        if (levelHeaderText != null) levelHeaderText.text = $"Level {currentLevel}";

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
        UpdateCompletedNumbers();
        ResetNotesButtonScale();

        isGameActive = true;

        SoundManager.Instance?.PlayMusic(UIManager.GetDifficultyMusicId(difficulty));
        StartRandomSfxLoop();
    }

    private void ResetScoreAwardTracking()
    {
        rowScoreAwarded = new bool[9];
        colScoreAwarded = new bool[9];
        boxScoreAwarded = new bool[9];
    }

    private void StartRandomSfxLoop()
    {
        if (randomSfxCoroutine != null) StopCoroutine(randomSfxCoroutine);
        randomSfxCoroutine = StartCoroutine(RandomSfxLoop());
    }

    private void StopRandomSfxLoop()
    {
        if (randomSfxCoroutine != null)
        {
            StopCoroutine(randomSfxCoroutine);
            randomSfxCoroutine = null;
        }
    }

    private IEnumerator RandomSfxLoop()
    {
        while (isGameActive)
        {
            float wait = UnityEngine.Random.Range(randomSfxMinInterval, randomSfxMaxInterval);
            yield return new WaitForSeconds(wait);

            if (!isGameActive) yield break;
            SoundManager.Instance?.PlaySFX("RandomSFX");
        }
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
            SoundManager.Instance?.PlaySFX("Right");

            // Correct placements lock permanently — not pushed to the undo
            // stack (nothing to undo back to) and can't be erased.
            selected.LockAsCorrect(number);
            selected.GlowNumber();

            // ---- NEW: Score Rewards ----
            sessionScore += scorePerCorrectNumber;
            CheckSectionCompletion(row, col);
            UpdateScoreHud();

            UpdateCompletedNumbers();
            CheckWinCondition();
        }
        else
        {
            SoundManager.Instance?.PlaySFX("Wrong");

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

    // ---- NEW: Row / Column / 3x3 Box completion score ----
    // Called right after a correct placement (and after a hint fills a
    // cell). Awards each section's score bonus exactly once, the moment it
    // becomes fully correct.
    private void CheckSectionCompletion(int row, int col)
    {
        if (gridLayout == null || rowScoreAwarded == null) return;

        if (!rowScoreAwarded[row] && IsRowComplete(row))
        {
            rowScoreAwarded[row] = true;
            sessionScore += scorePerRowComplete;
        }

        if (!colScoreAwarded[col] && IsColumnComplete(col))
        {
            colScoreAwarded[col] = true;
            sessionScore += scorePerColumnComplete;
        }

        int boxIndex = (row / 3) * 3 + (col / 3);
        if (!boxScoreAwarded[boxIndex] && IsBoxComplete(boxIndex))
        {
            boxScoreAwarded[boxIndex] = true;
            sessionScore += scorePerBoxComplete;
        }
    }

    private bool IsRowComplete(int row)
    {
        for (int c = 0; c < 9; c++)
        {
            SudokuCell cell = gridLayout.Cells[row, c];
            if (cell == null || cell.GetNumber() != solutionGrid[row, c] || !cell.IsCorrect)
                return false;
        }
        return true;
    }

    private bool IsColumnComplete(int col)
    {
        for (int r = 0; r < 9; r++)
        {
            SudokuCell cell = gridLayout.Cells[r, col];
            if (cell == null || cell.GetNumber() != solutionGrid[r, col] || !cell.IsCorrect)
                return false;
        }
        return true;
    }

    private bool IsBoxComplete(int boxIndex)
    {
        int boxRowStart = (boxIndex / 3) * 3;
        int boxColStart = (boxIndex % 3) * 3;

        for (int r = boxRowStart; r < boxRowStart + 3; r++)
        {
            for (int c = boxColStart; c < boxColStart + 3; c++)
            {
                SudokuCell cell = gridLayout.Cells[r, c];
                if (cell == null || cell.GetNumber() != solutionGrid[r, c] || !cell.IsCorrect)
                    return false;
            }
        }
        return true;
    }

    // The color a button should show normally: gray if that digit is fully
    // placed on the board, otherwise its own default tint, or the Note
    // Mode tint while notes are on.
    private Color GetKeypadIdleColor(int index)
    {
        if (numberCompleted != null && index < numberCompleted.Length && numberCompleted[index])
            return completedNumberButtonColor;

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

    // Scans the board and grays out (+ disables) any keypad number that
    // has all 9 of its correct placements already on the board.
    private void UpdateCompletedNumbers()
    {
        if (numberButtons == null || gridLayout == null) return;

        if (numberCompleted == null || numberCompleted.Length != numberButtons.Length)
            numberCompleted = new bool[numberButtons.Length];

        int[] counts = new int[10]; // index 1-9 used, 0 unused

        for (int r = 0; r < 9; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                SudokuCell cell = gridLayout.Cells[r, c];
                if (cell == null) continue;

                int num = cell.GetNumber();
                if (num >= 1 && num <= 9 && cell.IsCorrect)
                {
                    counts[num]++;
                }
            }
        }

        for (int i = 0; i < numberButtons.Length; i++)
        {
            int digit = i + 1;
            bool isDone = digit <= 9 && counts[digit] >= 9;
            numberCompleted[i] = isDone;

            if (numberButtons[i] != null)
                numberButtons[i].interactable = !isDone;
        }

        UpdateKeypadColorsForNotesMode();
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
            SoundManager.Instance?.PlaySFX("Erase");
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

        SoundManager.Instance?.PlaySFX("Undo");

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

        SoundManager.Instance?.PlaySFX("Hint");

        int correctVal = solutionGrid[selected.Row, selected.Col];
        selected.SetFixedNumber(correctVal);

        // A hint doesn't pay the "correct number" score bonus (the player
        // didn't solve it themselves), but it can still complete a row/
        // column/box, so those bonuses still fire normally.
        CheckSectionCompletion(selected.Row, selected.Col);
        UpdateScoreHud();

        UpdateCompletedNumbers();
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
        levelElapsedSeconds = Time.time - levelStartTime;

        // ---- NEW: Board-complete score, Profile XP, Level unlock ----
        sessionScore += scorePerBoardComplete;
        UpdateScoreHud();
        ProfileManager.Instance?.AddXP(GetProfileXPForDifficulty(currentDifficulty));
        LevelManager.Instance?.CompleteLevel(currentDifficulty, currentLevel);
        ProfileManager.Instance?.RecordVictory(currentDifficulty, sessionScore);

        ShowOutputPanel(isVictory: true);
    }

    private int GetProfileXPForDifficulty(UIManager.Difficulty difficulty)
    {
        switch (difficulty)
        {
            case UIManager.Difficulty.Easy: return profileXPEasy;
            case UIManager.Difficulty.Medium: return profileXPMedium;
            case UIManager.Difficulty.Hard: return profileXPHard;
            default: return profileXPEasy;
        }
    }

    private void HandleGameOver()
    {
        // Guard against double-invocation: this can now fire both from the
        // direct check in OnNumberEntered() and from heartManager's
        // OnGameOver event, if something else is also subscribed to it.
        if (!isGameActive) return;

        isGameActive = false;
        levelElapsedSeconds = Time.time - levelStartTime;
        ProfileManager.Instance?.RecordLoss();
        ShowOutputPanel(isVictory: false);
    }

    private void ShowOutputPanel(bool isVictory)
    {
        if (outputPanel == null) return;

        StopRandomSfxLoop();
        SoundManager.Instance?.StopMusic();
        SoundManager.Instance?.PlaySFX(isVictory ? "Victory" : "GameOver");

        // Banner
        if (victoryBanner != null) victoryBanner.SetActive(isVictory);
        if (gameOverBanner != null) gameOverBanner.SetActive(!isVictory);

        // Avatar - one random sprite from the outcome-appropriate set
        ShowRandomAvatar(isVictory);

        // Texts
        string statusSource = isVictory ? victoryStatusLabel : gameOverStatusLabel;
        if (outputLevelText != null)
        {
            string levelSource = $"Level {currentLevel}";
            outputLevelText.text = LocalizationManager.Instance != null
                ? LocalizationManager.Instance.Translate(levelSource)
                : levelSource;
        }
        if (outputStatusText != null)
        {
            outputStatusText.text = LocalizationManager.Instance != null
                ? LocalizationManager.Instance.Translate(statusSource)
                : statusSource;
        }

        // Score / Time
        if (outputScoreText != null) outputScoreText.text = $"SCORE:{sessionScore}";
        if (outputTimeText != null) outputTimeText.text = $"TIME: {FormatElapsedTime(levelElapsedSeconds)}";

        // Stars - victory only, earned count based on hearts remaining
        if (starsContainer != null) starsContainer.SetActive(isVictory);
        if (isVictory) UpdateStars();

        // Buttons - Next only makes sense after a Victory, and only if
        // there's a next level to go to.
        bool hasNextLevel = currentLevel < LevelManager.MaxLevel;
        if (outputNextButton != null) outputNextButton.gameObject.SetActive(isVictory && hasNextLevel);

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

    // Loads the next level in sequence (Victory only - hidden otherwise,
    // see ShowOutputPanel). Completing Level N always leads to Level N+1.
    private void OnOutputNextClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        HideOutputPanel();
        StartNewGame(currentDifficulty, currentLevel + 1);
    }

    // Restarts the level currently shown on the Output Panel.
    private void OnOutputRestartClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        HideOutputPanel();
        StartNewGame(currentDifficulty, currentLevel);
    }

    // Returns to the Difficulty Selection Panel - reuses the same routing
    // as the header Restart button: an in-scene panel if one is assigned,
    // otherwise a flagged reload of MainMenu straight into its panel.
    private void OnOutputBackClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        HideOutputPanel();
        PromptDifficultySelection();
    }

    // Picks one sprite at random from whichever outcome's set applies and
    // assigns it to the single avatar Image.
    private void ShowRandomAvatar(bool isVictory)
    {
        if (avatarImage == null) return;

        Sprite[] sprites = isVictory ? victoryAvatarSprites : gameOverAvatarSprites;
        if (sprites == null || sprites.Length == 0) return;

        int chosen = UnityEngine.Random.Range(0, sprites.Length);
        if (sprites[chosen] != null) avatarImage.sprite = sprites[chosen];
    }

    // Star rating is based on hearts remaining at the moment of victory:
    // full 3 hearts (6 half-hearts) -> 3 stars, down to 1 star minimum on
    // any win. Earned stars stay active; the rest are deactivated.
    private void UpdateStars()
    {
        if (starObjects == null || starObjects.Length == 0) return;

        int halfHearts = heartManager != null ? heartManager.CurrentHalfHearts : HeartManager.MaxHalfHearts;
        int earned;
        if (halfHearts >= 6) earned = 3;
        else if (halfHearts >= 4) earned = 2;
        else earned = 1;

        for (int i = 0; i < starObjects.Length; i++)
        {
            if (starObjects[i] != null) starObjects[i].SetActive(i < earned);
        }
    }

    private string FormatElapsedTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int secs = totalSeconds % 60;

        return hours > 0
            ? $"{hours:00}:{minutes:00}:{secs:00}"
            : $"{minutes:00}:{secs:00}";
    }

    // Call any time sessionScore changes, to keep the live HUD in sync.
    private void UpdateScoreHud()
    {
        if (hudScoreText != null) hudScoreText.text = $"Score: {sessionScore}";
    }

    private void OnRestartHeaderClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");

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

        SoundManager.Instance?.PlaySFX("Button");

        if (restartConfirmationPanel != null)
            restartConfirmationPanel.SetActive(false);

        PromptDifficultySelection();
    }

    private void OnRestartConfirmNoClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");

        if (restartConfirmationPanel != null)
            restartConfirmationPanel.SetActive(false);
    }

    private void PromptDifficultySelection()
    {
        // Stop the game (and keyboard input processing in Update()) the
        // instant the difficulty panel opens, so the old board can't keep
        // reacting to input while the player is choosing a new difficulty.
        isGameActive = false;
        StopRandomSfxLoop();

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
        SoundManager.Instance?.PlaySFX(UIManager.GetDifficultyButtonSfxId(difficulty));

        PlayerPrefs.SetString(UIManager.DifficultyPrefKey, difficulty.ToString());
        PlayerPrefs.Save();
        StartNewGame(difficulty);
    }

    private void OnBackClicked()
    {
        StopRandomSfxLoop();
        PromptDifficultySelection();
    }
}