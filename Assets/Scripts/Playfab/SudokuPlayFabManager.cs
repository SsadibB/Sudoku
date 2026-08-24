using System;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;

// Lightweight PlayFab layer for the Sudoku project — login/identity plus
// two-way sync of everything that currently lives in PlayerPrefs:
// CoinManager (coins), LevelManager (per-difficulty progress), and
// ProfileManager (profile level/XP, puzzles solved, win streak, high
// scores). This is intentionally separate from AreaForge's
// PlayFabManager.cs — same LoginWithCustomID + device-GUID pattern, but
// scoped to Sudoku's data shape instead of DisplayName/phone number.
//
// SETUP: put this on the same persistent "DontDestroyOnLoad" object as
// CoinManager / LevelManager / ProfileManager, or its own object — it
// marks itself DontDestroyOnLoad on first Awake either way. Call Login()
// once, early (e.g. a small splash/bootstrap scene before MainMenu).
//
// SYNC STRATEGY: every field is merged by taking the max of local vs.
// cloud (see ApplyCloudValue / ApplyCloudProgress / ApplyCloudStats on
// the other three managers) — so logging in on a second device, or after
// reinstalling, can only ever raise progress, never roll it back. After
// merging, the (now-authoritative) local state is pushed back to the
// cloud so both sides end up in sync.
//
// Data is pushed automatically on OnApplicationPause/OnApplicationQuit
// (the reliable "player is leaving" points on mobile). Call PushToCloud()
// manually too after anything you especially don't want to risk losing
// (e.g. right after a board completion) — it's cheap to call.
public class SudokuPlayFabManager : MonoBehaviour
{
    public static SudokuPlayFabManager Instance { get; private set; }

    private const string DeviceIdKey = "PlayFabDeviceId";
    private const string SaveDataKey = "SudokuSave";

    public bool IsLoggedIn { get; private set; }
    public bool IsSyncing { get; private set; }

    // Fired once login + the initial cloud merge are both done — good
    // point to unhide UI that shows coins/level/profile stats, so the
    // player never sees a flash of stale pre-sync numbers.
    public event Action OnSyncComplete;
    public event Action<string> OnLoginFailed;

    [Serializable]
    private class SudokuSaveData
    {
        public int coins;

        public int easyHighestCompleted;
        public int mediumHighestCompleted;
        public int hardHighestCompleted;

        public int profileLevel;
        public int profileXP;
        public int puzzlesSolved;
        public int bestWinStreak;

        public int easyHighScore;
        public int mediumHighScore;
        public int hardHighScore;
    }

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

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && IsLoggedIn) PushToCloud();
    }

    private void OnApplicationQuit()
    {
        if (IsLoggedIn) PushToCloud();
    }

    // ==================== Login ====================

    public void Login()
    {
        string deviceId = PlayerPrefs.GetString(DeviceIdKey, "");
        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(DeviceIdKey, deviceId);
            PlayerPrefs.Save();
        }

        var request = new LoginWithCustomIDRequest
        {
            CustomId = deviceId,
            CreateAccount = true
        };

        PlayFabClientAPI.LoginWithCustomID(request, OnLoginSuccess, OnLoginError);
    }

    private void OnLoginSuccess(LoginResult result)
    {
        IsLoggedIn = true;
        FetchAndMergeCloudData();
    }

    private void OnLoginError(PlayFabError error)
    {
        IsLoggedIn = false;
        Debug.LogWarning("SudokuPlayFabManager: login failed — " + error.GenerateErrorReport());
        OnLoginFailed?.Invoke(error.ErrorMessage);
    }

    // ==================== Pull + merge ====================

    private void FetchAndMergeCloudData()
    {
        IsSyncing = true;

        var request = new GetUserDataRequest
        {
            Keys = new System.Collections.Generic.List<string> { SaveDataKey }
        };

        PlayFabClientAPI.GetUserData(request, OnGetUserDataSuccess, OnSyncError);
    }

    private void OnGetUserDataSuccess(GetUserDataResult result)
    {
        if (result.Data != null && result.Data.TryGetValue(SaveDataKey, out UserDataRecord record)
            && !string.IsNullOrEmpty(record.Value))
        {
            SudokuSaveData cloud;
            try
            {
                cloud = JsonUtility.FromJson<SudokuSaveData>(record.Value);
            }
            catch (Exception e)
            {
                Debug.LogWarning("SudokuPlayFabManager: failed to parse cloud save, skipping merge — " + e.Message);
                FinishSync();
                return;
            }

            MergeCloudDataIntoLocal(cloud);
        }

        // Whether or not there was existing cloud data, push the
        // now-authoritative local (post-merge) state back up so both
        // sides agree.
        PushToCloud();
        FinishSync();
    }

    private void OnSyncError(PlayFabError error)
    {
        Debug.LogWarning("SudokuPlayFabManager: cloud fetch failed, continuing with local data only — " + error.GenerateErrorReport());
        FinishSync();
    }

    private void FinishSync()
    {
        IsSyncing = false;
        OnSyncComplete?.Invoke();
    }

    private void MergeCloudDataIntoLocal(SudokuSaveData cloud)
    {
        if (CoinManager.Instance != null)
        {
            CoinManager.Instance.ApplyCloudValue(cloud.coins);
        }

        if (LevelManager.Instance != null)
        {
            LevelManager.Instance.ApplyCloudProgress(UIManager.Difficulty.Easy, cloud.easyHighestCompleted);
            LevelManager.Instance.ApplyCloudProgress(UIManager.Difficulty.Medium, cloud.mediumHighestCompleted);
            LevelManager.Instance.ApplyCloudProgress(UIManager.Difficulty.Hard, cloud.hardHighestCompleted);
        }

        if (ProfileManager.Instance != null)
        {
            ProfileManager.Instance.ApplyCloudStats(
                cloud.profileLevel, cloud.profileXP, cloud.puzzlesSolved, cloud.bestWinStreak,
                cloud.easyHighScore, cloud.mediumHighScore, cloud.hardHighScore);
        }
    }

    // ==================== Push ====================

    // Safe to call any time after login — e.g. right after a board
    // completion, in addition to the automatic pause/quit pushes.
    public void PushToCloud()
    {
        if (!IsLoggedIn) return;
        if (CoinManager.Instance == null || LevelManager.Instance == null || ProfileManager.Instance == null)
        {
            Debug.LogWarning("SudokuPlayFabManager: push skipped, one or more managers aren't ready yet.");
            return;
        }

        var data = new SudokuSaveData
        {
            coins = CoinManager.Instance.TotalCoins,

            easyHighestCompleted = LevelManager.Instance.GetHighestCompleted(UIManager.Difficulty.Easy),
            mediumHighestCompleted = LevelManager.Instance.GetHighestCompleted(UIManager.Difficulty.Medium),
            hardHighestCompleted = LevelManager.Instance.GetHighestCompleted(UIManager.Difficulty.Hard),

            profileLevel = ProfileManager.Instance.ProfileLevel,
            profileXP = ProfileManager.Instance.CurrentXP,
            puzzlesSolved = ProfileManager.Instance.TotalPuzzlesSolved,
            bestWinStreak = ProfileManager.Instance.BestWinStreak,

            easyHighScore = ProfileManager.Instance.GetHighScore(UIManager.Difficulty.Easy),
            mediumHighScore = ProfileManager.Instance.GetHighScore(UIManager.Difficulty.Medium),
            hardHighScore = ProfileManager.Instance.GetHighScore(UIManager.Difficulty.Hard)
        };

        string json = JsonUtility.ToJson(data);

        var request = new UpdateUserDataRequest
        {
            Data = new System.Collections.Generic.Dictionary<string, string> { { SaveDataKey, json } },
            Permission = UserDataPermission.Private
        };

        PlayFabClientAPI.UpdateUserData(request, _ => { }, error =>
        {
            Debug.LogWarning("SudokuPlayFabManager: push to cloud failed — " + error.GenerateErrorReport());
        });
    }
}