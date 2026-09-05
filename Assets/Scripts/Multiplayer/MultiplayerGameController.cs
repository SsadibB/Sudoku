using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// Sits in GameScene. If a Fusion session is active, this script bridges
/// SudokuGameManager events → NetworkSudokuPlayer RPCs and watches for
/// winner conditions. In single-player sessions it does nothing.
/// </summary>
public class MultiplayerGameController : MonoBehaviour
{
    [Header("References (assign in Inspector or via MultiplayerManager)")]
    [SerializeField] private MultiplayerResultPanel resultPanel;
    [SerializeField] private OpponentBoardPanel opponentBoardPanel;
    [SerializeField] private Button boardButton;

    // Set by SudokuGameManager modifications — injected at game start
    private SudokuGameManager gameManager;

    private bool gameStarted;
    private float gameStartTime;

    // ---- Spawner: NetworkSudokuPlayer prefab ----
    [Header("Prefabs")]
    [SerializeField] private NetworkObject networkPlayerPrefab;

    private void Start()
    {
        if (boardButton != null)
            boardButton.onClick.AddListener(ToggleOpponentBoard);

        if (MultiplayerManager.Instance == null || !MultiplayerManager.Instance.IsInSession)
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
        gameManager.OnBoardComplete += HandleBoardComplete;

        // Listen for opponent leaving
        MultiplayerManager.Instance.OnOpponentLeft += HandleOpponentLeft;

        gameStartTime = Time.time;
        gameStarted = true;

        // Spawn the local NetworkSudokuPlayer — Fusion Shared mode auto-spawns
        // per player, but we need to ensure it has the puzzle snapshot once
        // SudokuGameManager finishes generating the puzzle.
        StartCoroutine(InitNetworkPlayerAfterFrame());
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnCellCorrect -= HandleCellCorrect;
            gameManager.OnBoardComplete -= HandleBoardComplete;
        }

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnOpponentLeft -= HandleOpponentLeft;
    }

    private IEnumerator InitNetworkPlayerAfterFrame()
    {
        // Wait until NetworkSudokuPlayer.Local is spawned by Fusion
        float waited = 0f;
        while (NetworkSudokuPlayer.Local == null && waited < 5f)
        {
            yield return null;
            waited += Time.deltaTime;
        }

        if (NetworkSudokuPlayer.Local == null)
        {
            Debug.LogWarning("MultiplayerGameController: Local NetworkSudokuPlayer never spawned.");
            yield break;
        }

        // Push the initial puzzle board into the network snapshot
        var puzzle = gameManager.GetCurrentPuzzle();
        NetworkSudokuPlayer.Local.InitBoardSnapshot(puzzle);

        // Register opponent board panel
        if (opponentBoardPanel != null)
            opponentBoardPanel.Initialize();
    }

    // ---- Event handlers ----

    private void HandleCellCorrect(int row, int col, int value)
    {
        if (!gameStarted) return;
        NetworkSudokuPlayer.Local?.RecordCorrectCell(row, col, (byte)value);
    }

    private void HandleBoardComplete()
    {
        if (!gameStarted) return;
        gameStarted = false;

        float elapsed = Time.time - gameStartTime;
        NetworkSudokuPlayer.Local?.MarkFinished(elapsed);

        // Determine winner: whoever is flagged Finished first wins.
        // Since we just set Local.IsFinished, check if Remote is already done.
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
