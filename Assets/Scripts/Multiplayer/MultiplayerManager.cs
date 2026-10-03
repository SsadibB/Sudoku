using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

/// <summary>
/// Singleton (DontDestroyOnLoad) that owns the Photon Fusion NetworkRunner.
/// Handles International matchmaking and Competition room create/join.
/// </summary>
[DefaultExecutionOrder(-500)]
public class MultiplayerManager : MonoBehaviour, INetworkRunnerCallbacks
{
    private static MultiplayerManager _instance;
    private static bool _isQuitting;

    // Editor with "Enter Play Mode Options" (no domain reload) keeps statics alive
    // between runs, so reset them on every play/start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _isQuitting = false;
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
        _intentionalLeave = true;
        _backgrounded = false;
        _needsRejoin = false;
        if (IsInSession)
        {
            AnnounceBackground(false);
            NetworkSudokuPlayer.Local?.Forfeit();
            FlushFusionClient();
        }
        ReleaseMatchWakeLock();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static MultiplayerManager Instance
    {
        get
        {
            // While the app / Play mode is shutting down, other scripts unsubscribe
            // from events in their OnDisable/OnDestroy. Creating a new manager at
            // that moment is what left a stray "MultiplayerManager" object behind.
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = FindAnyObjectByType<MultiplayerManager>();
                if (_instance == null)
                {
                    var go = new GameObject("MultiplayerManager");
                    _instance = go.AddComponent<MultiplayerManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    // ---- Events ----
    public event Action OnConnectedToRoom;
    public event Action<string> OnConnectionFailed;
    public event Action<string> OnRoomCodeGenerated;       // Competition host: roomCode
    public event Action OnOpponentJoined;
    public event Action OnOpponentLeft;
    public event Action OnMatchmakingTimeout;
    public event Action OnOpponentIdentity;
    public event Action OnRematchStateChanged;
    /// <summary>The opponent's lives reached 0. The argument is the frozen match time.</summary>
    public event Action<float> OnOpponentOutOfLives;

    // ---- State ----
    public NetworkRunner Runner { get; private set; }
    public bool IsInSession => Runner != null && Runner.IsRunning;
    public bool IsMultiplayerGame { get; private set; }
    public UIManager.Difficulty MatchDifficulty { get; private set; }
    public int MatchLevel { get; private set; } = 1;
    public string MatchSessionId { get; private set; }
    private bool _rematchStartSent;
    public bool IsHost { get; private set; }

    public void SetMatchLevel(int level)
    {
        if (level > 0) MatchLevel = level;
    }

    public string LocalPlayerName { get; private set; } = "Player";
    public string OpponentName { get; private set; }
    public int OpponentAvatarIndex { get; private set; } = -1;
    public string OpponentAvatarUrl { get; private set; }
    public Sprite OpponentAvatarSprite { get; private set; }
    private Sprite rematchOpponentAvatarSprite;
    private Coroutine opponentAvatarRoutine;
    private string loadedOpponentAvatarUrl;
    private byte[] localAvatarJpeg;
    private byte[] localAvatarThumbnail;
    private int seenAvatarCount = -1;
    private int seenAvatarChecksum;
    private int opponentAvatarPixels;
    private bool opponentAvatarIsOriginal;
    private Texture2D opponentAvatarTexture;
    private byte[] opponentAvatarPayload;
    private bool _exitAnnounced;
    public int CurrentMatchEpoch { get; private set; }
    public bool OpponentForfeited { get; private set; }
    public int OpponentProfileLevel { get; private set; } = 1;
    public bool RematchRequestedLocal { get; private set; }
    public bool RematchRequestedRemote { get; private set; }
    public string RematchOpponentName { get; private set; }
    public int RematchOpponentAvatar { get; private set; } = -1;
    public bool WantsRematchLobby => RematchRequestedLocal && IsInSession;
    public bool BothWantRematch => RematchRequestedLocal && OpponentAcceptedRematch;

    public bool OpponentAcceptedRematch =>
        RematchRequestedRemote || NetworkSudokuPlayer.RemoteWantsRematch();

    public bool IsCompetitionMatch =>
        !string.IsNullOrEmpty(_sessionName) && _sessionName.StartsWith("COMP_", StringComparison.Ordinal);

    /// <summary>True once the other player in this same room has also accepted the rematch.</summary>
    public bool IsExpectedRematchOpponent()
    {
        return BothWantRematch && IsInSession;
    }

    private const float MatchmakingTimeoutSeconds = 60f;
    private const int MaxPlayersPerRoom = 2;

    private Coroutine matchmakingTimeoutCoroutine;
    private Coroutine identityResendCoroutine;
    private bool waitingForOpponent;
    private bool awaitHostStart;
    private bool sceneLoadRequested;
    private bool rematchLobbyRequested;

    private static readonly ReliableKey IdentityKey = ReliableKey.FromInts(1, 0, 0, 0);
    private static readonly ReliableKey RematchKey = ReliableKey.FromInts(2, 0, 0, 0);
    private static readonly ReliableKey AwayKey = ReliableKey.FromInts(3, 0, 0, 0);
    private static readonly ReliableKey ClockKey = ReliableKey.FromInts(4, 0, 0, 0);
    private static readonly ReliableKey ReadyKey = ReliableKey.FromInts(5, 0, 0, 0);
    private static readonly ReliableKey AvatarImageKey = ReliableKey.FromInts(6, 0, 0, 0);
    private static readonly ReliableKey ExitKey = ReliableKey.FromInts(7, 0, 0, 0);

    [SerializeField] private NetworkObject playerPrefab;

    private string _sessionName;
    private bool _intentionalLeave;
    private bool _backgrounded;
    private bool _needsRejoin;
    private bool _opponentLeftWhileAway;
    private int _rejoinAttempts;
    private Photon.Realtime.ConnectionHandler _connectionHandler;
    private AndroidJavaObject _matchWakeLock;

    private int _lastClockCenti = -1;
    private float _nextClockSend;
    private float _nextIdentitySend;
    private DateTime _sharedStartUtc;
    private bool _sharedStartSet;
    private float _localFrozenElapsed = -1f;
    private float _remoteFrozenElapsed = -1f;
    private bool _opponentLevelLocked;
    private bool _localGameplayReady;
    private bool _remoteGameplayReady;
    private bool _matchClockArmed;
    private float _nextReadySend;
    private float _nextRematchSend;

    public bool MatchClockArmed => _matchClockArmed;

    public float OpponentElapsedSeconds { get; private set; }
    public int OpponentLiveScore { get; private set; }
    public int OpponentLiveHalfHearts { get; private set; } = HeartManager.MaxHalfHearts;
    public bool OpponentBackgrounded { get; private set; }
    public bool OpponentOutOfLives { get; private set; }
    private bool _forceLocalLivesZero;
    private bool _forceRemoteLivesZero;
    private int _forceRemoteScore = -1;
    public bool OpponentClockFrozen => _remoteFrozenElapsed >= 0f;
    public bool HasOpponentClock => _sharedStartSet || _remoteFrozenElapsed >= 0f;
    public bool HasSharedClock => _sharedStartSet;

    /// <summary>Match time from the shared start. Screen-off does not stop it.</summary>
    public float SharedElapsed =>
        _sharedStartSet ? (float)Math.Max(0d, (DateTime.UtcNow - _sharedStartUtc).TotalSeconds) : 0f;

    public float LocalDisplayTime => _localFrozenElapsed >= 0f ? _localFrozenElapsed : SharedElapsed;
    public float RemoteDisplayTime => _remoteFrozenElapsed >= 0f ? _remoteFrozenElapsed : SharedElapsed;
    public bool ResultCaptured { get; private set; }
    public int SnapshotLocalScore { get; private set; }
    public int SnapshotRemoteScore { get; private set; }
    public int SnapshotLocalLives { get; private set; }
    public int SnapshotRemoteLives { get; private set; }

    /// <summary>
    /// Copy the current match clock, scores, and lives. Later ticks do not change it.
    /// </summary>
    public void CaptureResultSnapshot()
    {
        if (ResultCaptured) return;
        EnsureSharedClock();

        float now = HasSharedClock ? SharedElapsed : Mathf.Max(0f, OpponentElapsedSeconds);
        if (_localFrozenElapsed < 0f)
            _localFrozenElapsed = now;
        if (_remoteFrozenElapsed < 0f)
            _remoteFrozenElapsed = now;

        var game = SudokuGameManager.Instance;
        var local = NetworkSudokuPlayer.Local;
        var remote = NetworkSudokuPlayer.Remote;
        SnapshotLocalScore = game != null ? game.SessionScore : 0;
        SnapshotLocalLives = game != null ? game.CurrentHalfHearts / 2 : 0;
        SnapshotRemoteScore = OpponentLiveScore;
        SnapshotRemoteLives = Mathf.Max(0, OpponentLiveHalfHearts) / 2;

        try
        {
            if (local != null && local.Object != null && local.Object.IsValid)
            {
                if (game == null)
                {
                    SnapshotLocalScore = local.Score;
                    SnapshotLocalLives = local.HalfHearts / 2;
                }
            }
            if (remote != null && remote.Object != null && remote.Object.IsValid)
            {
                SnapshotRemoteScore = remote.Score;
                SnapshotRemoteLives = Mathf.Max(0, remote.HalfHearts) / 2;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Result snapshot used the last live stats: {ex.Message}");
        }

        if (_forceLocalLivesZero)
            SnapshotLocalLives = 0;
        if (_forceRemoteLivesZero)
            SnapshotRemoteLives = 0;
        if (_forceRemoteScore >= 0)
            SnapshotRemoteScore = _forceRemoteScore;

        ResultCaptured = true;
    }

    /// <summary>
    /// Freeze both displayed clocks at the same match time before the result snapshot.
    /// </summary>
    public void FreezeMatchClocks(float elapsed)
    {
        if (ResultCaptured) return;
        float safe = Mathf.Max(0f, elapsed);
        if (_localFrozenElapsed < 0f)
            _localFrozenElapsed = safe;
        if (_remoteFrozenElapsed < 0f)
            _remoteFrozenElapsed = safe;
    }

    public void NoteLocalOutOfLives()
    {
        _forceLocalLivesZero = true;
    }

    public void NoteOpponentOutOfLives(int remoteScore)
    {
        OpponentOutOfLives = true;
        OpponentLiveHalfHearts = 0;
        _forceRemoteLivesZero = true;
        if (remoteScore >= 0)
        {
            OpponentLiveScore = remoteScore;
            _forceRemoteScore = remoteScore;
        }
    }

    /// <summary>
    /// True while this device is in the background. Opponent-left events during
    /// that window are our own link pausing, not the other player quitting.
    /// </summary>
    public bool SuppressOpponentLeft => _backgrounded && !_intentionalLeave;

    public float OpponentElapsedSmooth => RemoteDisplayTime;

    /// <summary>
    /// Local loading has finished. The match clock starts only after both phones are ready,
    /// so a slow loading screen does not leave one timer behind.
    /// </summary>
    public void NotifyLocalGameplayReady()
    {
        if (!IsInSession || _intentionalLeave) return;
        _localGameplayReady = true;
        SendToOthers(ReadyKey, Encoding.UTF8.GetBytes("1"));
        _nextReadySend = Time.unscaledTime + 1f;
        TryArmMatchClock();
    }

    /// <summary>Start the clock if the other phone never reports ready.</summary>
    public void ArmMatchClockNow()
    {
        if (_matchClockArmed || ResultCaptured) return;
        float remoteElapsed = ReadNetworkElapsed(NetworkSudokuPlayer.Remote, false);
        ArmFromElapsed(remoteElapsed > 5f ? remoteElapsed : 0f);
    }

    private void TryArmMatchClock()
    {
        if (_matchClockArmed || ResultCaptured || !_localGameplayReady) return;

        float remoteElapsed = ReadNetworkElapsed(NetworkSudokuPlayer.Remote, false);
        if (remoteElapsed > 5f)
        {
            ArmFromElapsed(remoteElapsed);
            return;
        }

        if (!_remoteGameplayReady) return;
        if (Runner != null && Runner.IsRunning && !Runner.IsSharedModeMasterClient)
            return;

        ArmFromElapsed(0f);
    }

    private void ArmFromElapsed(float elapsed)
    {
        if (_matchClockArmed) return;
        float startAt = Mathf.Max(0f, elapsed);
        _sharedStartUtc = DateTime.UtcNow.AddSeconds(-startAt);
        _sharedStartSet = true;
        _matchClockArmed = true;
        SudokuGameManager.Instance?.AlignMatchClock(startAt);
        NetworkSudokuPlayer.Local?.FlushElapsed(startAt);
    }

    /// <summary>
    /// The shared clock is started in ArmFromElapsed, after both phones have loaded.
    /// </summary>
    public void EnsureSharedClock()
    {
        if (_sharedStartSet || ResultCaptured || !_matchClockArmed) return;
        _sharedStartUtc = DateTime.UtcNow;
        _sharedStartSet = true;
    }

    /// <summary>
    /// Move this phone forward to the latest match time either player has reached.
    /// A later local start must not keep this device behind.
    /// </summary>
    public void PullAuthoritativeClock()
    {
        if (ResultCaptured || !_matchClockArmed) return;

        float best = _sharedStartSet ? SharedElapsed : -1f;

        float remoteElapsed = ReadNetworkElapsed(NetworkSudokuPlayer.Remote, false);
        if (remoteElapsed >= 0f)
            best = Mathf.Max(best, remoteElapsed);

        if (best < 0f) return;
        if (_sharedStartSet && best > SharedElapsed + 600f) return;
        if (_sharedStartSet && best <= SharedElapsed + 0.05f) return;

        _sharedStartUtc = DateTime.UtcNow.AddSeconds(-best);
        _sharedStartSet = true;
    }

    private float ReadNetworkElapsed(NetworkSudokuPlayer player, bool allowFrozen)
    {
        if (player == null || player.Object == null || !player.Object.IsValid) return -1f;
        float sample;
        try
        {
            if (player.HasForfeited) return -1f;
            // A frozen clock belongs to the match that just ended. A rematch
            // must not start from that time.
            if (!allowFrozen && player.ClockFrozen) return -1f;
            var local = NetworkSudokuPlayer.Local;
            if (local != null && local.Object != null && local.Object.IsValid
                && player.MatchEpoch != local.MatchEpoch)
                return -1f;
            sample = Mathf.Max(player.ElapsedTime, player.ElapsedWholeSeconds);
            if (player.ClockFrozen && player.FrozenElapsed > sample)
                sample = player.FrozenElapsed;
        }
        catch
        {
            return -1f;
        }

        if (sample < 0f || sample > 21600f) return -1f;
        return sample;
    }

    public void FreezeLocalClock()
    {
        if (_localFrozenElapsed >= 0f) return;
        EnsureSharedClock();
        if (_sharedStartSet)
        {
            _localFrozenElapsed = SharedElapsed;
            return;
        }

        float fallback = 0f;
        var game = SudokuGameManager.Instance;
        if (game != null)
            fallback = Mathf.Max(0f, game.ElapsedSeconds);
        _localFrozenElapsed = fallback;
    }

    private void AdoptAuthoritativeElapsed(float elapsed, bool frozen)
    {
        if (elapsed < 0f) return;
        OpponentElapsedSeconds = elapsed;
        if (frozen)
        {
            // The open result panel keeps the time it captured.
            if (!ResultCaptured || _remoteFrozenElapsed < 0f)
                _remoteFrozenElapsed = elapsed;
            return;
        }

        if (!_matchClockArmed)
        {
            if (_localGameplayReady && (_remoteGameplayReady || elapsed > 5f))
                ArmFromElapsed(elapsed);
            return;
        }

        if (ResultCaptured)
            return;

        // The further match time wins, on either phone.
        float shown = _sharedStartSet ? SharedElapsed : -1f;
        if (!_sharedStartSet || elapsed > shown + 0.05f)
        {
            _sharedStartUtc = DateTime.UtcNow.AddSeconds(-elapsed);
            _sharedStartSet = true;
        }
    }

    // ---- Session names ----
    // International rooms are named: "INT_{difficulty}_{shortGuid}"
    // Photon matchmaking finds a room by filtering on custom property "Difficulty".
    // Competition rooms: "COMP_{roomCode}"

    // Same stale-leftover guard as MultiplayerLobbyUI (see its comment for the
    // full explanation). MultiplayerLobbyUI is a child of this object, so
    // destroying a leftover MultiplayerManager also takes its stale
    // MultiplayerLobbyUI child down with it before the new scene's Awake
    // calls run.
    private void Awake()
    {
        // Detach any children (such as MultiplayerLobbyUI) so they remain in their scene
        // and are never pulled into DontDestroyOnLoad or destroyed with a duplicate manager.
        transform.DetachChildren();

        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        RefreshLocalPlayerName();
    }

    /// <summary>
    /// Screen-off and app background must not drop the session.
    /// Fusion stops ticking while Unity is paused, so a fallback thread keeps
    /// sending Photon acks and a partial wake lock keeps that thread scheduled.
    /// </summary>
    public void KeepSessionAliveInBackground()
    {
        Application.runInBackground = true;
        AcquireMatchWakeLock();

        var client = ResolveFusionClient(Runner);
        if (client == null) return;

        if (_connectionHandler == null)
            _connectionHandler = Photon.Realtime.ConnectionHandler.BuildInstance(client, "SudokuMatch");

        _connectionHandler.Client = client;
        _connectionHandler.KeepAliveInBackground = int.MaxValue;
    }

    private void OnApplicationPause(bool pause)
    {
        _backgrounded = pause;
        if (!IsMultiplayerGame || _intentionalLeave) return;

        if (pause)
        {
            KeepSessionAliveInBackground();
            AnnounceBackground(true);
            FlushFusionClient();
            return;
        }

        AnnounceBackground(false);
        BroadcastIdentity();
        FlushMatchClock();
        if (_opponentLeftWhileAway)
        {
            _opponentLeftWhileAway = false;
            OnOpponentLeft?.Invoke();
        }
        if (!_needsRejoin) return;
        _needsRejoin = false;
        RejoinActiveSession();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!IsMultiplayerGame || _intentionalLeave) return;

        if (!hasFocus)
        {
            KeepSessionAliveInBackground();
            AnnounceBackground(true);
            FlushFusionClient();
            return;
        }

        AnnounceBackground(false);
        BroadcastIdentity();
        FlushMatchClock();
        if (_opponentLeftWhileAway)
        {
            _opponentLeftWhileAway = false;
            OnOpponentLeft?.Invoke();
        }
        if (!_needsRejoin) return;
        _needsRejoin = false;
        RejoinActiveSession();
    }

    private void Update()
    {
        if (!IsInSession || _intentionalLeave) return;
        PullRemoteProfile();
        if (RematchRequestedLocal && Time.unscaledTime >= _nextRematchSend)
        {
            _nextRematchSend = Time.unscaledTime + 0.5f;
            NetworkSudokuPlayer.Local?.SetWantsRematch(true);
            BroadcastRematch();
        }
        if (RematchRequestedLocal)
            NoteRemoteRematch();
        if (BothWantRematch)
            TryBeginRematch();
        if (_localGameplayReady && !_matchClockArmed && Time.unscaledTime >= _nextReadySend)
        {
            _nextReadySend = Time.unscaledTime + 1f;
            SendToOthers(ReadyKey, Encoding.UTF8.GetBytes("1"));
            TryArmMatchClock();
        }
        if (Time.unscaledTime < _nextIdentitySend) return;
        _nextIdentitySend = Time.unscaledTime + 3f;
        BroadcastIdentity();
    }

    private void FlushMatchClock()
    {
        var match = MultiplayerGameController.Instance;
        if (match == null || !match.IsMatchRunning) return;
        EnsureSharedClock();
        float elapsed = HasSharedClock ? SharedElapsed : 0f;
        NetworkSudokuPlayer.Local?.FlushElapsed(elapsed);
    }

    /// <summary>
    /// Pushes the live clock, score, and hearts to the opponent.
    /// Throttled; score and hearts still replicate on their own networked fields.
    /// </summary>
    public void PublishLiveStats(float elapsed, int score, int halfHearts, bool frozen = false, bool force = false)
    {
        if (Runner == null || !Runner.IsRunning || elapsed < 0f) return;

        int centi = Mathf.FloorToInt(elapsed * 100f);
        bool sameSecond = _lastClockCenti >= 0 && centi / 100 == _lastClockCenti / 100;
        if (!frozen && !force && sameSecond && Time.unscaledTime < _nextClockSend) return;

        _lastClockCenti = centi;
        _nextClockSend = Time.unscaledTime + 0.2f;
        string payload = centi + "\n" + score + "\n" + halfHearts + "\n" + (frozen ? "1" : "0");
        SendToOthers(ClockKey, Encoding.UTF8.GetBytes(payload));
    }

    private void AnnounceBackground(bool away)
    {
        if (Runner == null || !Runner.IsRunning) return;
        SendToOthers(AwayKey, Encoding.UTF8.GetBytes(away ? "1" : "0"));
        NetworkSudokuPlayer.Local?.SetBackgrounded(away);
    }

    private void FlushFusionClient()
    {
        try
        {
            ResolveFusionClient(Runner)?.Service();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Flush skipped: {ex.Message}");
        }
    }

    public void SetLocalPlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        LocalPlayerName = name.Trim();

        var local = NetworkSudokuPlayer.Local;
        if (local != null && local.HasStateAuthority)
        {
            local.PlayerName = LocalPlayerName;
            if (ProfileManager.Instance != null)
            {
                local.AvatarIndex = string.IsNullOrEmpty(ProfileManager.Instance.AvatarUrl)
                    ? ProfileManager.Instance.AvatarPresetIndex
                    : -1;
            }
        }

        BroadcastIdentity();
    }

    public void RefreshLocalIdentity()
    {
        if (ProfileManager.Instance != null && !string.IsNullOrWhiteSpace(ProfileManager.Instance.PlayerName))
            SetLocalPlayerName(ProfileManager.Instance.PlayerName);
        else
            BroadcastIdentity();
    }

    private void RefreshLocalPlayerName()
    {
        if (ProfileManager.Instance != null && !string.IsNullOrWhiteSpace(ProfileManager.Instance.PlayerName))
        {
            LocalPlayerName = ProfileManager.Instance.PlayerName.Trim();
            return;
        }

        // Try to pull the display name from the AuthLogin session
        var authMgr = SadibTools.AuthLogin.AuthManager.Instance;
        if (authMgr != null && authMgr.IsSignedIn && authMgr.CurrentSession != null)
        {
            string name = authMgr.CurrentSession.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                LocalPlayerName = name;
                return;
            }
        }

        // Unauthenticated fallback name (e.g. Player 4821)
        if (string.IsNullOrEmpty(LocalPlayerName) || LocalPlayerName == "Player")
        {
            LocalPlayerName = $"Player_{UnityEngine.Random.Range(1000, 9999)}";
        }
    }

    private bool isStartingGame = false;

    // ====================================================================
    //  PUBLIC API
    // ====================================================================

    /// <summary>Start International matchmaking for the given difficulty.</summary>
    public async void StartInternational(UIManager.Difficulty difficulty)
    {
        if (isStartingGame)
        {
            Debug.LogWarning("[MultiplayerManager] StartInternational ignored: already starting a session.");
            return;
        }
        isStartingGame = true;

        try
        {
            RefreshLocalPlayerName();
            MatchDifficulty = difficulty;
            MatchLevel = UnityEngine.Random.Range(1, 500);
            IsHost = false;
            IsMultiplayerGame = true;
            waitingForOpponent = true;
            awaitHostStart = false;
            sceneLoadRequested = false;
            _intentionalLeave = false;
            _needsRejoin = false;
            _sessionName = $"INT_{(int)difficulty}";
            ClearOpponentIdentity();
            ClearRematchFlags();

            StartGameResult result = default;
            bool joined = false;
            for (int slot = 0; slot < 6 && !joined; slot++)
            {
                _sessionName = slot == 0
                    ? $"INT_{(int)difficulty}"
                    : $"INT_{(int)difficulty}_{slot}";

                var runner = await CreateNetworkRunner();
                var startArgs = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    Address = NetAddress.Any(),
                    SessionName = _sessionName,
                    Scene = GetStartGameSceneInfo(),
                    PlayerCount = 2,
                    CustomPhotonAppSettings = GetPhotonAppSettings(),
                    SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                    ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>()
                };

                result = await runner.StartGame(startArgs);
                if (result.Ok)
                {
                    joined = true;
                    break;
                }

                await ShutdownRunner();
                if (result.ShutdownReason != ShutdownReason.GameIsFull)
                    break;
            }

            if (!joined)
            {
                Debug.LogError($"[MultiplayerManager] StartInternational StartGame failed. ShutdownReason: {result.ShutdownReason}");
                IsMultiplayerGame = false;
                OnConnectionFailed?.Invoke(result.ShutdownReason.ToString());
                return;
            }

            // Start timeout coroutine — fires OnMatchmakingTimeout after 60 s
            // if we still haven't seen a second player
            KeepSessionAliveInBackground();
            StartCoroutine(ConfigureRoomSoon());
            if (matchmakingTimeoutCoroutine != null) StopCoroutine(matchmakingTimeoutCoroutine);
            matchmakingTimeoutCoroutine = StartCoroutine(MatchmakingTimeoutRoutine());
        }
        finally
        {
            isStartingGame = false;
        }
    }

    /// <summary>Create a Competition room as host. Returns the 6-char room code.</summary>
    public async Task<string> CreateCompetitionRoom(UIManager.Difficulty difficulty)
    {
        if (isStartingGame)
        {
            Debug.LogWarning("[MultiplayerManager] CreateCompetitionRoom ignored: already starting a session.");
            return null;
        }
        isStartingGame = true;

        try
        {
            RefreshLocalPlayerName();
            MatchDifficulty = difficulty;
            IsHost = true;
            IsMultiplayerGame = true;
            waitingForOpponent = true;
            awaitHostStart = true;
            sceneLoadRequested = false;
            _intentionalLeave = false;
            _needsRejoin = false;
            ClearOpponentIdentity();
            ClearRematchFlags();

            string roomCode = GenerateRoomCode();
            _sessionName = "COMP_" + roomCode;
            MatchLevel = (Math.Abs(roomCode.Trim().ToUpper().GetHashCode()) % 500) + 1;

            var runner = await CreateNetworkRunner();

            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                Address = NetAddress.Any(),
                SessionName = _sessionName,
                Scene = GetStartGameSceneInfo(),
                PlayerCount = 2,
                CustomPhotonAppSettings = GetPhotonAppSettings(),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>()
            };

            var result = await runner.StartGame(startArgs);
            if (!result.Ok)
            {
                Debug.LogError($"[MultiplayerManager] CreateCompetitionRoom StartGame failed. ShutdownReason: {result.ShutdownReason}");
                IsMultiplayerGame = false;
                OnConnectionFailed?.Invoke(result.ShutdownReason.ToString());
                await ShutdownRunner();
                return null;
            }

            KeepSessionAliveInBackground();
            StartCoroutine(ConfigureRoomSoon());
            OnRoomCodeGenerated?.Invoke(roomCode);
            return roomCode;
        }
        finally
        {
            isStartingGame = false;
        }
    }

    /// <summary>Join an existing Competition room by room code (guest).</summary>
    public async void JoinCompetitionRoom(string roomCode)
    {
        if (isStartingGame)
        {
            Debug.LogWarning("[MultiplayerManager] JoinCompetitionRoom ignored: already starting a session.");
            return;
        }
        isStartingGame = true;

        try
        {
            RefreshLocalPlayerName();
            MatchLevel = (Math.Abs(roomCode.Trim().ToUpper().GetHashCode()) % 500) + 1;
            IsHost = false;
            IsMultiplayerGame = true;
            waitingForOpponent = true;
            awaitHostStart = true;
            sceneLoadRequested = false;
            _intentionalLeave = false;
            _needsRejoin = false;
            _sessionName = "COMP_" + roomCode.ToUpper().Trim();
            ClearOpponentIdentity();
            ClearRematchFlags();

            var runner = await CreateNetworkRunner();

            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                Address = NetAddress.Any(),
                SessionName = _sessionName,
                Scene = GetStartGameSceneInfo(),
                PlayerCount = 2,
                CustomPhotonAppSettings = GetPhotonAppSettings(),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>()
            };

            var result = await runner.StartGame(startArgs);
            if (!result.Ok)
            {
                Debug.LogError($"[MultiplayerManager] JoinCompetitionRoom StartGame failed. ShutdownReason: {result.ShutdownReason}");
                IsMultiplayerGame = false;
                OnConnectionFailed?.Invoke($"Room not found or failed ({result.ShutdownReason}).");
                await ShutdownRunner();
                return;
            }

            KeepSessionAliveInBackground();
            StartCoroutine(ConfigureRoomSoon());
            StartCoroutine(AnnounceOpponentSoon());
            OnConnectedToRoom?.Invoke();
        }
        finally
        {
            isStartingGame = false;
        }
    }

    /// <summary>Disconnect from Photon and clean up.</summary>
    public async void Disconnect()
    {
        _intentionalLeave = true;
        _needsRejoin = false;
        _backgrounded = false;
        IsMultiplayerGame = false;
        waitingForOpponent = false;
        awaitHostStart = false;
        sceneLoadRequested = false;
        rematchLobbyRequested = false;
        isStartingGame = false;
        ClearOpponentIdentity();
        ClearRematchFlags();
        if (matchmakingTimeoutCoroutine != null)
        {
            StopCoroutine(matchmakingTimeoutCoroutine);
            matchmakingTimeoutCoroutine = null;
        }
        if (Runner != null) await ShutdownRunner();
        ReleaseMatchWakeLock();
    }

    // ====================================================================
    //  PRIVATE HELPERS
    // ====================================================================

    private NetworkSceneInfo GetStartGameSceneInfo()
    {
        return new NetworkSceneInfo();
    }

    private Fusion.Photon.Realtime.FusionAppSettings GetPhotonAppSettings()
    {
        if (Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out var global) && global.AppSettings != null)
        {
            return global.AppSettings;
        }

        var settingsAsset = Resources.Load<Fusion.Photon.Realtime.PhotonAppSettings>("PhotonAppSettings");
        if (settingsAsset != null && settingsAsset.AppSettings != null)
        {
            return settingsAsset.AppSettings;
        }

        Debug.LogWarning("[MultiplayerManager] PhotonAppSettings asset not found in Resources! Using global fallback.");
        return null;
    }

    private async Task<NetworkRunner> CreateNetworkRunner()
    {
        if (Runner != null)
        {
            await ShutdownRunner();
        }

        foreach (var r in new List<NetworkRunner>(NetworkRunner.Instances))
        {
            if (r != null)
            {
                try { r.RemoveCallbacks(this); } catch { }
                try { await r.Shutdown(); } catch { }
                if (r.gameObject != null) Destroy(r.gameObject);
            }
        }

        var runnerGO = new GameObject("FusionNetworkRunner");
        DontDestroyOnLoad(runnerGO);

        var runner = runnerGO.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        runner.AddCallbacks(this); // Callbacks MUST be added before StartGame!

        runnerGO.AddComponent<NetworkSceneManagerDefault>();
        runnerGO.AddComponent<NetworkObjectProviderDefault>();

        Runner = runner;
        return runner;
    }

    private async Task ShutdownRunner()
    {
        if (Runner == null) return;
        var r = Runner;
        Runner = null;

        try
        {
            await r.Shutdown();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ShutdownRunner exception ignored: {ex.Message}");
        }

        if (r != null && r.gameObject != null)
        {
            Destroy(r.gameObject);
        }
    }

    private string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[6];
        for (int i = 0; i < 6; i++)
            code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
        return new string(code);
    }

    private IEnumerator MatchmakingTimeoutRoutine()
    {
        yield return new WaitForSeconds(MatchmakingTimeoutSeconds);
        if (waitingForOpponent)
        {
            waitingForOpponent = false;
            OnMatchmakingTimeout?.Invoke();
            Disconnect();
        }
    }

    // ====================================================================
    //  INetworkRunnerCallbacks
    // ====================================================================
    // Implemented explicitly (void INetworkRunnerCallbacks.Foo(...) instead of
    // public void Foo(...)) purely to stop Unity's compiler from matching
    // names like OnConnectedToServer/OnDisconnectedFromServer against its own
    // legacy (pre-Fusion) network message signatures and flagging false
    // "incorrect signature" warnings. Fusion only ever calls these via the
    // interface (through Runner.AddCallbacks/the callbacks list), never by
    // name, so explicit implementation doesn't change any behavior — it just
    // hides these names from Unity's message-name scanner.

    void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
            EnsureLocalPlayerObject();
        else
        {
            _opponentPlayer = player;
            _hasOpponentPlayer = true;
        }

        AnnounceOpponentIfPresent();
    }

    private void AnnounceOpponentIfPresent()
    {
        if (!waitingForOpponent || Runner == null || !Runner.IsRunning) return;

        int playerCount = 0;
        foreach (var _ in Runner.ActivePlayers) playerCount++;
        if (playerCount < 2) return;

        waitingForOpponent = false;
        if (matchmakingTimeoutCoroutine != null)
        {
            StopCoroutine(matchmakingTimeoutCoroutine);
            matchmakingTimeoutCoroutine = null;
        }

        BroadcastIdentity();
        OnOpponentJoined?.Invoke();
        if (identityResendCoroutine != null)
            StopCoroutine(identityResendCoroutine);
        identityResendCoroutine = StartCoroutine(ResendIdentity());
    }

    private IEnumerator AnnounceOpponentSoon()
    {
        for (int i = 0; i < 20; i++)
        {
            if (!waitingForOpponent) yield break;
            AnnounceOpponentIfPresent();
            if (!waitingForOpponent) yield break;
            yield return null;
        }
    }

    private IEnumerator ResendIdentity()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        BroadcastIdentity();
        yield return new WaitForSecondsRealtime(1f);
        BroadcastIdentity();
        identityResendCoroutine = null;
    }

    public void RememberOpponent(string name, int profileLevel)
    {
        if (IsRealPlayerName(name) && string.IsNullOrEmpty(OpponentName))
            OpponentName = name.Trim();
        if (!_opponentLevelLocked && IsSaneProfileLevel(profileLevel))
        {
            OpponentProfileLevel = profileLevel;
            _opponentLevelLocked = true;
        }
    }

    private static bool IsRealPlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Equals("Opponent", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Player", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("Player_", StringComparison.Ordinal)) return false;
        for (int i = 0; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return true;
        }
        return false;
    }

    private static bool IsSaneProfileLevel(int level) => level >= 1 && level <= 300;

    private void PullRemoteProfile()
    {
        var remote = NetworkSudokuPlayer.Remote;
        if (remote == null || remote.Object == null || !remote.Object.IsValid) return;

        bool changed = false;
        try
        {
            string name = remote.PlayerName.ToString();
            if (IsRealPlayerName(name) && !string.Equals(OpponentName, name.Trim(), StringComparison.Ordinal))
            {
                OpponentName = name.Trim();
                changed = true;
            }

            int level = remote.ProfileLevel;
            if (!_opponentLevelLocked && IsSaneProfileLevel(level) && OpponentProfileLevel != level)
            {
                OpponentProfileLevel = level;
                changed = true;
            }

            int index = remote.AvatarIndex;
            if (index >= -1 && index < 64 && OpponentAvatarIndex != index)
            {
                OpponentAvatarIndex = index;
                changed = true;
            }

            int count = remote.AvatarByteCount;
            if (count > 32 && count <= remote.AvatarJpeg.Length && string.IsNullOrEmpty(loadedOpponentAvatarUrl))
            {
                int checksum = remote.AvatarJpeg[0]
                    + remote.AvatarJpeg[count / 2] * 31
                    + remote.AvatarJpeg[count - 1] * 17
                    + count;
                if (count != seenAvatarCount || checksum != seenAvatarChecksum)
                {
                    byte[] jpg = new byte[count];
                    for (int i = 0; i < count; i++)
                        jpg[i] = remote.AvatarJpeg[i];
                    if (ApplyOpponentAvatarBytes(jpg, false))
                    {
                        seenAvatarCount = count;
                        seenAvatarChecksum = checksum;
                        changed = true;
                    }
                    else if (OpponentAvatarSprite != null)
                    {
                        seenAvatarCount = count;
                        seenAvatarChecksum = checksum;
                    }
                }
            }
        }
        catch
        {
            // The networked state can be unread for a frame while the player spawns.
        }

        if (changed)
            OnOpponentIdentity?.Invoke();
    }

    private void BroadcastIdentity()
    {
        if (Runner == null) return;

        int avatar = ProfileManager.Instance != null ? ProfileManager.Instance.AvatarPresetIndex : -1;
        int level = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileLevel : 1;
        string avatarUrl = ProfileManager.Instance != null ? ProfileManager.Instance.AvatarUrl : null;
        if (string.IsNullOrEmpty(avatarUrl)) avatarUrl = "";
        else avatar = -1;
        string payload = LocalPlayerName + "\n" + avatar + "\n" + level + "\n" + avatarUrl;
        SendToOthers(IdentityKey, Encoding.UTF8.GetBytes(payload));
        SendAvatarImage();
    }

    private PlayerRef _opponentPlayer;
    private bool _hasOpponentPlayer;

    private void BroadcastRematch()
    {
        if (Runner == null || !Runner.IsRunning) return;
        byte[] payload = Encoding.UTF8.GetBytes("1");
        bool sent = false;
        foreach (var other in Runner.ActivePlayers)
        {
            if (other == Runner.LocalPlayer) continue;
            _opponentPlayer = other;
            _hasOpponentPlayer = true;
            Runner.SendReliableDataToPlayer(other, RematchKey, payload);
            sent = true;
        }

        if (!sent && _hasOpponentPlayer && _opponentPlayer != Runner.LocalPlayer)
        {
            try
            {
                Runner.SendReliableDataToPlayer(_opponentPlayer, RematchKey, payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MultiplayerManager] Rematch send skipped: {ex.Message}");
            }
        }
    }

    private void SendAvatarImage()
    {
        Sprite sprite = ProfileManager.Instance != null ? ProfileManager.Instance.CurrentAvatarSprite : null;
        Texture source = sprite != null ? sprite.texture : null;
        if (source == null) return;

        int native = Mathf.RoundToInt(Mathf.Max(sprite.textureRect.width, sprite.textureRect.height));
        int[] sizes = { Mathf.Clamp(native, 96, 384), 256, 192, 128 };
        for (int s = 0; s < sizes.Length; s++)
        {
            byte[] jpg = EncodeAvatarJpeg(source, sprite, sizes[s], 82);
            if (jpg == null || jpg.Length <= 32 || jpg.Length > 48000)
                continue;

            localAvatarJpeg = jpg;
            SendToOthers(AvatarImageKey, jpg);
            break;
        }

        byte[] thumbnail = EncodeAvatarJpeg(source, sprite, 96, 70);
        if (thumbnail != null && thumbnail.Length > 32 && thumbnail.Length <= 1024)
        {
            localAvatarThumbnail = thumbnail;
            NetworkSudokuPlayer.Local?.PublishAvatarJpeg(thumbnail);
        }
    }

    private byte[] EncodeAvatarJpeg(Texture source, Sprite sprite, int size, int quality)
    {
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Rect area = sprite.textureRect;
            float width = Mathf.Max(1f, source.width);
            float height = Mathf.Max(1f, source.height);
            Graphics.Blit(source, rt, new Vector2(area.width / width, area.height / height), new Vector2(area.x / width, area.y / height));
            RenderTexture.active = rt;
            readable = new Texture2D(size, size, TextureFormat.RGB24, false);
            readable.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            readable.Apply(false, false);
            return readable.EncodeToJPG(quality);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MultiplayerManager] Avatar image send skipped: " + ex.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (readable != null)
                Destroy(readable);
        }
    }

    public void PublishCachedAvatar()
    {
        if (localAvatarThumbnail != null)
            NetworkSudokuPlayer.Local?.PublishAvatarJpeg(localAvatarThumbnail);
    }

    private bool ApplyOpponentAvatarBytes(byte[] jpg, bool replace = false)
    {
        if (jpg == null || jpg.Length < 32) return false;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (!texture.LoadImage(jpg))
        {
            Destroy(texture);
            return false;
        }

        int pixels = texture.width * texture.height;
        bool haveSprite = OpponentAvatarSprite != null;
        if (haveSprite && (opponentAvatarIsOriginal || (!replace && pixels <= opponentAvatarPixels)))
        {
            Destroy(texture);
            return false;
        }

        opponentAvatarPayload = jpg;
        RememberOpponentAvatar(texture, false);
        OnOpponentIdentity?.Invoke();
        return true;
    }

    private Sprite CreateSharpAvatarSprite(Texture2D texture)
    {
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 4;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        opponentAvatarPixels = texture.width * texture.height;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    // The photo has to stay on this DontDestroyOnLoad object. A texture owned by a
    // web request, or only referenced by the searching-panel image, is destroyed
    // when that scene unloads and the gameplay portrait goes blank.
    private void RememberOpponentAvatar(Texture2D texture, bool markOriginal)
    {
        if (texture == null) return;
        if (opponentAvatarTexture != null && opponentAvatarTexture != texture)
            Destroy(opponentAvatarTexture);
        opponentAvatarTexture = texture;
        OpponentAvatarSprite = CreateSharpAvatarSprite(texture);
        if (markOriginal)
            opponentAvatarIsOriginal = true;
    }

    private static Texture2D CopyReadableTexture(Texture2D source)
    {
        if (source == null) return null;
        try
        {
            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.SetPixels(source.GetPixels());
            copy.Apply(false, false);
            return copy;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MultiplayerManager] Opponent photo copy skipped: " + ex.Message);
            return null;
        }
    }

    private void SendToOthers(ReliableKey key, byte[] payload)
    {
        if (Runner == null || !Runner.IsRunning || payload == null) return;

        bool sent = false;
        foreach (var other in Runner.ActivePlayers)
        {
            if (other == Runner.LocalPlayer) continue;
            Runner.SendReliableDataToPlayer(other, key, payload);
            sent = true;
        }

        if (!sent && _hasOpponentPlayer && _opponentPlayer != Runner.LocalPlayer)
        {
            try
            {
                Runner.SendReliableDataToPlayer(_opponentPlayer, key, payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiplayerManager] Send skipped: " + ex.Message);
            }
        }
    }

    public void AnnounceLocalExit()
    {
        if (_exitAnnounced || Runner == null || !Runner.IsRunning) return;
        _exitAnnounced = true;
        SendToOthers(ExitKey, Encoding.UTF8.GetBytes("exit"));
        StartCoroutine(ResendExit());
    }

    private IEnumerator ResendExit()
    {
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (!IsInSession || Runner == null) yield break;
            SendToOthers(ExitKey, Encoding.UTF8.GetBytes("exit"));
        }
    }

    private void NoteOpponentExit()
    {
        if (OpponentForfeited) return;
        OpponentForfeited = true;
        float elapsed = HasSharedClock ? SharedElapsed : 0f;
        FreezeMatchClocks(elapsed);
    }

    private void EnsureLocalPlayerObject()
    {
        if (NetworkSudokuPlayer.Local != null) return;
        if (playerPrefab == null || Runner == null || !Runner.IsRunning) return;
        if (Runner.LocalPlayer == PlayerRef.None) return;
        try
        {
            Runner.Spawn(playerPrefab, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MultiplayerManager] Player spawn skipped: " + ex.Message);
        }
    }

    private void ClearOpponentIdentity()
    {
        OpponentName = null;
        OpponentAvatarIndex = -1;
        OpponentAvatarUrl = null;
        OpponentAvatarSprite = null;
        if (opponentAvatarTexture != null)
        {
            Destroy(opponentAvatarTexture);
            opponentAvatarTexture = null;
        }
        opponentAvatarPayload = null;
        loadedOpponentAvatarUrl = null;
        seenAvatarCount = -1;
        seenAvatarChecksum = 0;
        opponentAvatarPixels = 0;
        opponentAvatarIsOriginal = false;
        if (opponentAvatarRoutine != null)
        {
            StopCoroutine(opponentAvatarRoutine);
            opponentAvatarRoutine = null;
        }
        OpponentProfileLevel = 1;
        OpponentElapsedSeconds = 0f;
        OpponentLiveScore = 0;
        OpponentLiveHalfHearts = HeartManager.MaxHalfHearts;
        OpponentBackgrounded = false;
        OpponentOutOfLives = false;
        _forceLocalLivesZero = false;
        _forceRemoteLivesZero = false;
        _forceRemoteScore = -1;
        _opponentLevelLocked = false;
        _sharedStartSet = false;
        _localFrozenElapsed = -1f;
        _remoteFrozenElapsed = -1f;
        _localGameplayReady = false;
        _remoteGameplayReady = false;
        _matchClockArmed = false;
        ResultCaptured = false;
        SnapshotLocalScore = 0;
        SnapshotRemoteScore = 0;
        SnapshotLocalLives = 0;
        SnapshotRemoteLives = 0;
        _lastClockCenti = -1;
        OpponentForfeited = false;
        _exitAnnounced = false;
        CurrentMatchEpoch = 0;
    }

    /// <summary>
    /// Drop the finished match's clock, score, and lives. Names stay so the rematch
    /// is still the same two players.
    /// </summary>
    public void ResetMatchProgress()
    {
        OpponentElapsedSeconds = 0f;
        OpponentLiveScore = 0;
        OpponentLiveHalfHearts = HeartManager.MaxHalfHearts;
        OpponentBackgrounded = false;
        OpponentOutOfLives = false;
        _forceLocalLivesZero = false;
        _forceRemoteLivesZero = false;
        _forceRemoteScore = -1;
        _sharedStartSet = false;
        _localFrozenElapsed = -1f;
        _remoteFrozenElapsed = -1f;
        _localGameplayReady = false;
        _remoteGameplayReady = false;
        _matchClockArmed = false;
        ResultCaptured = false;
        SnapshotLocalScore = 0;
        SnapshotRemoteScore = 0;
        SnapshotLocalLives = 0;
        SnapshotRemoteLives = 0;
        _lastClockCenti = -1;
        RematchRequestedLocal = false;
        RematchRequestedRemote = false;
        OpponentForfeited = false;
        _exitAnnounced = false;
    }

    private void ClearRematchFlags()
    {
        RematchRequestedLocal = false;
        RematchRequestedRemote = false;
        RematchOpponentName = null;
        RematchOpponentAvatar = -1;
        _hasOpponentPlayer = false;
    }

    /// <summary>
    /// Competition and rematch stay in the lobby until the host presses Start.
    /// International matchmaking calls this after the opponent has been shown.
    /// Only the shared-mode master client loads the scene; Fusion brings the other player along.
    /// </summary>
    public void HostStartMatch()
    {
        if (sceneLoadRequested) return;
        if (Runner == null || !Runner.IsRunning || !Runner.IsSharedModeMasterClient) return;

        sceneLoadRequested = true;
        rematchLobbyRequested = false;
        waitingForOpponent = false;
        ClearRematchFlags();

        Runner.LoadScene(SceneRef.FromIndex(
            SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GameScene.unity")));
    }

    public void RequestRematch()
    {
        RememberRematchTarget();
        RematchRequestedLocal = true;
        awaitHostStart = true;
        _rematchStartSent = false;
        sceneLoadRequested = false;
        rematchLobbyRequested = false;
        _nextRematchSend = Time.unscaledTime + 0.5f;
        NetworkSudokuPlayer.Local?.SetWantsRematch(true);
        BroadcastRematch();
        BroadcastIdentity();
        FlushFusionClient();
        OnRematchStateChanged?.Invoke();
        NoteRemoteRematch();
        TryBeginRematch();
    }

    private void NoteRemoteRematch()
    {
        if (!NetworkSudokuPlayer.RemoteWantsRematch()) return;
        if (!RematchRequestedRemote)
        {
            RematchRequestedRemote = true;
            OnRematchStateChanged?.Invoke();
        }
    }

    private void TryBeginRematch()
    {
        if (_rematchStartSent || sceneLoadRequested) return;
        if (!BothWantRematch || Runner == null || !Runner.IsRunning) return;
        if (!Runner.IsSharedModeMasterClient) return;
        BeginRematchMatch();
    }

    private void BeginRematchMatch()
    {
        if (_rematchStartSent || sceneLoadRequested) return;
        if (Runner == null || !Runner.IsRunning || !Runner.IsSharedModeMasterClient) return;

        _rematchStartSent = true;
        sceneLoadRequested = true;
        rematchLobbyRequested = false;
        waitingForOpponent = false;

        int nextLevel = UnityEngine.Random.Range(1, 500);
        if (nextLevel == MatchLevel)
            nextLevel = (MatchLevel % 499) + 1;
        MatchLevel = nextLevel;
        MatchSessionId = Guid.NewGuid().ToString("N");
        CurrentMatchEpoch = UnityEngine.Random.Range(1, int.MaxValue);
        ResetMatchProgress();
        NetworkSudokuPlayer.Local?.ResetForNewMatch(MatchLevel);

        string payload = "start\n" + MatchLevel + "\n" + MatchSessionId + "\n" + CurrentMatchEpoch;
        SendToOthers(RematchKey, Encoding.UTF8.GetBytes(payload));
        FlushFusionClient();
        ClearRematchFlags();

        Runner.LoadScene(SceneRef.FromIndex(
            SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GameScene.unity")));
    }

    private void ApplyRematchStart(string payload)
    {
        string[] parts = payload.Split('\n');
        string incomingId = parts.Length > 2 ? parts[2].Trim() : "";
        if (!string.IsNullOrEmpty(incomingId) && incomingId == MatchSessionId && _matchClockArmed)
            return;

        if (parts.Length > 1 && int.TryParse(parts[1], out int level) && level > 0)
            MatchLevel = level;
        if (!string.IsNullOrEmpty(incomingId))
            MatchSessionId = incomingId;
        if (parts.Length > 3 && int.TryParse(parts[3], out int epoch) && epoch > 0)
            CurrentMatchEpoch = epoch;

        _rematchStartSent = true;
        ResetMatchProgress();
        NetworkSudokuPlayer.Local?.ResetForNewMatch(MatchLevel);
        ClearRematchFlags();
        SudokuGameManager.Instance?.AlignMatchClock(0f);
        if (SudokuGameManager.Instance != null && SudokuGameManager.Instance.IsGameActive)
        {
            _localGameplayReady = true;
            ArmFromElapsed(0f);
        }
    }

    private void RememberRematchTarget()
    {
        if (IsRealPlayerName(OpponentName))
            RematchOpponentName = OpponentName;
        if (OpponentAvatarIndex >= 0)
            RematchOpponentAvatar = OpponentAvatarIndex;
        if (OpponentAvatarSprite != null)
            rematchOpponentAvatarSprite = OpponentAvatarSprite;
    }

    public void LoadRematchLobby()
    {
        if (rematchLobbyRequested) return;
        if (Runner == null || !Runner.IsRunning || !Runner.IsSharedModeMasterClient) return;

        rematchLobbyRequested = true;
        Runner.LoadScene(SceneRef.FromIndex(
            SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/MainMenu.unity")));
    }

    public Sprite GetOpponentAvatar()
    {
        if (OpponentAvatarSprite == null && opponentAvatarPayload != null && opponentAvatarPayload.Length > 32)
            ApplyOpponentAvatarBytes(opponentAvatarPayload, true);
        if (OpponentAvatarSprite != null) return OpponentAvatarSprite;
        if (!string.IsNullOrEmpty(OpponentAvatarUrl))
            return null;
        if (rematchOpponentAvatarSprite != null) return rematchOpponentAvatarSprite;
        if (ProfileManager.Instance == null) return null;
        int index = OpponentAvatarIndex >= 0 ? OpponentAvatarIndex : RematchOpponentAvatar;
        return ProfileManager.Instance.GetPresetAvatar(index);
    }

    private void BeginOpponentAvatarDownload(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        if (url == loadedOpponentAvatarUrl && OpponentAvatarSprite != null)
            return;

        if (opponentAvatarRoutine != null)
            StopCoroutine(opponentAvatarRoutine);
        opponentAvatarRoutine = StartCoroutine(DownloadOpponentAvatar(url));
    }

    private IEnumerator DownloadOpponentAvatar(string url)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                try { request.SetRequestHeader("User-Agent", "Mozilla/5.0"); }
                catch (InvalidOperationException) { }

                yield return request.SendWebRequest();
                if (url != OpponentAvatarUrl)
                {
                    opponentAvatarRoutine = null;
                    yield break;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D downloaded = DownloadHandlerTexture.GetContent(request);
                    Texture2D owned = CopyReadableTexture(downloaded);
                    if (owned != null)
                    {
                        byte[] jpg = owned.EncodeToJPG(90);
                        if (jpg != null && jpg.Length > 32)
                            opponentAvatarPayload = jpg;
                        RememberOpponentAvatar(owned, true);
                        loadedOpponentAvatarUrl = url;
                        opponentAvatarRoutine = null;
                        OnOpponentIdentity?.Invoke();
                        yield break;
                    }
                }

                Debug.LogWarning("[MultiplayerManager] Opponent photo download failed: " + request.error);
            }

            if (attempt == 0)
                yield return new WaitForSecondsRealtime(0.6f);
        }

        opponentAvatarRoutine = null;
    }

    private IEnumerator ConfigureRoomSoon()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        ConfigureRoomPersistence();
        KeepSessionAliveInBackground();
    }

    private void ConfigureRoomPersistence()
    {
        KeepSessionAliveInBackground();
        try
        {
            var room = ResolveFusionClient(Runner)?.CurrentRoom;
            if (room == null) return;
            // Keep the seat through a screen-off so the other phone does not
            // treat the pause as the player leaving the match.
            room.PlayerTtl = 180000;
            room.EmptyRoomTtl = 180000;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Room TTL skipped: {ex.Message}");
        }
    }

    private async void RejoinActiveSession()
    {
        if (_intentionalLeave || _isQuitting || string.IsNullOrEmpty(_sessionName)) return;
        if (isStartingGame || IsInSession) return;
        if (_rejoinAttempts >= 3) return;

        _rejoinAttempts++;
        isStartingGame = true;
        bool retry = false;
        try
        {
            var runner = await CreateNetworkRunner();
            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                Address = NetAddress.Any(),
                SessionName = _sessionName,
                Scene = GetStartGameSceneInfo(),
                PlayerCount = 2,
                CustomPhotonAppSettings = GetPhotonAppSettings(),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>()
            };

            var result = await runner.StartGame(startArgs);
            if (!result.Ok)
            {
                Debug.LogWarning($"[MultiplayerManager] Rejoin failed: {result.ShutdownReason}");
                await ShutdownRunner();
                retry = !_intentionalLeave && !_isQuitting;
            }
            else
            {
                _rejoinAttempts = 0;
                KeepSessionAliveInBackground();
                ConfigureRoomPersistence();
                AnnounceBackground(false);
            }
        }
        finally
        {
            isStartingGame = false;
        }

        if (retry)
            RejoinActiveSession();
    }

    private static Photon.Realtime.RealtimeClient ResolveFusionClient(NetworkRunner runner)
    {
        if (runner == null) return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object services = typeof(NetworkRunner).GetField("_cloudServices", flags)?.GetValue(runner);
        if (services == null) return null;

        object communicator = services.GetType().GetField("_communicator", flags)?.GetValue(services);
        if (communicator == null) return null;

        return communicator.GetType().GetProperty("Client")?.GetValue(communicator) as Photon.Realtime.RealtimeClient;
    }

    private void AcquireMatchWakeLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_matchWakeLock != null) return;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (var power = context.Call<AndroidJavaObject>("getSystemService", "power"))
            {
                // PARTIAL_WAKE_LOCK: CPU stays awake after the screen turns off.
                _matchWakeLock = power.Call<AndroidJavaObject>("newWakeLock", 1, "Sudoku:Match");
                _matchWakeLock.Call("setReferenceCounted", false);
                _matchWakeLock.Call("acquire");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Wake lock skipped: {ex.Message}");
        }
#endif
    }

    private void ReleaseMatchWakeLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_matchWakeLock == null) return;
        try
        {
            if (_matchWakeLock.Call<bool>("isHeld"))
                _matchWakeLock.Call("release");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Wake lock release: {ex.Message}");
        }
        finally
        {
            _matchWakeLock.Dispose();
            _matchWakeLock = null;
        }
#endif
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_intentionalLeave) return;
        if (player == runner.LocalPlayer) return;
        if (_backgrounded)
        {
            _opponentLeftWhileAway = true;
            return;
        }
        OnOpponentLeft?.Invoke();
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        OnConnectionFailed?.Invoke(reason.ToString());
    }

    // SimulationMessagePtr is marked obsolete in this Fusion version, but the
    // interface still declares this method with that exact signature — we
    // have no way to avoid referencing the type, so the obsolete warning is
    // suppressed locally rather than left cluttering the build output.
#pragma warning disable CS0618 // Type or member is obsolete
    void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
#pragma warning restore CS0618

    void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
    {
        if (runner == Runner && IsInSession)
            BroadcastIdentity();
    }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ReadOnlySpan<byte> data)
    {
        if (player == runner.LocalPlayer || data.Length == 0) return;

        int keyId;
        int unusedB;
        int unusedC;
        int unusedD;
        key.GetInts(out keyId, out unusedB, out unusedC, out unusedD);

        if (keyId == 6)
        {
            ApplyOpponentAvatarBytes(data.ToArray());
            return;
        }

        if (keyId == 7)
        {
            NoteOpponentExit();
            return;
        }

        string payload = Encoding.UTF8.GetString(data);

        if (keyId == 1)
        {
            string[] parts = payload.Split('\n');
            bool changed = false;
            if (parts.Length > 0 && IsRealPlayerName(parts[0]))
            {
                string name = parts[0].Trim();
                if (!string.Equals(OpponentName, name, StringComparison.Ordinal))
                {
                    OpponentName = name;
                    changed = true;
                }
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int avatarIndex) && avatarIndex >= -1 && avatarIndex < 64)
            {
                if (OpponentAvatarIndex != avatarIndex)
                {
                    OpponentAvatarIndex = avatarIndex;
                    changed = true;
                }
            }
            if (parts.Length > 3)
            {
                string url = parts[3].Trim();
                if (!string.Equals(OpponentAvatarUrl, url, StringComparison.Ordinal))
                {
                    OpponentAvatarUrl = url;
                    changed = true;
                    BeginOpponentAvatarDownload(url);
                }
            }
            if (parts.Length > 2 && int.TryParse(parts[2], out int profileLevel) && IsSaneProfileLevel(profileLevel))
            {
                // Keep the level that was announced. A later packet may record a
                // single real level-up, and nothing else.
                bool accept = !_opponentLevelLocked
                    || profileLevel == OpponentProfileLevel
                    || profileLevel == OpponentProfileLevel + 1;
                if (accept)
                {
                    if (OpponentProfileLevel != profileLevel)
                    {
                        OpponentProfileLevel = profileLevel;
                        changed = true;
                    }
                    _opponentLevelLocked = true;
                }
            }

            if (changed)
                OnOpponentIdentity?.Invoke();
        }
        else if (keyId == 2)
        {
            string text = payload.Trim();
            if (text.StartsWith("start", StringComparison.Ordinal))
            {
                ApplyRematchStart(text);
                return;
            }

            if (!RematchRequestedRemote)
            {
                RematchRequestedRemote = true;
                OnRematchStateChanged?.Invoke();
            }
            TryBeginRematch();
        }
        else if (keyId == 3)
        {
            OpponentBackgrounded = payload.Trim() == "1";
        }
        else if (keyId == 5)
        {
            _remoteGameplayReady = true;
            TryArmMatchClock();
        }
        else if (keyId == 4)
        {
            string[] parts = payload.Split('\n');
            float incoming = -1f;
            bool frozen = parts.Length > 3 && parts[3].Trim() == "1";
            if (parts.Length > 0 && int.TryParse(parts[0], out int centi))
            {
                incoming = Mathf.Max(0f, centi / 100f);
                AdoptAuthoritativeElapsed(incoming, frozen);
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int score))
                OpponentLiveScore = Mathf.Max(0, score);
            if (parts.Length > 2 && int.TryParse(parts[2], out int hearts))
            {
                OpponentLiveHalfHearts = Mathf.Clamp(hearts, 0, HeartManager.MaxHalfHearts);
                // An explicit frozen 0-life report ends the match. Screen state does not.
                if (hearts <= 0 && frozen && _matchClockArmed && !ResultCaptured && !OpponentOutOfLives)
                {
                    int reportedScore = -1;
                    if (parts.Length > 1 && int.TryParse(parts[1], out int deadScore))
                        reportedScore = Mathf.Max(0, deadScore);
                    NoteOpponentOutOfLives(reportedScore);
                    OnOpponentOutOfLives?.Invoke(incoming >= 0f ? incoming : RemoteDisplayTime);
                }
            }
        }
    }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        bool ours = Runner == runner;
        if (ours) Runner = null;
        if (!ours || _intentionalLeave || _isQuitting || !_backgrounded || !IsMultiplayerGame) return;
        _needsRejoin = true;
    }
}