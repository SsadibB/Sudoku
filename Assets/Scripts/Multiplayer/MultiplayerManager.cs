using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

/// <summary>
/// Singleton (DontDestroyOnLoad) that owns the Photon Fusion NetworkRunner.
/// Handles International matchmaking and Competition room create/join.
/// </summary>
public class MultiplayerManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static MultiplayerManager Instance { get; private set; }

    // ---- Events ----
    public event Action OnConnectedToRoom;
    public event Action<string> OnConnectionFailed;
    public event Action<string> OnRoomCodeGenerated;       // Competition host: roomCode
    public event Action OnOpponentJoined;
    public event Action OnOpponentLeft;
    public event Action OnMatchmakingTimeout;

    // ---- State ----
    public NetworkRunner Runner { get; private set; }
    public bool IsInSession => Runner != null && Runner.IsRunning;
    public bool IsMultiplayerGame { get; private set; }
    public UIManager.Difficulty MatchDifficulty { get; private set; }
    public bool IsHost { get; private set; }

    public string LocalPlayerName { get; private set; } = "Player";

    private const float MatchmakingTimeoutSeconds = 60f;
    private const int MaxPlayersPerRoom = 2;

    private Coroutine matchmakingTimeoutCoroutine;
    private bool waitingForOpponent;

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
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        RefreshLocalPlayerName();
    }

    private void RefreshLocalPlayerName()
    {
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

    // ====================================================================
    //  PUBLIC API
    // ====================================================================

    /// <summary>Start International matchmaking for the given difficulty.</summary>
    public async void StartInternational(UIManager.Difficulty difficulty)
    {
        RefreshLocalPlayerName();
        MatchDifficulty = difficulty;
        IsHost = false;
        IsMultiplayerGame = true;
        waitingForOpponent = true;

        if (Runner != null) await ShutdownRunner();
        Runner = gameObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;

        // Custom room properties used for matchmaking filter
        var sessionProps = new Dictionary<string, SessionProperty>
        {
            { "Difficulty", (int)difficulty },
            { "Mode", 0 } // 0 = International
        };

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = null, // Let Photon pick an existing room
            PlayerCount = MaxPlayersPerRoom,
            SessionProperties = sessionProps,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await Runner.StartGame(startArgs);
        if (!result.Ok)
        {
            IsMultiplayerGame = false;
            OnConnectionFailed?.Invoke(result.ShutdownReason.ToString());
            return;
        }

        // Start timeout coroutine — fires OnMatchmakingTimeout after 60 s
        // if we still haven't seen a second player
        if (matchmakingTimeoutCoroutine != null) StopCoroutine(matchmakingTimeoutCoroutine);
        matchmakingTimeoutCoroutine = StartCoroutine(MatchmakingTimeoutRoutine());
    }

    /// <summary>Create a Competition room as host. Returns the 6-char room code.</summary>
    public async Task<string> CreateCompetitionRoom(UIManager.Difficulty difficulty)
    {
        RefreshLocalPlayerName();
        MatchDifficulty = difficulty;
        IsHost = true;
        IsMultiplayerGame = true;
        waitingForOpponent = true;

        string roomCode = GenerateRoomCode();

        if (Runner != null) await ShutdownRunner();
        Runner = gameObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;

        var sessionProps = new Dictionary<string, SessionProperty>
        {
            { "Difficulty", (int)difficulty },
            { "Mode", 1 },          // 1 = Competition
            { "RoomCode", roomCode }
        };

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = "COMP_" + roomCode,
            PlayerCount = MaxPlayersPerRoom,
            SessionProperties = sessionProps,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await Runner.StartGame(startArgs);
        if (!result.Ok)
        {
            IsMultiplayerGame = false;
            OnConnectionFailed?.Invoke(result.ShutdownReason.ToString());
            return null;
        }

        OnRoomCodeGenerated?.Invoke(roomCode);
        return roomCode;
    }

    /// <summary>Join an existing Competition room by room code (guest).</summary>
    public async void JoinCompetitionRoom(string roomCode)
    {
        RefreshLocalPlayerName();
        IsHost = false;
        IsMultiplayerGame = true;

        if (Runner != null) await ShutdownRunner();
        Runner = gameObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;

        var startArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = "COMP_" + roomCode.ToUpper().Trim(),
            PlayerCount = MaxPlayersPerRoom,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await Runner.StartGame(startArgs);
        if (!result.Ok)
        {
            IsMultiplayerGame = false;
            OnConnectionFailed?.Invoke("Room not found or full.");
            return;
        }

        // Grab the difficulty from the room's session properties
        if (Runner.SessionInfo.Properties.TryGetValue("Difficulty", out SessionProperty diffProp))
        {
            MatchDifficulty = (UIManager.Difficulty)(int)diffProp;
        }

        OnConnectedToRoom?.Invoke();
    }

    /// <summary>Disconnect from Photon and clean up.</summary>
    public async void Disconnect()
    {
        IsMultiplayerGame = false;
        waitingForOpponent = false;
        if (matchmakingTimeoutCoroutine != null)
        {
            StopCoroutine(matchmakingTimeoutCoroutine);
            matchmakingTimeoutCoroutine = null;
        }
        if (Runner != null) await ShutdownRunner();
    }

    // ====================================================================
    //  PRIVATE HELPERS
    // ====================================================================

    private async Task ShutdownRunner()
    {
        if (Runner == null) return;
        await Runner.Shutdown();
        Destroy(Runner);
        Runner = null;
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

            // If this is NOT the local player joining, it's the opponent
            if (player != runner.LocalPlayer)
            {
                OnOpponentJoined?.Invoke();

                // Difficulty is confirmed from session — load the game scene
                // Give a brief moment so both players get the callback
                StartCoroutine(LoadGameSceneNextFrame());
            }
        }
    }

    private IEnumerator LoadGameSceneNextFrame()
    {
        yield return new WaitForSeconds(0.5f);
        // Only the Shared-mode "State Authority" (first player / host-equivalent)
        // drives scene load; others are loaded by Fusion's scene manager.
        if (Runner != null && Runner.IsSharedModeMasterClient)
        {
            Runner.LoadScene(SceneRef.FromIndex(
                SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GameScene.unity")));
        }
    }

    void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (player != runner.LocalPlayer)
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
    void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ReadOnlySpan<byte> data) { }
    void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
}