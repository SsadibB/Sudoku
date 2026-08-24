using UnityEngine;

// Tracks the player's coin total, persisted locally via PlayerPrefs.
//
// SETUP: same as LevelManager — put it on the persistent
// "DontDestroyOnLoad" object (or its own object; it marks itself
// DontDestroyOnLoad on first Awake either way).
public class CoinManager : MonoBehaviour
{
    public static CoinManager Instance { get; private set; }

    private const string CoinsKey = "PlayerCoins";

    public int TotalCoins { get; private set; }

    // Fired any time the total changes, passing the new total — UIManager
    // subscribes to this to keep the coin label live.
    public event System.Action<int> OnCoinsChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        TotalCoins = Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, 0));
    }

    // amount can be negative internally (SpendCoins uses this), but the
    // public entry point for rewards should always pass a positive value.
    public void AddCoins(int amount)
    {
        if (amount == 0) return;

        TotalCoins = Mathf.Max(0, TotalCoins + amount);
        Save();
        OnCoinsChanged?.Invoke(TotalCoins);
    }

    // Returns false (and changes nothing) if the player can't afford it —
    // handy for a future shop/IAP screen.
    public bool SpendCoins(int amount)
    {
        if (amount <= 0) return true;
        if (TotalCoins < amount) return false;

        TotalCoins -= amount;
        Save();
        OnCoinsChanged?.Invoke(TotalCoins);
        return true;
    }

    private void Save()
    {
        PlayerPrefs.SetInt(CoinsKey, TotalCoins);
        PlayerPrefs.Save();
    }

    // ==================== Cloud sync (PlayFab) ====================

    // Called by SudokuPlayFabManager after login, with whatever value came
    // back from the cloud. Merges by taking the max so a device that's
    // behind never overwrites/loses coins the player already earned
    // elsewhere. Returns the resulting (post-merge) total so the caller
    // can push it back up if the cloud was actually behind.
    public int ApplyCloudValue(int cloudCoins)
    {
        if (cloudCoins > TotalCoins)
        {
            TotalCoins = cloudCoins;
            Save();
            OnCoinsChanged?.Invoke(TotalCoins);
        }
        return TotalCoins;
    }
}