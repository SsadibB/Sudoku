using UnityEngine;

// Tracks per-difficulty level progress (Level 1 - 1000), persisted locally
// via PlayerPrefs. Completing a level unlocks the next one; earlier levels
// stay unlocked/replayable forever.
//
// SETUP: put this on the same persistent object as SoundManager (the
// "DontDestroyOnLoad" object) so it survives the MainMenu -> GameScene
// transition, or drop it on its own empty GameObject in MainMenu — either
// way it marks itself DontDestroyOnLoad on first Awake.
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    public const int MaxLevel = 1000;

    // Set by a level-select screen (via SetSelectedLevel) before loading
    // GameScene, to tell it exactly which level to open. If nothing was
    // set, GameScene falls back to "Continue" — the first not-yet-completed
    // level for whichever difficulty was chosen.
    public const string SelectedLevelPrefKey = "SelectedLevel";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private static string CompletedKey(UIManager.Difficulty difficulty) => $"HighestCompleted_{difficulty}";

    // Highest level number the player has fully completed on this
    // difficulty. 0 means nothing completed yet.
    public int GetHighestCompleted(UIManager.Difficulty difficulty)
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(CompletedKey(difficulty), 0), 0, MaxLevel);
    }

    // The level a "Continue" button should open: the first level not yet
    // completed (clamped so it never exceeds MaxLevel).
    public int GetContinueLevel(UIManager.Difficulty difficulty)
    {
        return Mathf.Min(GetHighestCompleted(difficulty) + 1, MaxLevel);
    }

    // A level is unlocked/playable if it's already completed OR it's the
    // very next one in line.
    public bool IsLevelUnlocked(UIManager.Difficulty difficulty, int level)
    {
        return level >= 1 && level <= GetContinueLevel(difficulty);
    }

    public bool IsLevelCompleted(UIManager.Difficulty difficulty, int level)
    {
        return level >= 1 && level <= GetHighestCompleted(difficulty);
    }

    // Call when the player finishes `level` on `difficulty`. Only advances
    // progress (and therefore unlocks the next level) if this was the
    // current frontier level — replaying an already-completed earlier
    // level still triggers coin/XP rewards via SudokuGameManager, but
    // won't move progress backward or skip levels.
    public void CompleteLevel(UIManager.Difficulty difficulty, int level)
    {
        int highest = GetHighestCompleted(difficulty);
        if (level == highest + 1)
        {
            highest = Mathf.Min(level, MaxLevel);
            PlayerPrefs.SetInt(CompletedKey(difficulty), highest);
            PlayerPrefs.Save();
        }
    }

    // ---------------- Handoff to GameScene ----------------

    // Call from a level-select button before SceneManager.LoadScene("GameScene").
    public void SetSelectedLevel(int level)
    {
        PlayerPrefs.SetInt(SelectedLevelPrefKey, Mathf.Clamp(level, 1, MaxLevel));
        PlayerPrefs.Save();
    }

    // Reads (and consumes) the level a level-select screen queued up.
    // Falls back to "Continue" for the given difficulty if none was set —
    // e.g. when the player just tapped a difficulty poster with no
    // level-select step in between.
    public int ConsumeSelectedLevel(UIManager.Difficulty difficulty)
    {
        int level = PlayerPrefs.GetInt(SelectedLevelPrefKey, 0);
        PlayerPrefs.DeleteKey(SelectedLevelPrefKey);
        PlayerPrefs.Save();

        return level >= 1 ? Mathf.Clamp(level, 1, MaxLevel) : GetContinueLevel(difficulty);
    }
}