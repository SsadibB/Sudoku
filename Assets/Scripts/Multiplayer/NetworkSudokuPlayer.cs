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

    // Player name (max 32 chars)
    [Networked] public NetworkString<_32> PlayerName { get; set; }

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

    // ---- Called by MultiplayerGameController ----

    /// <summary>Record a correct cell placement (state-authority only).</summary>
    public void RecordCorrectCell(int row, int col, byte value)
    {
        if (!HasStateAuthority) return;
        CompletedCells++;
        BoardSnapshot.Set(row * 9 + col, value);
    }

    /// <summary>Sync the initial puzzle fixed cells into the snapshot.</summary>
    public void InitBoardSnapshot(int[,] puzzle)
    {
        if (!HasStateAuthority) return;
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                BoardSnapshot.Set(r * 9 + c, (byte)puzzle[r, c]);
    }

    /// <summary>Mark this player as finished.</summary>
    public void MarkFinished(float finishTime)
    {
        if (!HasStateAuthority) return;
        IsFinished = true;
        FinishTime = finishTime;
    }
}
