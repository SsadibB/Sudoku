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
    private float _nextSpawnAttempt;
    private bool _matchInitDone;
    private Coroutine _awayWait;
    private NetworkSudokuPlayer _boundLocal;
    private bool _resultShown;
    private int _remoteHeartsSeen;

    public bool IsMatchRunning => gameStarted;
    public float MatchStartTime => gameStartTime;

    // ---- Spawner: NetworkSudokuPlayer prefab ----
    [Header("Prefabs")]
    [SerializeField] private NetworkObject networkPlayerPrefab;

    private void Start()
    {
        Instance = this;

        bool isMultiplayer = MultiplayerManager.Instance != null && MultiplayerManager.Instance.IsMultiplayerGame;

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

        var fresh = MultiplayerManager.Instance;
        fresh?.ResetMatchProgress();
        NetworkSudokuPlayer.Local?.ResetForNewMatch(fresh != null ? fresh.MatchLevel : 0);

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
        MultiplayerManager.Instance.OnOpponentOutOfLives += HandleOpponentOutOfLives;
        NetworkSudokuPlayer.OnPlayerForfeited += HandlePlayerForfeited;

        gameStartTime = Time.time;
        gameStarted = true;
        MultiplayerManager.Instance?.KeepSessionAliveInBackground();
        MultiplayerManager.Instance?.EnsureSharedClock();

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
        {
            MultiplayerManager.Instance.OnOpponentLeft -= HandleOpponentLeft;
            MultiplayerManager.Instance.OnOpponentOutOfLives -= HandleOpponentOutOfLives;
        }

        NetworkSudokuPlayer.OnPlayerForfeited -= HandlePlayerForfeited;
    }

    private void OnApplicationPause(bool pause)
    {
        if (!gameStarted) return;
        MultiplayerManager.Instance?.KeepSessionAliveInBackground();
        if (!pause)
            FlushLocalClock();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!gameStarted || !hasFocus) return;
        FlushLocalClock();
    }

    private void FlushLocalClock()
    {
        var mp = MultiplayerManager.Instance;
        mp?.EnsureSharedClock();
        float elapsed = mp != null && mp.HasSharedClock
            ? mp.SharedElapsed
            : (gameManager != null ? gameManager.ElapsedSeconds : 0f);
        NetworkSudokuPlayer.Local?.FlushElapsed(elapsed);
    }

    private void OnApplicationQuit()
    {
        if (!gameStarted) return;
        gameStarted = false;
        NetworkSudokuPlayer.Local?.Forfeit();
        MultiplayerManager.Instance?.Disconnect();
    }

    private void Update()
    {
        if (MultiplayerManager.Instance != null && MultiplayerManager.Instance.OpponentForfeited)
        {
            ConcludeOpponentGone();
            return;
        }

        if (!gameStarted) return;
        if (_matchInitDone)
            EnsureLocalPlayer();

        var mp = MultiplayerManager.Instance;
        if (mp == null || !mp.MatchClockArmed)
            return;

        if (mp.OpponentOutOfLives)
        {
            HandleOpponentOutOfLives(mp.RemoteDisplayTime);
            return;
        }

        var remote = ResolveRemote();
        if (remote == null || remote.Object == null || !remote.Object.IsValid) return;

        bool forfeited = false;
        bool finished = false;
        int remoteHearts = -1;
        try
        {
            forfeited = remote.HasForfeited;
            // FinishTime stays 0 when the flag is a stale read during reconnect.
            finished = remote.IsFinished && remote.FinishTime > 0.5f;
            remoteHearts = remote.HalfHearts;
        }
        catch
        {
            return;
        }

        if (remoteHearts > 0)
            _remoteHeartsSeen = remoteHearts;

        bool clockFrozen = false;
        try { clockFrozen = remote.ClockFrozen; }
        catch { clockFrozen = false; }

        bool livesGone = remoteHearts == 0 && _remoteHeartsSeen > 0 && clockFrozen;
        if (livesGone)
        {
            float endTime = CurrentElapsed();
            try
            {
                if (remote.ClockFrozen)
                    endTime = remote.FrozenElapsed;
            }
            catch
            {
                // The shared match time is used when the frozen time is not readable yet.
            }
            if (mp != null && mp.OpponentClockFrozen)
                endTime = mp.RemoteDisplayTime;
            HandleOpponentOutOfLives(endTime);
            return;
        }

        if (forfeited)
            ConcludeOpponentGone();
        else if (finished)
        {
            gameStarted = false;
            ShowResult(isWinner: false, CurrentElapsed());
        }
    }

    private void LateUpdate()
    {
        var mp = MultiplayerManager.Instance;
        bool clockRunning = mp != null && mp.MatchClockArmed;
        if (gameStarted && clockRunning)
        {
            mp.EnsureSharedClock();
            mp.PullAuthoritativeClock();
        }

        float elapsed = clockRunning && mp.HasSharedClock
            ? mp.SharedElapsed
            : (mp != null && mp.IsInSession
                ? 0f
                : (gameManager != null ? gameManager.ElapsedSeconds : Mathf.Max(0f, Time.time - gameStartTime)));

        if (gameStarted && clockRunning)
            NetworkSudokuPlayer.Local?.UpdateElapsed(elapsed);

        if (mp != null && mp.ResultCaptured)
            return;

        RefreshMatchHud(mp != null && mp.HasSharedClock ? mp.LocalDisplayTime : elapsed);
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
        if (NetworkSudokuPlayer.Local != null)
            NetworkSudokuPlayer.Local.ResetForNewMatch(matchLevel);
        else if (networkPlayerPrefab != null && runner != null && runner.IsRunning)
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
        PushLocalSnapshot();

        // Register and initialize opponent board panel
        if (opponentBoardPanel != null)
        {
            if (boardButton != null)
                opponentBoardPanel.SetBoardButton(boardButton.GetComponent<RectTransform>());
            opponentBoardPanel.Initialize();
        }

        _matchInitDone = true;
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

        float elapsed = gameManager != null ? gameManager.ElapsedSeconds : Mathf.Max(0f, Time.time - gameStartTime);

        NetworkSudokuPlayer.Local?.UpdateScore(gameManager != null ? gameManager.SessionScore : 0);
        NetworkSudokuPlayer.Local?.UpdateElapsed(elapsed);
        NetworkSudokuPlayer.Local?.MarkFinished(elapsed);

        // Determine winner: whoever flags Finished first wins.
        bool iWon = NetworkSudokuPlayer.Remote == null || !NetworkSudokuPlayer.Remote.IsFinished;
        ShowResult(iWon, elapsed);
    }

    private void HandleOpponentLeft()
    {
        if (isOpponentLeftHandled) return;

        var remote = ResolveRemote();
        bool forfeited = false;
        if (remote != null && remote.Object != null && remote.Object.IsValid)
        {
            try { forfeited = remote.HasForfeited; }
            catch { forfeited = false; }
        }
        if (forfeited)
        {
            ConcludeOpponentGone();
            return;
        }

        // Screen-off and a dropped socket are not a finished game.
        // Wait for them to return, or for an explicit forfeit.
        if (_awayWait == null)
            _awayWait = StartCoroutine(WaitForAwayOpponent());
    }

    private IEnumerator WaitForAwayOpponent()
    {
        // A dropped connection without a screen-off notice can still be a
        // brief network blip. A known screen-off is allowed to last much longer.
        float goneDeadline = Time.realtimeSinceStartup + 180f;
        float awayDeadline = Time.realtimeSinceStartup + 600f;
        while (Time.realtimeSinceStartup < awayDeadline)
        {
            if (isOpponentLeftHandled)
            {
                _awayWait = null;
                yield break;
            }

            var remote = ResolveRemote();
            var mp = MultiplayerManager.Instance;
            bool theyAreAway = mp != null && mp.OpponentBackgrounded;
            bool theyAreHere = remote != null && remote.Object != null && remote.Object.IsValid;
            if (theyAreHere)
            {
                bool stillForfeited = false;
                try { stillForfeited = remote.HasForfeited; }
                catch { stillForfeited = false; }
                if (stillForfeited)
                    break;

                if (!theyAreAway)
                {
                    _awayWait = null;
                    yield break;
                }
            }

            if (theyAreAway)
                goneDeadline = Time.realtimeSinceStartup + 180f;
            else if (!theyAreHere && Time.realtimeSinceStartup >= goneDeadline)
                break;

            yield return null;
        }

        _awayWait = null;
        if (!isOpponentLeftHandled)
            ConcludeOpponentGone();
    }

    private void ConcludeOpponentGone()
    {
        if (isOpponentLeftHandled) return;
        isOpponentLeftHandled = true;
        gameStarted = false;
        if (_awayWait != null)
        {
            StopCoroutine(_awayWait);
            _awayWait = null;
        }

        ShowResult(isWinner: true, CurrentElapsed());
    }

    private void HandlePlayerForfeited(NetworkSudokuPlayer player)
    {
        if (player == NetworkSudokuPlayer.Local) return;
        ConcludeOpponentGone();
    }

    private float CurrentElapsed()
    {
        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.HasSharedClock)
            return mp.SharedElapsed;
        if (gameManager != null)
            return gameManager.ElapsedSeconds;
        return Mathf.Max(0f, Time.time - gameStartTime);
    }

    /// <summary>
    /// Local lives reached 0. This player loses and the opponent wins.
    /// Both clocks freeze at the same match time.
    /// </summary>
    public void EndMatchOnLocalLives()
    {
        if (_resultShown) return;
        gameStarted = false;

        var mp = MultiplayerManager.Instance;
        float elapsed = CurrentElapsed();
        mp?.FreezeMatchClocks(elapsed);
        mp?.NoteLocalOutOfLives();

        int score = gameManager != null ? gameManager.SessionScore : 0;
        NetworkSudokuPlayer.Local?.UpdateScore(score);
        NetworkSudokuPlayer.Local?.UpdateHalfHearts(0);
        NetworkSudokuPlayer.Local?.FreezeClock(elapsed);

        ShowResult(isWinner: false, elapsed);
    }

    private void HandleOpponentOutOfLives(float elapsed)
    {
        if (_resultShown || !gameStarted) return;
        gameStarted = false;

        var mp = MultiplayerManager.Instance;
        float endTime = Mathf.Max(0f, elapsed);
        mp?.FreezeMatchClocks(endTime);
        if (mp != null && !mp.OpponentOutOfLives)
            mp.NoteOpponentOutOfLives(-1);
        NetworkSudokuPlayer.Local?.FreezeClock(endTime);

        ShowResult(isWinner: true, endTime);
    }

    private void ShowResult(bool isWinner, float elapsed)
    {
        if (_resultShown) return;
        _resultShown = true;
        gameStarted = false;

        if (gameManager != null)
            gameManager.RecordCompletedMatch(isWinner);
        else if (isWinner)
        {
            UIManager.Difficulty difficulty = MultiplayerManager.Instance != null
                ? MultiplayerManager.Instance.MatchDifficulty
                : UIManager.Difficulty.Easy;
            ProfileManager.Instance?.RecordVictory(difficulty, 0);
        }
        else
            ProfileManager.Instance?.RecordLoss();

        var mp = MultiplayerManager.Instance;
        mp?.CaptureResultSnapshot();

        if (gameManager != null && gameManager.IsGameActive)
            gameManager.EndGameForMultiplayer();

        float shown = mp != null && mp.ResultCaptured
            ? mp.LocalDisplayTime
            : (gameManager != null ? gameManager.ElapsedSeconds : Mathf.Max(0f, elapsed));
        NetworkSudokuPlayer.Local?.FreezeClock(shown);

        if (opponentBoardPanel != null)
            opponentBoardPanel.Hide();

        if (resultPanel != null)
            resultPanel.Show(isWinner, shown);

        // Write the snapshot into the output stats once. Later frames do not refresh them.
        RefreshMatchHud(shown);
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
        if (isOpponentLeftHandled || _resultShown) return;
        isOpponentLeftHandled = true;
        gameStarted = false;

        // Snapshot both players before the clock is allowed to move again.
        MultiplayerManager.Instance?.CaptureResultSnapshot();
        gameManager?.EndGameForMultiplayer();

        // Notify the opponent over the network.
        NetworkSudokuPlayer.Local?.Forfeit();

        // Show lose result to the leaving player.
        // Do NOT call Disconnect() here — MultiplayerResultPanel.OnReturnToMenu
        // already calls Disconnect() and loads MainMenu when the player taps the button.
        ShowResult(isWinner: false, CurrentElapsed());
    }

    private void EnsureLocalPlayer()
    {
        var local = NetworkSudokuPlayer.Local;
        if (local != null)
        {
            if (_boundLocal != local)
            {
                _boundLocal = local;
                PushLocalSnapshot();
            }
            return;
        }

        if (Time.unscaledTime < _nextSpawnAttempt) return;
        _nextSpawnAttempt = Time.unscaledTime + 1f;

        var runner = MultiplayerManager.Instance != null ? MultiplayerManager.Instance.Runner : null;
        if (networkPlayerPrefab == null || runner == null || !runner.IsRunning) return;

        try
        {
            runner.SpawnAsync(networkPlayerPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[MultiplayerGameController] Spawn local player error: {ex.Message}");
        }
    }

    private void PushLocalSnapshot()
    {
        var local = NetworkSudokuPlayer.Local;
        if (local == null || gameManager == null) return;

        if (!gameManager.CopyLiveBoard(local))
        {
            var puzzle = gameManager.GetCurrentPuzzle();
            if (puzzle != null)
                local.InitBoardSnapshot(puzzle);
        }

        local.UpdateScore(gameManager.SessionScore);
        local.UpdateHalfHearts(gameManager.CurrentHalfHearts);
        local.UpdateElapsed(gameManager.ElapsedSeconds);
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

    private static bool RemoteClockStopped(NetworkSudokuPlayer remote)
    {
        if (remote == null || remote.Object == null || !remote.Object.IsValid) return false;
        try
        {
            return remote.HasForfeited || (remote.IsFinished && remote.FinishTime > 0.5f);
        }
        catch
        {
            return false;
        }
    }

    private static float StoppedRemoteTime(NetworkSudokuPlayer remote, float matchTime)
    {
        try
        {
            if (remote != null && remote.FinishTime > 0.5f)
                return remote.FinishTime;
        }
        catch
        {
            // Keep the shared match time if the finish time is not readable yet.
        }
        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.OpponentClockFrozen)
            return mp.RemoteDisplayTime;
        return matchTime;
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
        if (outputOpponentStats.Count == 0)
            CacheOutputOpponentStats();

        var local = NetworkSudokuPlayer.Local;
        var remote = ResolveRemote();
        var mp = MultiplayerManager.Instance;

        string localName = mp != null && IsRealHudName(mp.LocalPlayerName)
            ? ProfileManager.ShortDisplayName(mp.LocalPlayerName)
            : ReadNetworkName(local, "You");
        if (string.IsNullOrEmpty(localName)) localName = "You";
        int localLevel = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileLevel : 1;
        bool frozen = mp != null && mp.ResultCaptured;
        int localLives = frozen
            ? mp.SnapshotLocalLives
            : (local != null
                ? local.HalfHearts / 2
                : (gameManager != null ? gameManager.CurrentHalfHearts / 2 : HeartManager.MaxLives));
        int localScore = frozen
            ? mp.SnapshotLocalScore
            : (gameManager != null ? gameManager.SessionScore : (local != null ? local.Score : 0));
        if (frozen)
            elapsed = mp.LocalDisplayTime;
        else if (mp != null && mp.IsInSession && !mp.MatchClockArmed)
            elapsed = 0f;
        else if (mp != null && mp.HasSharedClock)
            elapsed = mp.SharedElapsed;
        Sprite localAvatar = ProfileManager.Instance != null ? ProfileManager.Instance.CurrentAvatarSprite : null;

        if (playerSide != null)
            ApplySide(playerSide, localName, localLevel, localScore, elapsed, localLives, localAvatar, false);

        string networkName = ReadNetworkName(remote, null);
        if (mp != null)
            mp.RememberOpponent(networkName, 0);
        string remoteSource = mp != null && IsRealHudName(mp.OpponentName)
            ? mp.OpponentName
            : networkName;
        string remoteName = ProfileManager.ShortDisplayName(remoteSource);
        int remoteLevel = mp != null ? mp.OpponentProfileLevel : 1;
        int remoteScore = frozen
            ? mp.SnapshotRemoteScore
            : (remote != null ? remote.Score : (mp != null ? mp.OpponentLiveScore : 0));
        float remoteTime = elapsed;
        if (frozen)
            remoteTime = mp.RemoteDisplayTime;
        else if (RemoteClockStopped(remote))
            remoteTime = StoppedRemoteTime(remote, elapsed);
        int remoteLives = frozen
            ? mp.SnapshotRemoteLives
            : Mathf.Max(0, remote != null ? remote.HalfHearts : (mp != null ? mp.OpponentLiveHalfHearts : 0)) / 2;
        Sprite remoteAvatar = mp != null ? mp.GetOpponentAvatar() : null;
        if (remoteAvatar == null && remote != null && ProfileManager.Instance != null
            && string.IsNullOrEmpty(mp != null ? mp.OpponentAvatarUrl : null)
            && remote.AvatarIndex >= 0)
            remoteAvatar = ProfileManager.Instance.GetPresetAvatar(remote.AvatarIndex);

        if (opponentSide != null)
            ApplySide(opponentSide, remoteName, remoteLevel, remoteScore, remoteTime, remoteLives, remoteAvatar, true);

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

    private static void ApplySide(Transform side, string playerName, int level, int score, float time, int lives, Sprite avatar, bool opponentSlot)
    {
        if (side == null) return;

        TMP_Text nameText = FindText(side, "Name_Text (TMP)");
        TMP_Text levelText = FindText(side, "Level_Text (TMP)");
        TMP_Text scoreText = FindText(side, "ScoreValue_Text (TMP)");
        TMP_Text timeText = FindText(side, "TimeValue_Text (TMP)");

        if (nameText != null)
        {
            nameText.text = string.IsNullOrEmpty(playerName) ? "" : playerName;
            ProfileManager.FitNameLabel(nameText);
        }
        if (levelText != null)
            levelText.text = $"Level {Mathf.Max(1, level):00}";
        if (scoreText != null)
            scoreText.text = score.ToString();
        if (timeText != null)
            timeText.text = FormatClock(time);

        ApplyCircularAvatar(side, avatar, opponentSlot);

        Transform livesRoot = FindChild(side, "Lives");
        if (livesRoot == null) return;
        int shownLives = Mathf.Clamp(lives, 0, livesRoot.childCount);
        for (int i = 0; i < livesRoot.childCount; i++)
        {
            Transform slot = livesRoot.GetChild(i);
            Transform life = slot.Find("Life");
            Transform lifeless = slot.Find("Lifeless");
            bool alive = i < shownLives;
            if (life != null) life.gameObject.SetActive(alive);
            if (lifeless != null) lifeless.gameObject.SetActive(!alive);
        }
    }

    private static void ApplyCircularAvatar(Transform side, Sprite avatar, bool hideUntilReady)
    {
        Transform avatarRoot = FindChild(side, "Avatar");
        if (avatarRoot == null) return;

        Image image = avatarRoot.GetComponent<Image>();
        if (image == null)
            image = avatarRoot.GetComponentInChildren<Image>(true);
        image = ProfileManager.ApplyCircularMask(image);
        if (image == null) return;

        image.gameObject.SetActive(true);
        if (image.transform.parent != null)
            image.transform.parent.gameObject.SetActive(true);

        if (avatar != null)
        {
            image.sprite = avatar;
            image.color = Color.white;
            image.enabled = true;
            image.preserveAspect = false;
            return;
        }

        if (!hideUntilReady) return;

        image.sprite = null;
        image.color = new Color(1f, 1f, 1f, 0f);
        image.enabled = true;
    }

    private static bool IsRealHudName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return !name.Trim().Equals("Opponent", System.StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadNetworkName(NetworkSudokuPlayer player, string fallback)
    {
        if (player == null || player.Object == null || !player.Object.IsValid)
            return fallback;
        try
        {
            string name = player.PlayerName.ToString();
            return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        }
        catch
        {
            return fallback;
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