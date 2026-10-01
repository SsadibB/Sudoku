using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using DG.Tweening;

/// <summary>
/// Full-screen result overlay shown when either player finishes the board.
/// Displays win/lose state, opponent name, both times, and a "Return to Menu" button.
/// </summary>
public class MultiplayerResultPanel : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelCG;

    [Header("Result Banner")]
    [SerializeField] private GameObject winnerBanner;
    [SerializeField] private GameObject loserBanner;

    [Header("Info Texts")]
    [SerializeField] private TMP_Text resultTitleText;
    [SerializeField] private TMP_Text yourTimeText;
    [SerializeField] private TMP_Text opponentNameText;
    [SerializeField] private TMP_Text opponentTimeText;

    [Header("Buttons")]
    [SerializeField] private Button returnToMenuButton;
    [SerializeField] private Button playAgainButton;    // optional — returns to lobby

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.45f;

    private Sequence panelSequence;
    private bool leavingToMenu;
    private bool showing;
    private float shownLocalTime;
    private float shownRemoteTime;
    private int shownLocalScore;
    private int shownRemoteScore;
    private int shownLocalLives;
    private int shownRemoteLives;
    private bool statsFrozen;

    private void Awake()
    {
        if (panelRoot != null && panelRoot != gameObject) panelRoot.SetActive(false);
        if (returnToMenuButton != null) returnToMenuButton.onClick.AddListener(OnReturnToMenu);
        if (playAgainButton != null)    playAgainButton.onClick.AddListener(OnPlayAgain);

        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnRematchStateChanged += OnRematchStateChanged;
    }

    private void OnDestroy()
    {
        panelSequence?.Kill();
        if (returnToMenuButton != null) returnToMenuButton.onClick.RemoveListener(OnReturnToMenu);
        if (playAgainButton != null)    playAgainButton.onClick.RemoveListener(OnPlayAgain);
        if (MultiplayerManager.Instance != null)
            MultiplayerManager.Instance.OnRematchStateChanged -= OnRematchStateChanged;
    }

    /// <summary>Show the result panel.</summary>
    /// <param name="isWinner">True if local player won.</param>
    /// <param name="localTime">Local player's finish time in seconds.</param>
    public void Show(bool isWinner, float localTime)
    {
        gameObject.SetActive(true);
        showing = true;
        CaptureShownStats(localTime);
        if (panelRoot == null) panelRoot = gameObject;

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnRematchStateChanged -= OnRematchStateChanged;
            MultiplayerManager.Instance.OnRematchStateChanged += OnRematchStateChanged;
        }

        // Banner
        if (winnerBanner != null) winnerBanner.SetActive(isWinner);
        if (loserBanner != null)  loserBanner.SetActive(!isWinner);

        // Title
        if (resultTitleText != null)
            resultTitleText.text = isWinner ? "YOU WIN! 🎉" : "YOU LOSE";

        // Your time — the value captured when this panel opened.
        if (yourTimeText != null)
            yourTimeText.text = $"Your time: {FormatTime(shownLocalTime)}";

        // Opponent info
        var mp = MultiplayerManager.Instance;
        string oppName = mp != null && !string.IsNullOrWhiteSpace(mp.OpponentName)
            ? mp.OpponentName
            : "Opponent";
        if (opponentNameText != null)
            opponentNameText.text = oppName;

        RefreshTimeLines();

        // Animate in
        panelRoot.SetActive(true);
        panelRoot.transform.localScale = Vector3.one * 0.75f;
        if (panelCG != null) panelCG.alpha = 0f;

        panelSequence?.Kill();
        panelSequence = DOTween.Sequence()
            .Append(panelRoot.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack))
            .Join(panelCG != null ? panelCG.DOFade(1f, animDuration) : null)
            .SetLink(panelRoot);

        FillMatchStats(isWinner, shownLocalTime);

        // Play sound
        SoundManager.Instance?.PlaySFX(isWinner ? "Victory" : "GameOver");
    }

    private void CaptureShownStats(float localTime)
    {
        var mp = MultiplayerManager.Instance;
        mp?.CaptureResultSnapshot();
        if (mp != null && mp.ResultCaptured)
        {
            shownLocalTime = mp.LocalDisplayTime;
            shownRemoteTime = mp.RemoteDisplayTime;
            shownLocalScore = mp.SnapshotLocalScore;
            shownRemoteScore = mp.SnapshotRemoteScore;
            shownLocalLives = mp.SnapshotLocalLives;
            shownRemoteLives = mp.SnapshotRemoteLives;
        }
        else
        {
            shownLocalTime = localTime;
            shownRemoteTime = localTime;
            var game = SudokuGameManager.Instance;
            shownLocalScore = game != null ? game.SessionScore : 0;
            shownLocalLives = game != null ? game.CurrentHalfHearts / 2 : 0;
            shownRemoteScore = 0;
            shownRemoteLives = 0;
        }
        statsFrozen = true;
    }

    private void RefreshTimeLines()
    {
        var local = NetworkSudokuPlayer.Local;
        var remote = NetworkSudokuPlayer.Remote;

        if (yourTimeText != null)
            yourTimeText.text = $"Your time: {FormatTime(shownLocalTime)}";

        if (opponentTimeText == null) return;
        bool forfeited = false;
        if (remote != null && remote.Object != null && remote.Object.IsValid)
        {
            try { forfeited = remote.HasForfeited; }
            catch { forfeited = false; }
        }
        if (forfeited)
        {
            opponentTimeText.text = "Forfeited";
            return;
        }

        if (statsFrozen)
        {
            opponentTimeText.text = $"Time: {FormatTime(shownRemoteTime)}";
        }
        else if (remote != null && remote.Object != null && remote.Object.IsValid)
            opponentTimeText.text = "Still playing…";
        else
            opponentTimeText.text = "Disconnected";
    }

    // ---- Button handlers ----

    private void OnReturnToMenu()
    {
        SoundManager.Instance?.PlaySFX("Button");
        if (returnToMenuButton != null) returnToMenuButton.interactable = false;

        RewardedAdManager.EnsureInstance();
        if (RewardedAdManager.Instance == null)
        {
            GoHome();
            return;
        }

        RewardedAdManager.Instance.ShowRewardedAd(null, GoHome, GoHome);
    }

    private void GoHome()
    {
        if (leavingToMenu) return;
        leavingToMenu = true;
        MultiplayerManager.Instance?.Disconnect();
        SceneManager.LoadScene("MainMenu");
    }

    private void OnPlayAgain()
    {
        SoundManager.Instance?.PlaySFX("Button");
        if (playAgainButton != null) playAgainButton.interactable = false;
        SetWaitingForRematch();
        MultiplayerManager.Instance?.RequestRematch();
    }

    private void OnRematchStateChanged()
    {
        // Each player opens the searching panel when they press Rematch.
        // The other player is not pulled out of the result screen.
    }

    private void SetWaitingForRematch()
    {
        if (resultTitleText != null)
            resultTitleText.text = "Waiting for opponent to rematch...";

        if (panelRoot == null) return;
        foreach (var text in panelRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.gameObject.name == "Output_Text (TMP)")
                text.text = "Waiting for your opponent to rematch.";
        }
    }

    private void FillMatchStats(bool isWinner, float localTime)
    {
        if (panelRoot == null) return;

        int localScore = shownLocalScore;
        int localLives = shownLocalLives;
        int remoteScore = shownRemoteScore;
        int remoteLives = shownRemoteLives;
        if (!statsFrozen)
        {
            var local = NetworkSudokuPlayer.Local;
            var remote = NetworkSudokuPlayer.Remote;
            localScore = SudokuGameManager.Instance != null ? SudokuGameManager.Instance.SessionScore : (local != null ? local.Score : 0);
            localLives = local != null ? local.HalfHearts / 2 : (SudokuGameManager.Instance != null ? SudokuGameManager.Instance.CurrentHalfHearts / 2 : 0);
            remoteScore = remote != null ? remote.Score : 0;
            remoteLives = remote != null ? Mathf.Max(0, remote.HalfHearts) / 2 : 0;
        }

        SetStat(panelRoot.transform, "PlayerStats", localScore, shownLocalTime, localLives);
        SetStat(panelRoot.transform, "OpponentStats", remoteScore, shownRemoteTime, remoteLives);
    }

    private static void SetStat(Transform root, string sideName, int score, float time, int lives)
    {
        Transform side = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == sideName)
            {
                side = t;
                break;
            }
        }
        if (side == null) return;

        SetNamedValue(side, "ScoreStats", score.ToString());
        SetNamedValue(side, "TimeStats", FormatClock(time));
        SetNamedValue(side, "LivesStats", lives.ToString());
    }

    private static void SetNamedValue(Transform side, string statName, string value)
    {
        Transform stat = side.Find(statName);
        if (stat == null)
        {
            foreach (var t in side.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == statName)
                {
                    stat = t;
                    break;
                }
            }
        }
        if (stat == null) return;

        TMP_Text[] texts = stat.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length == 0) return;
        texts[texts.Length - 1].text = value;
    }

    private static string FormatClock(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    // ---- Helpers ----

    private string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int m = total / 60;
        int s = total % 60;
        return $"{m:00}:{s:00}";
    }
}
