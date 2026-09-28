using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

        if (boardButton == null)
            boardButton = FindOpponentBoardButton();

        if (boardButton != null)
        {
            boardButton.gameObject.SetActive(isMultiplayer);
            if (isMultiplayer)
                boardButton.onClick.AddListener(ToggleOpponentBoard);
        }

        BindMatchHud(isMultiplayer);

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
        float elapsed = gameManager != null
            ? gameManager.ElapsedSeconds
            : Mathf.Max(0f, Time.time - gameStartTime);

        if (playerSide != null)
        {
            if (gameStarted)
                NetworkSudokuPlayer.Local?.UpdateElapsed(elapsed);
            RefreshMatchHud(elapsed);
        }

        if (!gameStarted) return;

        // Watch if remote opponent finished the board or forfeited
        var remote = ResolveRemote();
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
                ShowResult(isWinner: false, elapsed);
            }
        }
    }

    private IEnumerator InitNetworkPlayerAndBoard()
    {
        float waitReady = 0f;
        while ((gameManager == null || !gameManager.GameplayReady) && waitReady < 8f)
        {
            gameManager = SudokuGameManager.Instance;
            waitReady += Time.deltaTime;
            yield return null;
        }

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

    private Button FindOpponentBoardButton()
    {
        var game = GameObject.Find("Game");
        if (game == null) return null;
        Transform board = game.transform.Find("ButtonsAndLevels/OpponentsBoard");
        if (board == null) return null;

        Button button = board.GetComponent<Button>();
        if (button == null) button = board.gameObject.AddComponent<Button>();
        if (button.targetGraphic == null)
            button.targetGraphic = board.GetComponent<Graphic>() ?? board.GetComponentInChildren<Graphic>(true);
        return button;
    }

    private Transform playerSide;
    private Transform opponentSide;

    private void BindMatchHud(bool isMultiplayer)
    {
        var game = GameObject.Find("Game");
        if (game == null) return;

        Transform single = game.transform.Find("SingleStatus");
        Transform multi = game.transform.Find("MultiplayerStatus");
        if (single != null) single.gameObject.SetActive(!isMultiplayer);
        if (multi != null) multi.gameObject.SetActive(isMultiplayer);
        if (!isMultiplayer || multi == null) return;

        playerSide = multi.Find("Contents/PlayerSide");
        opponentSide = multi.Find("Contents/OpponentSide");
        CacheOutputOpponentStats();
        RefreshMatchHud(0f);
    }

    private static NetworkSudokuPlayer ResolveRemote()
    {
        if (NetworkSudokuPlayer.Remote != null)
            return NetworkSudokuPlayer.Remote;

        var players = Object.FindObjectsByType<NetworkSudokuPlayer>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i] != NetworkSudokuPlayer.Local)
                return players[i];
        }
        return null;
    }

    private readonly System.Collections.Generic.List<Transform> outputOpponentStats = new System.Collections.Generic.List<Transform>();

    private void CacheOutputOpponentStats()
    {
        outputOpponentStats.Clear();
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].name != "OpponentStats") continue;
            if (!all[i].gameObject.scene.IsValid()) continue;
            outputOpponentStats.Add(all[i]);
        }
    }

    private void RefreshMatchHud(float elapsed)
    {
        if (playerSide == null) return;

        var local = NetworkSudokuPlayer.Local;
        var remote = ResolveRemote();
        var mp = MultiplayerManager.Instance;

        string localName = local != null ? local.PlayerName.ToString() : (mp != null ? mp.LocalPlayerName : "You");
        int localLevel = local != null && local.ProfileLevel > 0
            ? local.ProfileLevel
            : (ProfileManager.Instance != null ? ProfileManager.Instance.ProfileLevel : 1);
        int localLives = local != null
            ? local.HalfHearts / 2
            : (gameManager != null ? gameManager.CurrentHalfHearts / 2 : HeartManager.MaxLives);
        int localScore = gameManager != null ? gameManager.SessionScore : (local != null ? local.Score : 0);
        Sprite localAvatar = ProfileManager.Instance != null ? ProfileManager.Instance.CurrentAvatarSprite : null;

        ApplySide(playerSide, localName, localLevel, localScore, elapsed, localLives, localAvatar);

        string remoteName = remote != null && !string.IsNullOrEmpty(remote.PlayerName.ToString())
            ? remote.PlayerName.ToString()
            : (mp != null && !string.IsNullOrEmpty(mp.OpponentName) ? mp.OpponentName : "Opponent");
        int remoteLevel = remote != null && remote.ProfileLevel > 0
            ? remote.ProfileLevel
            : (mp != null ? mp.OpponentProfileLevel : 1);
        int remoteScore = remote != null ? remote.Score : 0;
        float remoteTime = remote != null ? remote.ElapsedTime : 0f;
        int remoteLives = remote != null ? Mathf.Max(0, remote.HalfHearts) / 2 : 0;
        Sprite remoteAvatar = null;
        if (remote != null && ProfileManager.Instance != null)
            remoteAvatar = ProfileManager.Instance.GetPresetAvatar(remote.AvatarIndex);
        if (remoteAvatar == null && mp != null)
            remoteAvatar = mp.GetOpponentAvatar();

        ApplySide(opponentSide, remoteName, remoteLevel, remoteScore, remoteTime, remoteLives, remoteAvatar);

        for (int i = 0; i < outputOpponentStats.Count; i++)
            ApplyOutputOpponent(outputOpponentStats[i], remoteScore, remoteTime, remoteLives);
    }

    private static void ApplyOutputOpponent(Transform stats, int score, float time, int lives)
    {
        if (stats == null) return;

        SetOutputValue(stats, "ScoreStats", score.ToString());
        SetOutputValue(stats, "TimeStats", FormatClock(time));
        SetOutputValue(stats, "LivesStats", Mathf.Max(0, lives).ToString());

        TMP_Text scoreText = FindText(stats, "ScoreValue_Text (TMP)");
        TMP_Text timeText = FindText(stats, "TimeValue_Text (TMP)");
        if (scoreText != null) scoreText.text = score.ToString();
        if (timeText != null) timeText.text = FormatClock(time);

        Transform livesRoot = FindChild(stats, "Lives");
        if (livesRoot == null) return;
        int shown = Mathf.Clamp(lives, 0, livesRoot.childCount);
        for (int i = 0; i < livesRoot.childCount; i++)
        {
            Transform slot = livesRoot.GetChild(i);
            Transform life = slot.Find("Life");
            Transform lifeless = slot.Find("Lifeless");
            bool alive = i < shown;
            if (life != null) life.gameObject.SetActive(alive);
            if (lifeless != null) lifeless.gameObject.SetActive(!alive);
        }
    }

    private static void SetOutputValue(Transform stats, string statName, string value)
    {
        Transform stat = FindChild(stats, statName);
        if (stat == null) return;
        TMP_Text[] texts = stat.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length == 0) return;
        TMP_Text valueText = texts[texts.Length - 1];
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].gameObject.name.IndexOf("Value", System.StringComparison.OrdinalIgnoreCase) >= 0)
                valueText = texts[i];
        }
        valueText.text = value;
    }

    private static void ApplySide(Transform side, string playerName, int level, int score, float time, int lives, Sprite avatar)
    {
        if (side == null) return;

        TMP_Text nameText = FindText(side, "Name_Text (TMP)");
        TMP_Text levelText = FindText(side, "Level_Text (TMP)");
        TMP_Text scoreText = FindText(side, "ScoreValue_Text (TMP)");
        TMP_Text timeText = FindText(side, "TimeValue_Text (TMP)");

        if (nameText != null) nameText.text = playerName;
        if (levelText != null) levelText.text = $"Level {Mathf.Max(1, level):00}";
        if (scoreText != null) scoreText.text = score.ToString();
        if (timeText != null) timeText.text = FormatClock(time);

        Transform avatarImage = FindChild(side, "Avatar");
        if (avatarImage != null && avatar != null)
        {
            var image = avatarImage.GetComponent<Image>();
            if (image != null) image.sprite = avatar;
        }

        Transform livesRoot = FindChild(side, "Lives");
        if (livesRoot == null) return;
        int shown = Mathf.Clamp(lives, 0, livesRoot.childCount);
        for (int i = 0; i < livesRoot.childCount; i++)
        {
            Transform slot = livesRoot.GetChild(i);
            Transform life = slot.Find("Life");
            Transform lifeless = slot.Find("Lifeless");
            bool alive = i < shown;
            if (life != null) life.gameObject.SetActive(alive);
            if (lifeless != null) lifeless.gameObject.SetActive(!alive);
        }
    }

    private static string FormatClock(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = total / 60;
        int secs = total % 60;
        return $"{minutes:00}:{secs:00}";
    }

    private static TMP_Text FindText(Transform root, string childName)
    {
        Transform t = FindChild(root, childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (root == null) return null;
        if (root.name == childName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChild(root.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }
}