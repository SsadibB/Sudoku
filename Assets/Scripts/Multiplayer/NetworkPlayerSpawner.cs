using Fusion;
using UnityEngine;

/// <summary>
/// Placed in GameScene. When Fusion spawns this as a shared network object,
/// each player automatically gets their own NetworkSudokuPlayer spawned.
/// </summary>
public class NetworkPlayerSpawner : SimulationBehaviour, IPlayerJoined
{
    [SerializeField] private NetworkObject networkPlayerPrefab;

    public void PlayerJoined(PlayerRef player)
    {
        // Only spawn for the local player
        if (player == Runner.LocalPlayer)
        {
            Runner.Spawn(networkPlayerPrefab, Vector3.zero, Quaternion.identity, player);
        }
    }
}
