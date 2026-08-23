using UnityEngine;

// Tracks the player's Profile Level and XP — separate from the per-
// difficulty Sudoku level progress (LevelManager). Completing a Sudoku
// level grants Profile XP; once the XP bar fills, Profile Level increases.
//
// SETUP: same as LevelManager/CoinManager — persistent object, marks
// itself DontDestroyOnLoad on first Awake.
public class ProfileLevelManager : MonoBehaviour
{
    public static ProfileLevelManager Instance { get; private set; }

    private const string ProfileLevelKey = "ProfileLevel";
    private const string ProfileXPKey = "ProfileXP";

    [Header("XP Curve")]
    [Tooltip("XP required to go from Profile Level 1 to Level 2.")]
    [SerializeField] private int baseXPRequired = 100;
    [Tooltip("Extra XP required per subsequent level, added on top of the base each time (e.g. Lvl2->3 needs baseXPRequired + this, Lvl3->4 needs baseXPRequired + 2x this, etc.)")]
    [SerializeField] private int xpIncreasePerLevel = 50;

    public int ProfileLevel { get; private set; }
    public int CurrentXP { get; private set; }
    public int XPRequiredForCurrentLevel => GetXPRequiredForLevel(ProfileLevel);

    // Fired whenever XP changes (including as part of a level-up),
    // args: (currentXP, xpRequiredForCurrentLevel, profileLevel).
    public event System.Action<int, int, int> OnXPChanged;

    // Fired specifically when Profile Level increases, args: newLevel.
    // Handy for a one-off "Level Up!" popup/animation.
    public event System.Action<int> OnLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        ProfileLevel = Mathf.Max(1, PlayerPrefs.GetInt(ProfileLevelKey, 1));
        CurrentXP = Mathf.Max(0, PlayerPrefs.GetInt(ProfileXPKey, 0));
    }

    private int GetXPRequiredForLevel(int level)
    {
        return baseXPRequired + Mathf.Max(0, level - 1) * xpIncreasePerLevel;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        CurrentXP += amount;

        int required = GetXPRequiredForLevel(ProfileLevel);
        while (CurrentXP >= required)
        {
            CurrentXP -= required;
            ProfileLevel++;
            OnLevelUp?.Invoke(ProfileLevel);
            required = GetXPRequiredForLevel(ProfileLevel);
        }

        Save();
        OnXPChanged?.Invoke(CurrentXP, required, ProfileLevel);
    }

    private void Save()
    {
        PlayerPrefs.SetInt(ProfileLevelKey, ProfileLevel);
        PlayerPrefs.SetInt(ProfileXPKey, CurrentXP);
        PlayerPrefs.Save();
    }
}