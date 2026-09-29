using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
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

    // ---- State ----
    public NetworkRunner Runner { get; private set; }
    public bool IsInSession => Runner != null && Runner.IsRunning;
    public bool IsMultiplayerGame { get; private set; }
    public UIManager.Difficulty MatchDifficulty { get; private set; }
    public int MatchLevel { get; private set; } = 1;
    public bool IsHost { get; private set; }

    public void SetMatchLevel(int level)
    {
        if (level > 0) MatchLevel = level;
    }

    public string LocalPlayerName { get; private set; } = "Player";
    public string OpponentName { get; private set; }
    public int OpponentAvatarIndex { get; private set; } = -1;
    public int OpponentProfileLevel { get; private set; } = 1;
    public bool RematchRequestedLocal { get; private set; }
    public bool RematchRequestedRemote { get; private set; }
    public bool WantsRematchLobby => RematchRequestedLocal && IsInSession;
    public bool BothWantRematch => RematchRequestedLocal && RematchRequestedRemote;

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
    private float _opponentClockReceivedAt = -1f;
    private float _smoothFloor;

    public float OpponentElapsedSeconds { get; private set; }
    public int OpponentLiveScore { get; private set; }
    public int OpponentLiveHalfHearts { get; private set; } = HeartManager.MaxHalfHearts;
    public bool OpponentBackgrounded { get; private set; }

    /// <summary>
    /// True while this device is in the background. Opponent-left events during
    /// that window are our own link pausing, not the other player quitting.
    /// </summary>
    public bool SuppressOpponentLeft => _backgrounded && !_intentionalLeave;

    public float OpponentElapsedSmooth
    {
        get
        {
            float value = OpponentElapsedSeconds;
            if (_opponentClockReceivedAt >= 0f)
                value += Mathf.Clamp(Time.unscaledTime - _opponentClockReceivedAt, 0f, 1.25f);

            if (value + 1f < _smoothFloor)
                _smoothFloor = value;
            else if (value > _smoothFloor)
                _smoothFloor = value;
            return _smoothFloor;
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
        if (_opponentLeftWhileAway)
        {
            _opponentLeftWhileAway = false;
            OnOpponentLeft?.Invoke();
        }
        if (!_needsRejoin) return;
        _needsRejoin = false;
        RejoinActiveSession();
    }

    /// <summary>
    /// Pushes the live clock, score, and hearts to the opponent.
    /// Throttled; score and hearts still replicate on their own networked fields.
    /// </summary>
    public void PublishLiveStats(float elapsed, int score, int halfHearts)
    {
        if (Runner == null || !Runner.IsRunning || elapsed < 0f) return;

        int centi = Mathf.FloorToInt(elapsed * 100f);
        bool sameSecond = _lastClockCenti >= 0 && centi / 100 == _lastClockCenti / 100;
        if (sameSecond && Time.unscaledTime < _nextClockSend) return;

        _lastClockCenti = centi;
        _nextClockSend = Time.unscaledTime + 0.2f;
        string payload = centi + "\n" + score + "\n" + halfHearts;
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
            local.PlayerName = LocalPlayerName;
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
                Debug.LogError($"[MultiplayerManager] StartInternational StartGame failed. ShutdownReason: {result.ShutdownReason}");
                IsMultiplayerGame = false;
                OnConnectionFailed?.Invoke(result.ShutdownReason.ToString());
                await ShutdownRunner();
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
        int playerCount = 0;
        foreach (var _ in runner.ActivePlayers) playerCount++;

        if (playerCount >= 2 && waitingForOpponent)
        {
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
    }

    private IEnumerator ResendIdentity()
    {
        yield return new WaitForSecondsRealtime(0.4f);
        BroadcastIdentity();
        yield return new WaitForSecondsRealtime(1f);
        BroadcastIdentity();
        identityResendCoroutine = null;
    }

    private void BroadcastIdentity()
    {
        if (Runner == null) return;

        int avatar = ProfileManager.Instance != null ? ProfileManager.Instance.AvatarPresetIndex : -1;
        int level = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileLevel : 1;
        string payload = LocalPlayerName + "\n" + avatar + "\n" + level;
        SendToOthers(IdentityKey, Encoding.UTF8.GetBytes(payload));
    }

    private void BroadcastRematch()
    {
        SendToOthers(RematchKey, Encoding.UTF8.GetBytes("1"));
    }

    private void SendToOthers(ReliableKey key, byte[] payload)
    {
        if (Runner == null || payload == null) return;

        foreach (var other in Runner.ActivePlayers)
        {
            if (other == Runner.LocalPlayer) continue;
            Runner.SendReliableDataToPlayer(other, key, payload);
        }
    }

    private void ClearOpponentIdentity()
    {
        OpponentName = null;
        OpponentAvatarIndex = -1;
        OpponentProfileLevel = 1;
        OpponentElapsedSeconds = 0f;
        OpponentLiveScore = 0;
        OpponentLiveHalfHearts = HeartManager.MaxHalfHearts;
        OpponentBackgrounded = false;
        _opponentClockReceivedAt = -1f;
        _smoothFloor = 0f;
    }

    private void ClearRematchFlags()
    {
        RematchRequestedLocal = false;
        RematchRequestedRemote = false;
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
        RematchRequestedLocal = true;
        awaitHostStart = true;
        sceneLoadRequested = false;
        BroadcastRematch();
        OnRematchStateChanged?.Invoke();
        if (BothWantRematch)
            LoadRematchLobby();
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
        if (ProfileManager.Instance == null) return null;
        return ProfileManager.Instance.GetPresetAvatar(OpponentAvatarIndex);
    }

    private IEnumerator ConfigureRoomSoon()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        ConfigureRoomPersistence();
        KeepSessionAliveInBackground();
    }

    private void ConfigureRoomPersistence()
    {
        // Seat lifetime stays at Fusion's default so a real app exit removes
        // the player. Screen-off keeps the socket alive instead of relying on TTL.
        KeepSessionAliveInBackground();
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
    void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
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

        string payload = Encoding.UTF8.GetString(data);

        if (keyId == 1)
        {
            string[] parts = payload.Split('\n');
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                OpponentName = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1], out int avatarIndex))
                OpponentAvatarIndex = avatarIndex;
            if (parts.Length > 2 && int.TryParse(parts[2], out int profileLevel))
                OpponentProfileLevel = Mathf.Max(1, profileLevel);

            OnOpponentIdentity?.Invoke();
        }
        else if (keyId == 2)
        {
            RematchRequestedRemote = true;
            OnRematchStateChanged?.Invoke();
        }
        else if (keyId == 3)
        {
            OpponentBackgrounded = payload.Trim() == "1";
        }
        else if (keyId == 4)
        {
            string[] parts = payload.Split('\n');
            if (parts.Length > 0 && int.TryParse(parts[0], out int centi))
            {
                OpponentElapsedSeconds = Mathf.Max(0f, centi / 100f);
                _opponentClockReceivedAt = Time.unscaledTime;
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int score))
                OpponentLiveScore = Mathf.Max(0, score);
            if (parts.Length > 2 && int.TryParse(parts[2], out int hearts))
                OpponentLiveHalfHearts = Mathf.Clamp(hearts, 0, HeartManager.MaxHalfHearts);
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