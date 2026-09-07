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

    // Synchronized puzzle level for multiplayer (set by Master Client)
    [Networked] public int SharedPuzzleLevel { get; set; }

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

    /// <summary>Mark this player as finished.</summary>
    public void MarkFinished(float finishTime)
    {
        if (!HasStateAuthority) return;
        IsFinished = true;
        FinishTime = finishTime;
    }
}
