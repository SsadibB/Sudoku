using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// Sits in GameScene. If a Fusion session is active, this script bridges
/// SudokuGameManager events → NetworkSudokuPlayer RPCs/properties, synchronizes
/// identical puzzle board generation across players, and watches for winner conditions.
/// In single-player sessions it disables itself and hides the board button.
/// </summary>
public class MultiplayerGameController : MonoBehaviour
{
    public static MultiplayerGameController Instance { get; private set; }

    [Header("References (assign in Inspector or via MultiplayerManager)")]
    [SerializeField] private MultiplayerResultPanel resultPanel;
    [SerializeField] private OpponentBoardPanel opponentBoardPanel;
    [SerializeField] private Button boardButton;

    // Injected at game start
    private SudokuGameManager gameManager;

    private bool gameStarted;
    private bool isOpponentLeftHandled;
    private float gameStartTime;

    // ---- Spawner: NetworkSudokuPlayer prefab ----
    [Header("Prefabs")]
    [SerializeField] private NetworkObject networkPlayerPrefab;

    private void Start()
    {
        Instance = this;

        bool isMultiplayer = MultiplayerManager.Instance != null && MultiplayerManager.Instance.IsInSession;

        if (boardButton != null)
        {
            boardButton.gameObject.SetActive(isMultiplayer);
            if (isMultiplayer)
                boardButton.onClick.AddListener(ToggleOpponentBoard);
        }

        if (opponentBoardPanel != null && boardButton != null)
        {
            opponentBoardPanel.SetBoardButton(boardButton.GetComponent<RectTransform>());
        }

        if (!isMultiplayer)
        {
            // Single-player — disable self
            enabled = false;
            return;
        }

        gameManager = SudokuGameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError("MultiplayerGameController: SudokuGameManager not found.");
            return;
        }

        // Listen for SudokuGameManager multiplayer events
        gameManager.OnCellCorrect += HandleCellCorrect;
        gameManager.OnCellChanged += HandleCellChanged;
        gameManager.OnBoardComplete += HandleBoardComplete;
        gameManager.OnHeartsChanged += HandleHeartsChanged;
        gameManager.OnScoreChanged += HandleScoreChanged;

        // Listen for opponent leaving or forfeiting
        MultiplayerManager.Instance.OnOpponentLeft += HandleOpponentLeft;
        NetworkSudokuPlayer.OnPlayerForfeited += HandlePlayerForfeited;

        gameStartTime = Time.time;
        gameStarted = true;

        StartCoroutine(InitNetworkPlayerAndBoard());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (gameManager != null)
        {
            gameManager.OnCellCorrect -= HandleCellCorrect;
            gameManager.OnCellChanged -= HandleCellChanged;
            gameManager.OnBoardComplete -= HandleBoardComplete;
            gameManager.OnHeartsChanged -= HandleHeartsChanged;
            gameManager.OnScoreChanged -= HandleScoreChanged;
        }

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnOpponentLeft -= HandleOpponentLeft;

        NetworkSudokuPlayer.OnPlayerForfeited -= HandlePlayerForfeited;
    }

    private void OnApplicationPause(bool paused)
    {
        // On Android, pressing the home button or switching apps pauses the app.
        // Forfeit and disconnect so the opponent immediately gets the win.
        if (paused && gameStarted)
        {
            gameStarted = false;
            NetworkSudokuPlayer.Local?.Forfeit();
            MultiplayerManager.Instance?.Disconnect();
        }
    }

    private void OnApplicationQuit()
    {
        if (gameStarted)
        {
            gameStarted = false;
            NetworkSudokuPlayer.Local?.Forfeit();
        }
    }

    private void Update()
    {
        if (!gameStarted) return;

        // Watch if remote opponent finished the board or forfeited
        var remote = NetworkSudokuPlayer.Remote;
        if (remote != null)
        {
            if (remote.HasForfeited)
            {
                // Route through the same guard as HandleOpponentLeft
                HandleOpponentLeft();
            }
            else if (remote.IsFinished)
            {
                gameStarted = false;
                gameManager?.EndGameForMultiplayer();
                float elapsed = Time.time - gameStartTime;
                ShowResult(isWinner: false, elapsed);
            }
        }
    }

    private IEnumerator InitNetworkPlayerAndBoard()
    {
        var mp = MultiplayerManager.Instance;
        var runner = mp != null ? mp.Runner : null;
        bool isMaster = runner != null && runner.IsSharedModeMasterClient;
        int matchLevel = mp != null ? mp.MatchLevel : 1;
        var diff = mp != null ? mp.MatchDifficulty : UIManager.Difficulty.Easy;

        // Spawn NetworkSudokuPlayer for the local player if not already spawned
        if (NetworkSudokuPlayer.Local == null && networkPlayerPrefab != null && runner != null && runner.IsRunning)
        {
            try
            {
                runner.SpawnAsync(networkPlayerPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MultiplayerGameController] Spawn local player error: {ex.Message}");
            }
        }

        // Wait briefly for NetworkSudokuPlayer.Local
        float waited = 0f;
        while (NetworkSudokuPlayer.Local == null && waited < 3f)
        {
            yield return null;
            waited += Time.deltaTime;
        }

        if (!isMaster)
        {
            // Guest player: wait briefly for remote master player's SharedPuzzleLevel
            float waitRemote = 0f;
            while ((NetworkSudokuPlayer.Remote == null || NetworkSudokuPlayer.Remote.SharedPuzzleLevel <= 0) && waitRemote < 2f)
            {
                yield return null;
                waitRemote += Time.deltaTime;
            }

            if (NetworkSudokuPlayer.Remote != null && NetworkSudokuPlayer.Remote.SharedPuzzleLevel > 0)
            {
                matchLevel = NetworkSudokuPlayer.Remote.SharedPuzzleLevel;
                mp?.SetMatchLevel(matchLevel);
            }
        }
        else
        {
            if (NetworkSudokuPlayer.Local != null)
            {
                NetworkSudokuPlayer.Local.SharedPuzzleLevel = matchLevel;
            }
        }

        // Ensure board matches the synchronized level
        if (gameManager.CurrentLevel != matchLevel)
        {
            gameManager.StartMultiplayerGame(diff, matchLevel);
            yield return null;
        }

        // Push the initial puzzle board into the local network snapshot
        if (NetworkSudokuPlayer.Local != null)
        {
            var puzzle = gameManager.GetCurrentPuzzle();
            if (puzzle != null)
                NetworkSudokuPlayer.Local.InitBoardSnapshot(puzzle);

            // Push the starting score and hearts so the opponent's board panel
            // shows accurate values from the very first frame.
            NetworkSudokuPlayer.Local.UpdateScore(gameManager.SessionScore);
            NetworkSudokuPlayer.Local.UpdateHalfHearts(gameManager.CurrentHalfHearts);
        }

        // Register and initialize opponent board panel
        if (opponentBoardPanel != null)
        {
            if (boardButton != null)
                opponentBoardPanel.SetBoardButton(boardButton.GetComponent<RectTransform>());
            opponentBoardPanel.Initialize();
        }
    }

    // ---- Event handlers ----

    private void HandleCellCorrect(int row, int col, int value)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.RecordCorrectCell(row, col, (byte)value);

        // Correct placements (and any row/column/box bonuses they trigger)
        // are already reflected in gameManager.SessionScore by this point.
        NetworkSudokuPlayer.Local?.UpdateScore(gameManager.SessionScore);
    }

    private void HandleCellChanged(int row, int col, int value, bool isCorrect)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.RecordCellChange(row, col, (byte)value, isCorrect);
    }

    private void HandleHeartsChanged(int halfHearts)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.UpdateHalfHearts(halfHearts);
    }

    private void HandleScoreChanged(int score)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.UpdateScore(score);
    }

    private void HandleBoardComplete()
    {
        if (!gameStarted) return;
        gameStarted = false;

        gameManager?.EndGameForMultiplayer();

        float elapsed = Time.time - gameStartTime;

        // The board-complete score bonus is already added to gameManager.SessionScore
        // before OnBoardComplete fires — push the final total.
        NetworkSudokuPlayer.Local?.UpdateScore(gameManager.SessionScore);
        NetworkSudokuPlayer.Local?.MarkFinished(elapsed);

        // Determine winner: whoever flags Finished first wins.
        bool iWon = NetworkSudokuPlayer.Remote == null || !NetworkSudokuPlayer.Remote.IsFinished;
        ShowResult(iWon, elapsed);
    }

    private void HandleOpponentLeft()
    {
        // Guard: only fire once (OnOpponentLeft and HasForfeited can both trigger on disconnect)
        if (isOpponentLeftHandled) return;
        isOpponentLeftHandled = true;
        gameStarted = false;

        gameManager?.EndGameForMultiplayer();

        // Opponent disconnected/forfeited — local player wins by default
        float elapsed = Time.time - gameStartTime;
        ShowResult(isWinner: true, elapsed);
    }

    private void HandlePlayerForfeited(NetworkSudokuPlayer player)
    {
        // Only react to the remote (opponent) forfeiting, not the local player's own forfeit.
        if (player == NetworkSudokuPlayer.Local) return;

        // Route through the same guard as HandleOpponentLeft to avoid double-results.
        HandleOpponentLeft();
    }

    private void ShowResult(bool isWinner, float elapsed)
    {
        gameManager?.EndGameForMultiplayer();

        if (opponentBoardPanel != null)
            opponentBoardPanel.Hide();

        if (resultPanel != null)
            resultPanel.Show(isWinner, elapsed);
    }

    // ---- Public API for OpponentBoardPanel button ----

    public void ToggleOpponentBoard()
    {
        if (opponentBoardPanel != null)
            opponentBoardPanel.Toggle();
    }

    /// <summary>
    /// Called when the local player deliberately leaves the game (back button confirmed).
    /// Sends the forfeit RPC to the opponent (they will see the Win panel via
    /// HandleOpponentLeft / HandlePlayerForfeited), then immediately shows the Lose
    /// result panel to the leaving player so they get proper feedback before the
    /// "Return to Menu" button disconnects and navigates away.
    /// </summary>
    public void ShowLocalPlayerForfeit()
    {
        if (isOpponentLeftHandled) return;
        isOpponentLeftHandled = true;
        gameStarted = false;

        gameManager?.EndGameForMultiplayer();

        // Notify the opponent over the network.
        NetworkSudokuPlayer.Local?.Forfeit();

        // Show lose result to the leaving player.
        // Do NOT call Disconnect() here — MultiplayerResultPanel.OnReturnToMenu
        // already calls Disconnect() and loads MainMenu when the player taps the button.
        float elapsed = Time.time - gameStartTime;
        ShowResult(isWinner: false, elapsed);
    }
}