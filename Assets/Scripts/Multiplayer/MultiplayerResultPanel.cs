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

    private void Awake()
    {
        if (panelRoot != null && panelRoot != gameObject) panelRoot.SetActive(false);
        if (returnToMenuButton != null) returnToMenuButton.onClick.AddListener(OnReturnToMenu);
        if (playAgainButton != null)    playAgainButton.onClick.AddListener(OnPlayAgain);
    }

    private void OnDestroy()
    {
        panelSequence?.Kill();
        if (returnToMenuButton != null) returnToMenuButton.onClick.RemoveListener(OnReturnToMenu);
        if (playAgainButton != null)    playAgainButton.onClick.RemoveListener(OnPlayAgain);
    }

    /// <summary>Show the result panel.</summary>
    /// <param name="isWinner">True if local player won.</param>
    /// <param name="localTime">Local player's finish time in seconds.</param>
    public void Show(bool isWinner, float localTime)
    {
        gameObject.SetActive(true);
        if (panelRoot == null) panelRoot = gameObject;

        // Banner
        if (winnerBanner != null) winnerBanner.SetActive(isWinner);
        if (loserBanner != null)  loserBanner.SetActive(!isWinner);

        // Title
        if (resultTitleText != null)
            resultTitleText.text = isWinner ? "YOU WIN! 🎉" : "YOU LOSE";

        // Your time
        if (yourTimeText != null)
            yourTimeText.text = $"Your time: {FormatTime(localTime)}";

        // Opponent info
        var remote = NetworkSudokuPlayer.Remote;
        string oppName = remote != null ? remote.PlayerName.ToString() : "Opponent";
        if (opponentNameText != null)
            opponentNameText.text = oppName;

        if (opponentTimeText != null)
        {
            if (remote != null && remote.HasForfeited)
                opponentTimeText.text = "Forfeited";
            else if (remote != null && remote.IsFinished)
                opponentTimeText.text = $"Time: {FormatTime(remote.FinishTime)}";
            else
                opponentTimeText.text = remote != null && remote.Object.IsValid
                    ? "Still playing…"
                    : "Disconnected";
        }

        // Animate in
        panelRoot.SetActive(true);
        panelRoot.transform.localScale = Vector3.one * 0.75f;
        if (panelCG != null) panelCG.alpha = 0f;

        panelSequence?.Kill();
        panelSequence = DOTween.Sequence()
            .Append(panelRoot.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack))
            .Join(panelCG != null ? panelCG.DOFade(1f, animDuration) : null)
            .SetLink(panelRoot);

        // Play sound
        SoundManager.Instance?.PlaySFX(isWinner ? "Victory" : "GameOver");
    }

    // ---- Button handlers ----

    private void OnReturnToMenu()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        SceneManager.LoadScene("MainMenu");
    }

    private void OnPlayAgain()
    {
        SoundManager.Instance?.PlaySFX("Button");
        // Disconnect and go back to MainMenu lobby
        MultiplayerManager.Instance?.Disconnect();
        SceneManager.LoadScene("MainMenu");
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
