using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
    /// <summary>This device lost the session and could not rejoin. The match UI must leave with it.</summary>
    public event Action OnLocalSessionLost;
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
    private int _avatarSourceId = int.MinValue;
    private Coroutine _avatarSendRoutine;
    private int _avatarBursts;
    private byte[][] _avatarChunks;
    private int _avatarChunkTotal;
    private int _avatarChunkStamp;
    private Sprite _lastPaintedOpponentAvatar;
    private float _nextOpponentAvatarPaint;
    private bool _opponentSeenInRoom;
    private float _opponentMissingSince = -1f;
    private float _departureConfirmAt = -1f;
    private bool _opponentDepartureNotified;
    private Coroutine _rejoinRoutine;
    private const float OpponentMissingGraceSeconds = 12f;
    private const int SharpAvatarMaxBytes = 48000;
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
    private AndroidJavaObject _wifiLock;

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
            _backgrounded = true;
            KeepSessionAliveInBackground();
            AnnounceBackground(true);
            FlushFusionClient();
            return;
        }

        _backgrounded = false;
        AnnounceBackground(false);
        BroadcastIdentity();
        DeliverAvatarSoon(true);
        FlushMatchClock();
        if (_opponentLeftWhileAway)
        {
            _opponentLeftWhileAway = false;
            if (!OpponentBackgrounded)
                _departureConfirmAt = Time.unscaledTime + OpponentMissingGraceSeconds;
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
            _backgrounded = true;
            KeepSessionAliveInBackground();
            AnnounceBackground(true);
            FlushFusionClient();
            return;
        }

        _backgrounded = false;
        AnnounceBackground(false);
        BroadcastIdentity();
        DeliverAvatarSoon(true);
        FlushMatchClock();
        if (_opponentLeftWhileAway)
        {
            _opponentLeftWhileAway = false;
            if (!OpponentBackgrounded)
                _departureConfirmAt = Time.unscaledTime + OpponentMissingGraceSeconds;
        }
        if (!_needsRejoin) return;
        _needsRejoin = false;
        RejoinActiveSession();
    }

    private void Update()
    {
        if (!IsInSession || _intentionalLeave) return;
        PullPhotonNickName();
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
        WatchOpponentPresence();
        if (Time.unscaledTime < _nextIdentitySend) return;
        _nextIdentitySend = Time.unscaledTime + 3f;
        BroadcastIdentity();
    }

    /// <summary>True while a dropped session is being joined again.</summary>
    public bool IsRejoining => _needsRejoin || _rejoinRoutine != null || (isStartingGame && _rejoinAttempts > 0);

    /// <summary>The other player has left or been removed from this room.</summary>
    public bool OpponentDeparted => _opponentDepartureNotified || OpponentForfeited;

    /// <summary>Both seats are filled and neither side has left.</summary>
    public bool OpponentStillConnected()
    {
        if (!IsInSession || _opponentDepartureNotified || OpponentForfeited) return false;
        return CountActivePlayers() >= 2;
    }

    private int CountActivePlayers()
    {
        if (Runner == null || !Runner.IsRunning) return 0;
        int count = 0;
        foreach (var _ in Runner.ActivePlayers)
            count++;
        return count;
    }

    // A live match must not keep running after the other phone is gone.
    // A few seconds covers a one-frame player-list flicker. An explicit exit
    // still arrives through NoteOpponentExit without waiting.
    private void WatchOpponentPresence()
    {
        if (_opponentDepartureNotified || waitingForOpponent) return;

        if (OpponentBackgrounded)
        {
            _opponentMissingSince = -1f;
            _departureConfirmAt = -1f;
            return;
        }

        if (_departureConfirmAt >= 0f && Time.unscaledTime >= _departureConfirmAt)
        {
            _departureConfirmAt = -1f;
            if (CountActivePlayers() < 2)
                NotifyOpponentDeparted();
        }

        if (CountActivePlayers() >= 2 && !OpponentForfeited)
        {
            _opponentSeenInRoom = true;
            _opponentMissingSince = -1f;
            return;
        }

        if (!_opponentSeenInRoom) return;
        if (OpponentForfeited)
        {
            NotifyOpponentDeparted();
            return;
        }

        if (_opponentMissingSince < 0f)
            _opponentMissingSince = Time.unscaledTime;
        else if (Time.unscaledTime - _opponentMissingSince >= OpponentMissingGraceSeconds)
            NotifyOpponentDeparted();
    }

    private void NotifyOpponentDeparted()
    {
        if (_opponentDepartureNotified || _intentionalLeave) return;
        _opponentDepartureNotified = true;
        _opponentMissingSince = -1f;
        OnOpponentLeft?.Invoke();
    }

    private void FlushMatchClock()
    {
        var match = MultiplayerGameController.Instance;
        if (match == null || !match.IsMatchRunning) return;
        EnsureSharedClock();
        float elapsed = HasSharedClock ? SharedElapsed : 0f;
        NetworkSudokuPlayer.Local?.FlushElapsed(elapsed);
        var game = SudokuGameManager.Instance;
        int score = game != null ? game.SessionScore : 0;
        int hearts = game != null ? game.CurrentHalfHearts : 0;
        PublishLiveStats(elapsed, score, hearts, false, true);
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
            _sessionName = null;
            ClearOpponentIdentity();
            ClearRematchFlags();

            StartGameResult result = default;
            bool joined = false;
            var sessionFilter = new Dictionary<string, SessionProperty>
            {
                { "mode", "intl" },
                { "diff", (int)difficulty }
            };

            // Join any open room for this difficulty. A fixed room name stays
            // full after someone leaves (PlayerTtl), so the next player never
            // lands in the room where someone is still waiting.
            for (int attempt = 0; attempt < 8 && !joined; attempt++)
            {
                var runner = await CreateNetworkRunner();
                var startArgs = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    Address = NetAddress.Any(),
                    SessionName = null,
                    Scene = GetStartGameSceneInfo(),
                    PlayerCount = 2,
                    IsOpen = true,
                    IsVisible = true,
                    MatchmakingMode = Photon.Realtime.MatchmakingMode.FillRoom,
                    SessionProperties = sessionFilter,
                    CustomPhotonAppSettings = GetPhotonAppSettings(),
                    SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
                    ObjectProvider = runner.GetComponent<NetworkObjectProviderDefault>()
                };

                result = await runner.StartGame(startArgs);
                if (!result.Ok)
                {
                    await ShutdownRunner();
                    if (result.ShutdownReason != ShutdownReason.GameIsFull
                        && result.ShutdownReason != ShutdownReason.GameClosed
                        && result.ShutdownReason != ShutdownReason.GameNotFound)
                        break;
                    continue;
                }

                if (Runner != null && Runner.SessionInfo.IsValid && !string.IsNullOrEmpty(Runner.SessionInfo.Name))
                    _sessionName = Runner.SessionInfo.Name;

                PublishPhotonNickName();
                OpenSearchRoom();

                if (RoomBlocksNewOpponent())
                {
                    Debug.LogWarning("[MultiplayerManager] Search room still holds an inactive player. Trying another room.");
                    CloseAndDropRoom();
                    await ShutdownRunner();
                    continue;
                }

                joined = true;
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
        if (!_intentionalLeave && Runner != null && Runner.IsRunning)
        {
            AnnounceLocalExit();
            FlushFusionClient();
            try { await Task.Delay(300); }
            catch (Exception) { }
        }

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
        // Only an empty search times out. A player already in HostWait or a
        // match must stay connected for as long as the session is alive.
        if (!waitingForOpponent || CountActivePlayers() >= 2)
        {
            waitingForOpponent = false;
            yield break;
        }

        waitingForOpponent = false;
        OnMatchmakingTimeout?.Invoke();
        Disconnect();
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
        _opponentSeenInRoom = true;
        _opponentDepartureNotified = false;
        _opponentMissingSince = -1f;
        if (matchmakingTimeoutCoroutine != null)
        {
            StopCoroutine(matchmakingTimeoutCoroutine);
            matchmakingTimeoutCoroutine = null;
        }

        HoldMatchedRoom();
        BroadcastIdentity();
        DeliverAvatarSoon(true);
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
        if (name.Equals("You", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Searching...", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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
        var localPlayer = NetworkSudokuPlayer.Local;
        if (localPlayer != null && localPlayer.Object != null && localPlayer.Object.IsValid && localPlayer.HasStateAuthority
            && IsRealPlayerName(LocalPlayerName))
        {
            try
            {
                if (!string.Equals(localPlayer.PlayerName.ToString(), LocalPlayerName, StringComparison.Ordinal))
                    localPlayer.PlayerName = LocalPlayerName;
            }
            catch { }
        }

        string payload = LocalPlayerName + "\n" + avatar + "\n" + level + "\n" + avatarUrl;
        PublishPhotonNickName();
        SendToOthers(IdentityKey, Encoding.UTF8.GetBytes(payload));
        DeliverAvatarSoon(false);
    }

    private void PublishPhotonNickName()
    {
        if (string.IsNullOrWhiteSpace(LocalPlayerName)) return;
        try
        {
            var local = ResolveFusionClient(Runner)?.LocalPlayer;
            if (local == null) return;
            if (!string.Equals(local.NickName, LocalPlayerName, StringComparison.Ordinal))
                local.NickName = LocalPlayerName;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MultiplayerManager] Nickname skipped: " + ex.Message);
        }
    }

    private void PullPhotonNickName()
    {
        var room = ResolveFusionClient(Runner)?.CurrentRoom;
        if (room == null || room.Players == null) return;

        foreach (var player in room.Players.Values)
        {
            if (player == null || player.IsLocal || player.IsInactive) continue;
            if (!IsRealPlayerName(player.NickName)) continue;

            string nick = player.NickName.Trim();
            if (string.Equals(OpponentName, nick, StringComparison.Ordinal)) return;
            OpponentName = nick;
            OnOpponentIdentity?.Invoke();
            return;
        }
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

    private void DeliverAvatarSoon(bool force = false)
    {
        EnsureAvatarEncoded();
        if (force)
            _avatarBursts = 0;
        if (localAvatarJpeg == null || _avatarSendRoutine != null || _avatarBursts >= 3) return;
        _avatarBursts++;
        _avatarSendRoutine = StartCoroutine(DeliverAvatarImage());
    }

    private IEnumerator DeliverAvatarImage()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            yield return SendAvatarChunks();
            if (pass == 0)
                yield return new WaitForSecondsRealtime(1.5f);
        }

        _avatarSendRoutine = null;
    }

    // One full photo is larger than a single Fusion message, so the portrait
    // was dropped and the other phone kept an empty circle. Small pieces arrive.
    private IEnumerator SendAvatarChunks()
    {
        byte[] jpg = localAvatarJpeg;
        if (jpg == null || jpg.Length < 32 || !IsInSession) yield break;

        const int chunkSize = 480;
        int total = (jpg.Length + chunkSize - 1) / chunkSize;
        for (int i = 0; i < total; i++)
        {
            int offset = i * chunkSize;
            int count = Math.Min(chunkSize, jpg.Length - offset);
            byte[] packet = new byte[count + 3];
            packet[0] = 0xA7;
            packet[1] = (byte)i;
            packet[2] = (byte)total;
            Buffer.BlockCopy(jpg, offset, packet, 3, count);
            SendToOthers(ReliableKey.FromInts(6, i + 1, total, jpg.Length), packet);
            float waited = 0f;
            while (_ackChunk != i + 1 && waited < 0.2f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }

    private void EnsureAvatarEncoded()
    {
        Sprite sprite = ProfileManager.Instance != null ? ProfileManager.Instance.CurrentAvatarSprite : null;
        Texture source = sprite != null ? sprite.texture : null;
        if (source == null) return;

        int sourceId = source.GetInstanceID();
        if (localAvatarJpeg != null && _avatarSourceId == sourceId) return;

        _avatarSourceId = sourceId;
        if (_avatarSendRoutine != null)
        {
            StopCoroutine(_avatarSendRoutine);
            _avatarSendRoutine = null;
        }

        int native = Mathf.Max(source.width, source.height);
        int first = Mathf.Clamp(native, 192, 384);
        localAvatarJpeg = EncodeAvatarThatFits(
            source,
            sprite,
            new[] { first, 320, 256, 192 },
            new[] { 85, 75, 65 },
            SharpAvatarMaxBytes);
    }

    private void SendAvatarImage()
    {
        DeliverAvatarSoon(true);
    }

    private byte[] EncodeAvatarThatFits(Texture source, Sprite sprite, int[] sizes, int[] qualities, int maxBytes)
    {
        for (int s = 0; s < sizes.Length; s++)
        {
            for (int q = 0; q < qualities.Length; q++)
            {
                byte[] jpg = EncodeAvatarJpeg(source, sprite, sizes[s], qualities[q]);
                if (jpg != null && jpg.Length > 32 && jpg.Length <= maxBytes)
                    return jpg;
            }
        }
        return null;
    }

    private byte[] EncodeAvatarJpeg(Texture source, Sprite sprite, int size, int quality)
    {
        if (source is Texture2D readableSource && readableSource.isReadable)
        {
            byte[] fromPixels = EncodeReadableSprite(readableSource, sprite, size, quality);
            if (fromPixels != null && fromPixels.Length > 32)
                return fromPixels;
        }

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

    private byte[] EncodeReadableSprite(Texture2D texture, Sprite sprite, int size, int quality)
    {
        Rect area = sprite != null ? sprite.textureRect : new Rect(0, 0, texture.width, texture.height);
        int x = Mathf.Clamp(Mathf.FloorToInt(area.x), 0, Mathf.Max(0, texture.width - 1));
        int y = Mathf.Clamp(Mathf.FloorToInt(area.y), 0, Mathf.Max(0, texture.height - 1));
        int w = Mathf.Clamp(Mathf.RoundToInt(area.width), 1, texture.width - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(area.height), 1, texture.height - y);

        Color[] pixels;
        try
        {
            pixels = texture.GetPixels(x, y, w, h);
        }
        catch (Exception)
        {
            return null;
        }

        Texture2D crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
        crop.SetPixels(pixels);
        crop.Apply(false, false);

        RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D scaled = null;
        try
        {
            Graphics.Blit(crop, rt);
            RenderTexture.active = rt;
            scaled = new Texture2D(size, size, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            scaled.Apply(false, false);
            return scaled.EncodeToJPG(quality);
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
            Destroy(crop);
            if (scaled != null)
                Destroy(scaled);
        }
    }

    public void PublishCachedAvatar()
    {
        if (localAvatarThumbnail != null)
            NetworkSudokuPlayer.Local?.PublishAvatarJpeg(localAvatarThumbnail);
    }

    private void AcceptAvatarChunk(int indexFromKey, int totalFromKey, int stamp, byte[] raw)
    {
        if (raw == null || raw.Length < 2) return;

        int index;
        int total;
        byte[] piece;
        if (raw[0] == 0xA7 && raw.Length >= 4)
        {
            index = raw[1];
            total = raw[2];
            piece = new byte[raw.Length - 3];
            Buffer.BlockCopy(raw, 3, piece, 0, piece.Length);
        }
        else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xD8)
        {
            ApplyOpponentAvatarBytes(raw, true);
            return;
        }
        else
        {
            index = indexFromKey;
            total = totalFromKey > 0 ? totalFromKey : 1;
            piece = raw;
        }

        if (total <= 1)
        {
            ApplyOpponentAvatarBytes(piece, true);
            return;
        }
        if (total > 128 || index < 0 || index >= total) return;

        int imageStamp = stamp != 0 ? stamp : total;
        if (_avatarChunkStamp != imageStamp || _avatarChunks == null || _avatarChunkTotal != total)
        {
            _avatarChunkStamp = imageStamp;
            _avatarChunkTotal = total;
            _avatarChunks = new byte[total][];
        }

        _avatarChunks[index] = piece;
        int length = 0;
        for (int i = 0; i < total; i++)
        {
            if (_avatarChunks[i] == null) return;
            length += _avatarChunks[i].Length;
        }

        byte[] jpg = new byte[length];
        int write = 0;
        for (int i = 0; i < total; i++)
        {
            Buffer.BlockCopy(_avatarChunks[i], 0, jpg, write, _avatarChunks[i].Length);
            write += _avatarChunks[i].Length;
        }

        ApplyOpponentAvatarBytes(jpg, true);
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
        if (OpponentAvatarSprite != null && pixels <= opponentAvatarPixels)
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
        sprite.name = "OpponentProfile";
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
        PaintOpponentAvatars();
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
            try
            {
                Runner.SendReliableDataToPlayer(other, key, payload);
                sent = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiplayerManager] Send skipped: " + ex.Message);
            }
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
        NotifyOpponentDeparted();
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
        _avatarChunks = null;
        _avatarChunkTotal = 0;
        _avatarChunkStamp = 0;
        _lastPaintedOpponentAvatar = null;
        _nextOpponentAvatarPaint = 0f;
        seenAvatarCount = -1;
        seenAvatarChecksum = 0;
        opponentAvatarPixels = 0;
        opponentAvatarIsOriginal = false;
        _opponentSeenInRoom = false;
        _opponentMissingSince = -1f;
        _opponentDepartureNotified = false;
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
        if (!OpponentStillConnected()) return;

        sceneLoadRequested = true;
        rematchLobbyRequested = false;
        waitingForOpponent = false;
        ClearRematchFlags();
        SealMatchRoom();
        PreserveDisplayedOpponentAvatar();

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
        if (!OpponentStillConnected()) return;
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
        PreserveDisplayedOpponentAvatar();

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

    /// <summary>
    /// The portrait already on SearchingGroup or HostWaitGroup is the one gameplay must reuse.
    /// </summary>
    public void PreserveDisplayedOpponentAvatar()
    {
        if (OpponentAvatarSprite != null && !IsPlaceholderAvatar(OpponentAvatarSprite))
            return;

        Image[] images = Resources.FindObjectsOfTypeAll<Image>();
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.sprite == null || !image.gameObject.scene.IsValid()) continue;
            if (image.gameObject.name != "OpponentAvatarImage") continue;
            if (IsPlaceholderAvatar(image.sprite)) continue;
            OpponentAvatarSprite = image.sprite;
            if (image.sprite.texture is Texture2D texture)
                opponentAvatarTexture = texture;
            return;
        }
    }

    public static bool IsPlaceholderAvatar(Sprite sprite)
    {
        if (sprite == null) return true;
        string name = sprite.name ?? "";
        return name.IndexOf("SearchingAvatar", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public Sprite GetOpponentAvatar()
    {
        if (OpponentAvatarSprite == null || OpponentAvatarSprite.texture == null)
        {
            if (opponentAvatarPayload != null && opponentAvatarPayload.Length > 32)
                ApplyOpponentAvatarBytes(opponentAvatarPayload, true);
        }
        if (OpponentAvatarSprite != null) return OpponentAvatarSprite;
        // An account photo is still downloading. Keep the circle empty until
        // that photo is cached, instead of swapping in a preset.
        if (!string.IsNullOrEmpty(OpponentAvatarUrl) || OpponentAvatarIndex < 0)
            return null;
        if (rematchOpponentAvatarSprite != null) return rematchOpponentAvatarSprite;
        if (ProfileManager.Instance == null) return null;
        int index = OpponentAvatarIndex >= 0 ? OpponentAvatarIndex : RematchOpponentAvatar;
        return ProfileManager.Instance.GetPresetAvatar(index);
    }

    /// <summary>
    /// Writes the cached opponent portrait onto every opponent avatar Image,
    /// including OpponentAvatarImage. The local player's portrait is left alone.
    /// </summary>
    public void PaintOpponentAvatars()
    {
        Sprite sprite = OpponentAvatarSprite;
        if (sprite == null) return;
        if (sprite == _lastPaintedOpponentAvatar && Time.unscaledTime < _nextOpponentAvatarPaint)
            return;
        _lastPaintedOpponentAvatar = sprite;
        _nextOpponentAvatarPaint = Time.unscaledTime + 0.4f;

        Image[] images = Resources.FindObjectsOfTypeAll<Image>();
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || !image.gameObject.scene.IsValid()) continue;
            if (!IsOpponentPortraitTarget(image.transform)) continue;
            PaintPortrait(image, sprite);
        }
    }

    private static bool IsOpponentPortraitTarget(Transform image)
    {
        if (image.name == "OpponentAvatarImage") return true;

        bool underOpponent = false;
        bool underLocal = false;
        Transform cursor = image;
        while (cursor != null)
        {
            if (cursor.name == "Opponent" || cursor.name == "OpponentSide")
                underOpponent = true;
            if (cursor.name == "Player" || cursor.name == "PlayerSide")
                underLocal = true;
            cursor = cursor.parent;
        }

        if (underLocal && !underOpponent) return false;
        if (!underOpponent) return false;
        return image.name == "Avatar" || image.name == "AvatarImage";
    }

    /// <summary>
    /// Puts the cached opponent photo on the Image that is already in the scene.
    /// Does not create a Circle Mask, an Avatar Image, or any other object.
    /// </summary>
    public static void AssignExistingOpponentImage(Image image, Sprite sprite)
    {
        if (image == null || sprite == null) return;
        if (IsPlaceholderAvatar(sprite) && image.sprite != null && !IsPlaceholderAvatar(image.sprite))
            return;
        image.sprite = sprite;
        image.enabled = true;
        image.color = Color.white;
        for (int i = 0; i < image.transform.childCount; i++)
        {
            Transform child = image.transform.GetChild(i);
            if (child.name == "CircleSpinner" || child.name == "CircleLoading")
                child.gameObject.SetActive(false);
        }
    }

    private static void PaintPortrait(Image image, Sprite sprite)
    {
        AssignExistingOpponentImage(image, sprite);
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
                        int pixels = owned.width * owned.height;
                        bool better = OpponentAvatarSprite == null || pixels >= opponentAvatarPixels;
                        if (better)
                        {
                            byte[] jpg = owned.EncodeToJPG(90);
                            if (jpg != null && jpg.Length > 32)
                                opponentAvatarPayload = jpg;
                            RememberOpponentAvatar(owned, true);
                            OnOpponentIdentity?.Invoke();
                        }
                        else
                        {
                            Destroy(owned);
                        }

                        loadedOpponentAvatarUrl = url;
                        opponentAvatarRoutine = null;
                        yield break;
                    }
                }

                Debug.LogWarning("[MultiplayerManager] Opponent photo download failed: " + request.error);
            }

            if (attempt == 0)
                yield return new WaitForSecondsRealtime(0.6f);
        }

        opponentAvatarRoutine = null;
        if (OpponentAvatarSprite == null)
        {
            string fallback = url.Replace("=s1024-c", "=s512-c");
            if (!string.Equals(fallback, url, StringComparison.Ordinal))
                yield return DownloadOpponentAvatar(fallback);
        }
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
        if (waitingForOpponent)
            OpenSearchRoom();
        else if (!sceneLoadRequested)
            HoldMatchedRoom();
        else
            SealMatchRoom();
    }

    // A search room must drop a player the moment they cancel. A long PlayerTtl
    // keeps that seat for minutes, Photon reports the room as full, and the
    // other phone can never join the person who is still waiting.
    private void OpenSearchRoom()
    {
        try
        {
            var room = ResolveFusionClient(Runner)?.CurrentRoom;
            if (room == null) return;
            room.PlayerTtl = 0;
            room.EmptyRoomTtl = 0;
            room.IsOpen = true;
            room.IsVisible = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Search room setup skipped: {ex.Message}");
        }
    }

    // Both players are in the room. Keep the seat through a short drop so a
    // stall does not delete them, but hide the room from new matchmaking.
    private void HoldMatchedRoom()
    {
        try
        {
            var room = ResolveFusionClient(Runner)?.CurrentRoom;
            if (room == null) return;
            room.PlayerTtl = 3600000;
            room.EmptyRoomTtl = 60000;
            room.IsOpen = true;
            room.IsVisible = false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Matched room setup skipped: {ex.Message}");
        }
    }

    private void SealMatchRoom()
    {
        try
        {
            var room = ResolveFusionClient(Runner)?.CurrentRoom;
            if (room == null) return;
            room.IsOpen = false;
            room.IsVisible = false;
            // The seat stays through a locked screen. The match ends when a
            // player leaves, not when the display turns off.
            room.PlayerTtl = 3600000;
            room.EmptyRoomTtl = 60000;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Room TTL skipped: {ex.Message}");
        }
    }

    private bool RoomBlocksNewOpponent()
    {
        if (Runner == null || !Runner.IsRunning) return true;

        int active = 0;
        foreach (var _ in Runner.ActivePlayers) active++;
        if (active >= 2) return false;

        var room = ResolveFusionClient(Runner)?.CurrentRoom;
        if (room == null || room.Players == null) return false;

        int inactive = 0;
        foreach (var player in room.Players.Values)
        {
            if (player != null && player.IsInactive)
                inactive++;
        }

        int max = room.MaxPlayers > 0 ? room.MaxPlayers : MaxPlayersPerRoom;
        return inactive > 0 && active + inactive >= max;
    }

    private void CloseAndDropRoom()
    {
        try
        {
            var room = ResolveFusionClient(Runner)?.CurrentRoom;
            if (room == null) return;
            room.IsOpen = false;
            room.IsVisible = false;
            room.PlayerTtl = 0;
            room.EmptyRoomTtl = 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Room close skipped: {ex.Message}");
        }
    }

    private async void RejoinActiveSession()
    {
        if (_intentionalLeave || _isQuitting || string.IsNullOrEmpty(_sessionName)) return;
        if (isStartingGame || IsInSession) return;
        if (_rejoinAttempts >= 3)
        {
            FailLocalSession();
            return;
        }

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
                retry = _rejoinAttempts < 3 && !_intentionalLeave && !_isQuitting;
                if (!retry)
                    FailLocalSession();
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
            {
                using (var power = context.Call<AndroidJavaObject>("getSystemService", "power"))
                {
                    // PARTIAL_WAKE_LOCK: CPU stays awake after the screen turns off.
                    _matchWakeLock = power.Call<AndroidJavaObject>("newWakeLock", 1, "Sudoku:Match");
                    _matchWakeLock.Call("setReferenceCounted", false);
                    _matchWakeLock.Call("acquire");
                }

                using (var wifi = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    if (wifi != null)
                    {
                        _wifiLock = wifi.Call<AndroidJavaObject>("createWifiLock", 3, "Sudoku:MatchWifi");
                        _wifiLock.Call("setReferenceCounted", false);
                        _wifiLock.Call("acquire");
                    }
                }
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
        if (_matchWakeLock == null && _wifiLock == null) return;
        try
        {
            if (_matchWakeLock != null && _matchWakeLock.Call<bool>("isHeld"))
                _matchWakeLock.Call("release");
            if (_wifiLock != null && _wifiLock.Call<bool>("isHeld"))
                _wifiLock.Call("release");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MultiplayerManager] Wake lock release: {ex.Message}");
        }
        finally
        {
            if (_matchWakeLock != null)
            {
                _matchWakeLock.Dispose();
                _matchWakeLock = null;
            }
            if (_wifiLock != null)
            {
                _wifiLock.Dispose();
                _wifiLock = null;
            }
        }
#endif
    }

    private void FailLocalSession()
    {
        if (_intentionalLeave || _isQuitting) return;
        _needsRejoin = false;
        _intentionalLeave = true;
        IsMultiplayerGame = false;
        waitingForOpponent = false;
        OnLocalSessionLost?.Invoke();
    }

    private IEnumerator RejoinAfterDrop()
    {
        yield return null;
        _rejoinRoutine = null;
        if (!_needsRejoin || _intentionalLeave || _isQuitting) yield break;
        _needsRejoin = false;
        RejoinActiveSession();
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
        if (waitingForOpponent && !_opponentSeenInRoom) return;
        if (OpponentBackgrounded)
            return;
        _departureConfirmAt = Time.unscaledTime + OpponentMissingGraceSeconds;
    }

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        if (runner != Runner || _intentionalLeave || _isQuitting) return;
        Debug.LogWarning($"[MultiplayerManager] Disconnected from server: {reason}");
    }
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
        {
            BroadcastIdentity();
            DeliverAvatarSoon(true);
        }
    }
    void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
    {
        if (runner == Runner && !waitingForOpponent)
            SealMatchRoom();
    }
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
            AcceptAvatarChunk(unusedB, unusedC, unusedD, data.ToArray());
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
                string url = ProfileManager.HighResolutionPhotoUrl(parts[3].Trim());
                if (!string.IsNullOrEmpty(url) && !string.Equals(OpponentAvatarUrl, url, StringComparison.Ordinal))
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
            if (OpponentBackgrounded)
            {
                _departureConfirmAt = -1f;
                _opponentMissingSince = -1f;
            }
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
    private int _ackChunk = -1;

    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        if (progress < 1f) return;
        key.GetInts(out int keyId, out int index, out _, out _);
        if (keyId == 6)
            _ackChunk = index;
    }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        bool ours = Runner == runner;
        if (ours) Runner = null;
        if (!ours || _intentionalLeave || _isQuitting || isStartingGame) return;
        if (!IsMultiplayerGame || string.IsNullOrEmpty(_sessionName)) return;
        _needsRejoin = true;
        if (_backgrounded) return;
        if (_rejoinRoutine == null)
            _rejoinRoutine = StartCoroutine(RejoinAfterDrop());
    }
}