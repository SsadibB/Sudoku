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
    [Header("References (assign in Inspector or via MultiplayerManager)")]
    [SerializeField] private MultiplayerResultPanel resultPanel;
    [SerializeField] private OpponentBoardPanel opponentBoardPanel;
    [SerializeField] private Button boardButton;

    // Injected at game start
    private SudokuGameManager gameManager;

    private bool gameStarted;
    private float gameStartTime;

    // ---- Spawner: NetworkSudokuPlayer prefab ----
    [Header("Prefabs")]
    [SerializeField] private NetworkObject networkPlayerPrefab;

    private void Start()
    {
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

        // Listen for opponent leaving or forfeiting
        MultiplayerManager.Instance.OnOpponentLeft += HandleOpponentLeft;
        NetworkSudokuPlayer.OnPlayerForfeited += HandlePlayerForfeited;

        gameStartTime = Time.time;
        gameStarted = true;

        StartCoroutine(InitNetworkPlayerAndBoard());
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnCellCorrect -= HandleCellCorrect;
            gameManager.OnCellChanged -= HandleCellChanged;
            gameManager.OnBoardComplete -= HandleBoardComplete;
        }

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnOpponentLeft -= HandleOpponentLeft;

        NetworkSudokuPlayer.OnPlayerForfeited -= HandlePlayerForfeited;
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
                gameStarted = false;
                float elapsed = Time.time - gameStartTime;
                ShowResult(isWinner: true, elapsed);
            }
            else if (remote.IsFinished)
            {
                gameStarted = false;
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
    }

    private void HandleCellChanged(int row, int col, int value, bool isCorrect)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.RecordCellChange(row, col, (byte)value, isCorrect);
    }

    private void HandleBoardComplete()
    {
        if (!gameStarted) return;
        gameStarted = false;

        float elapsed = Time.time - gameStartTime;
        NetworkSudokuPlayer.Local?.MarkFinished(elapsed);

        // Determine winner: whoever flags Finished first wins.
        bool iWon = NetworkSudokuPlayer.Remote == null || !NetworkSudokuPlayer.Remote.IsFinished;
        ShowResult(iWon, elapsed);
    }

    private void HandleOpponentLeft()
    {
        if (!gameStarted) return;
        gameStarted = false;

        // Opponent disconnected — local player wins by default
        float elapsed = Time.time - gameStartTime;
        ShowResult(isWinner: true, elapsed);
    }

    private void HandlePlayerForfeited(NetworkSudokuPlayer player)
    {
        if (!gameStarted) return;
        if (player != NetworkSudokuPlayer.Local)
        {
            gameStarted = false;
            float elapsed = Time.time - gameStartTime;
            ShowResult(isWinner: true, elapsed);
        }
    }

    private void ShowResult(bool isWinner, float elapsed)
    {
        if (resultPanel != null)
            resultPanel.Show(isWinner, elapsed);
    }

    // ---- Public API for OpponentBoardPanel button ----

    public void ToggleOpponentBoard()
    {
        if (opponentBoardPanel != null)
            opponentBoardPanel.Toggle();
    }
}
