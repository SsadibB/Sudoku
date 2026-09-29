using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// NetworkBehaviour spawned once per player in the shared Fusion session.
/// Holds all state that needs to be visible to the other player:
/// - How many cells are completed
/// - The full 81-cell board snapshot (for the opponent board panel)
/// - Whether this player has finished
/// - Their finish time
/// - Their display name
/// </summary>
public class NetworkSudokuPlayer : NetworkBehaviour
{
    // How many cells this player has correctly placed (0–81)
    [Networked] public int CompletedCells { get; set; }

    // 81 bytes: index = row*9+col, value = 0-9 (0 = empty)
    [Networked, Capacity(81)]
    public NetworkArray<byte> BoardSnapshot { get; }

    [Networked] public NetworkBool IsFinished { get; set; }
    [Networked] public float FinishTime { get; set; }
    [Networked] public NetworkBool HasForfeited { get; set; }

    // Live session score, mirrors SudokuGameManager.SessionScore
    [Networked] public int Score { get; set; }

    // Live half-heart count (0-6), mirrors HeartManager.CurrentHalfHearts
    [Networked] public int HalfHearts { get; set; }

    public static event System.Action<NetworkSudokuPlayer> OnPlayerForfeited;

    // Synchronized puzzle level for multiplayer (set by Master Client)
    [Networked] public int SharedPuzzleLevel { get; set; }

    // Player name (max 32 chars)
    [Networked] public NetworkString<_32> PlayerName { get; set; }

    // Live match clock, profile level, and preset avatar index for the HUD.
    // ElapsedWholeSeconds is an int so it replicates the same way score and lives do.
    [Networked] public float ElapsedTime { get; set; }
    [Networked] public int ElapsedWholeSeconds { get; set; }
    [Networked] public NetworkBool IsBackgrounded { get; set; }
    [Networked] public int ProfileLevel { get; set; }
    [Networked] public int AvatarIndex { get; set; }

    // Proxies coast the last whole-second snapshot so the HUD ticks between packets.
    private int _seenWholeSeconds = -1;
    private float _seenWholeAt;

    public float VisibleElapsed
    {
        get
        {
            if (HasStateAuthority)
            {
                float live = ReadLocalElapsed();
                if (live >= 0f) return live;
            }

            int whole = 0;
            float networkedFloat = 0f;
            try
            {
                whole = ElapsedWholeSeconds;
                networkedFloat = ElapsedTime;
            }
            catch
            {
                // State buffer can be unread while the object is spawning.
            }

            if (whole != _seenWholeSeconds)
            {
                _seenWholeSeconds = whole;
                _seenWholeAt = Time.unscaledTime;
            }

            float coast = _seenWholeSeconds > 0
                ? Mathf.Clamp(Time.unscaledTime - _seenWholeAt, 0f, 1.25f)
                : 0f;
            return Mathf.Max(networkedFloat, whole + coast);
        }
    }

    // ---- Static lookup ----
    // Allows any script to quickly get the local or remote player object
    public static NetworkSudokuPlayer Local { get; private set; }
    public static NetworkSudokuPlayer Remote { get; private set; }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            Local = this;
            // Set player name from MultiplayerManager
            string name = MultiplayerManager.Instance != null
                ? MultiplayerManager.Instance.LocalPlayerName
                : "Player";
            PlayerName = new NetworkString<_32>(name);

            // Start with full hearts and zero score until the game reports otherwise
            HalfHearts = HeartManager.MaxHalfHearts;
            Score = 0;
            ElapsedTime = 0f;

            if (ProfileManager.Instance != null)
            {
                ProfileLevel = ProfileManager.Instance.ProfileLevel;
                AvatarIndex = ProfileManager.Instance.AvatarPresetIndex;
            }

            // If Master Client, publish the match level
            if (Runner.IsSharedModeMasterClient && MultiplayerManager.Instance != null)
            {
                SharedPuzzleLevel = MultiplayerManager.Instance.MatchLevel;
            }
        }
        else
        {
            Remote = this;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (HasStateAuthority) Local = null;
        else Remote = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        float elapsed = ReadLocalElapsed();
        if (elapsed < 0f) return;
        PublishElapsed(elapsed);
    }

    // ---- Called by MultiplayerGameController ----

    /// <summary>Record a correct cell placement (state-authority only).</summary>
    public void RecordCorrectCell(int row, int col, byte value)
    {
        RecordCellChange(row, col, value, true);
    }

    /// <summary>
    /// Record any cell change: value (0=cleared, 1-9=number) and whether it's correct.
    /// Encodes value in bits 0-3, and bit 4 (0x10) indicates an incorrect user attempt.
    /// </summary>
    public void RecordCellChange(int row, int col, byte value, bool isCorrect)
    {
        if (!HasStateAuthority) return;

        byte encoded = (byte)(value & 0x0F);
        if (value > 0 && !isCorrect)
        {
            encoded |= 0x10; // Bit 4 set: wrong user guess
        }
        BoardSnapshot.Set(row * 9 + col, encoded);

        // Recalculate completed correct cells
        int count = 0;
        for (int i = 0; i < 81; i++)
        {
            byte b = BoardSnapshot[i];
            if ((b & 0x0F) > 0 && (b & 0x10) == 0)
                count++;
        }
        CompletedCells = count;
    }

    /// <summary>Sync the initial puzzle fixed cells into the snapshot.</summary>
    public void InitBoardSnapshot(int[,] puzzle)
    {
        if (!HasStateAuthority) return;
        int count = 0;
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                byte val = (byte)puzzle[r, c];
                BoardSnapshot.Set(r * 9 + c, val);
                if (val > 0) count++;
            }
        CompletedCells = count;
    }

    /// <summary>Push the local player's live score so the opponent's board panel can show it.</summary>
    public void UpdateScore(int score)
    {
        if (!HasStateAuthority) return;
        Score = score;
    }

    /// <summary>Push the local player's live half-heart count so the opponent's board panel can show it.</summary>
    public void UpdateHalfHearts(int halfHearts)
    {
        if (!HasStateAuthority) return;
        HalfHearts = halfHearts;
    }

    public void UpdateElapsed(float elapsed)
    {
        if (!HasStateAuthority || elapsed < 0f) return;
        PublishElapsed(elapsed);
    }

    public void SetBackgrounded(bool backgrounded)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority) return;
        try
        {
            IsBackgrounded = backgrounded;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[NetworkSudokuPlayer] Background flag skipped: {ex.Message}");
        }
    }

    private static float ReadLocalElapsed()
    {
        var match = MultiplayerGameController.Instance;
        if (match == null || !match.IsMatchRunning) return -1f;

        var game = SudokuGameManager.Instance;
        if (game != null) return game.ElapsedSeconds;
        return Mathf.Max(0f, Time.time - match.MatchStartTime);
    }

    private void PublishElapsed(float elapsed)
    {
        int score = 0;
        int hearts = HeartManager.MaxHalfHearts;
        var game = SudokuGameManager.Instance;
        if (game != null)
        {
            score = game.SessionScore;
            hearts = game.CurrentHalfHearts;
        }

        MultiplayerManager.Instance?.PublishLiveStats(elapsed, score, hearts);

        try
        {
            int whole = Mathf.FloorToInt(elapsed);
            if (whole != ElapsedWholeSeconds)
                ElapsedWholeSeconds = whole;
            if (elapsed > ElapsedTime)
                ElapsedTime = elapsed;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[NetworkSudokuPlayer] Clock write skipped: {ex.Message}");
        }
    }

    /// <summary>Mark this player as finished.</summary>
    public void MarkFinished(float finishTime)
    {
        if (!HasStateAuthority) return;
        IsFinished = true;
        FinishTime = finishTime;
    }

    /// <summary>Mark this player as having forfeited the match.</summary>
    public void Forfeit()
    {
        if (!HasStateAuthority) return;
        try { IsBackgrounded = false; } catch { /* flag is optional */ }
        HasForfeited = true;
        RpcForfeit();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RpcForfeit()
    {
        HasForfeited = true;
        OnPlayerForfeited?.Invoke(this);
    }
}