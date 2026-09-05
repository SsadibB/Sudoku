using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Manages the Competition mode panel: hosting (creates room + shows code)
/// and joining (enter code → join). Only visible in Competition flow.
/// </summary>
public class CompetitionRoomUI : MonoBehaviour
{
    [Header("Root Panel")]
    [SerializeField] private GameObject competitionPanel;

    [Header("Mode Chooser (shown first)")]
    [SerializeField] private GameObject modeChooserGroup;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;

    [Header("Host Flow — Difficulty Selection")]
    [SerializeField] private GameObject hostDifficultyGroup;
    [SerializeField] private Button hostEasyButton;
    [SerializeField] private Button hostMediumButton;
    [SerializeField] private Button hostHardButton;

    [Header("Host Flow — Waiting")]
    [SerializeField] private GameObject hostWaitGroup;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text hostStatusText;
    [SerializeField] private Button cancelHostButton;

    [Header("Join Flow")]
    [SerializeField] private GameObject joinGroup;
    [SerializeField] private TMP_InputField roomCodeInput;
    [SerializeField] private Button joinConfirmButton;
    [SerializeField] private TMP_Text joinStatusText;
    [SerializeField] private TMP_Text guestDifficultyText;
    [SerializeField] private Button cancelJoinButton;

    [Header("Back")]
    [SerializeField] private Button backButton;

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.3f;

    private UIManager.Difficulty selectedHostDifficulty;

    private void OnEnable()
    {
        RegisterListeners();
        ShowModeChooser();
    }

    private void OnDisable()
    {
        UnregisterListeners();
    }

    private void RegisterListeners()
    {
        if (hostButton != null)         hostButton.onClick.AddListener(OnHostClicked);
        if (joinButton != null)          joinButton.onClick.AddListener(OnJoinClicked);

        if (hostEasyButton != null)     hostEasyButton.onClick.AddListener(() => OnHostDifficultySelected(UIManager.Difficulty.Easy));
        if (hostMediumButton != null)   hostMediumButton.onClick.AddListener(() => OnHostDifficultySelected(UIManager.Difficulty.Medium));
        if (hostHardButton != null)     hostHardButton.onClick.AddListener(() => OnHostDifficultySelected(UIManager.Difficulty.Hard));

        if (cancelHostButton != null)   cancelHostButton.onClick.AddListener(OnCancelHostClicked);
        if (cancelJoinButton != null)   cancelJoinButton.onClick.AddListener(OnCancelJoinClicked);
        if (joinConfirmButton != null)  joinConfirmButton.onClick.AddListener(OnJoinConfirmClicked);
        if (backButton != null)         backButton.onClick.AddListener(OnBackClicked);

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnRoomCodeGenerated += OnRoomCodeGenerated;
            MultiplayerManager.Instance.OnOpponentJoined    += OnOpponentJoined;
            MultiplayerManager.Instance.OnConnectionFailed  += OnConnectionFailed;
        }
    }

    private void UnregisterListeners()
    {
        if (hostButton != null)         hostButton.onClick.RemoveListener(OnHostClicked);
        if (joinButton != null)          joinButton.onClick.RemoveListener(OnJoinClicked);

        if (hostEasyButton != null)     hostEasyButton.onClick.RemoveListener(() => OnHostDifficultySelected(UIManager.Difficulty.Easy));
        if (hostMediumButton != null)   hostMediumButton.onClick.RemoveListener(() => OnHostDifficultySelected(UIManager.Difficulty.Medium));
        if (hostHardButton != null)     hostHardButton.onClick.RemoveListener(() => OnHostDifficultySelected(UIManager.Difficulty.Hard));

        if (cancelHostButton != null)   cancelHostButton.onClick.RemoveListener(OnCancelHostClicked);
        if (cancelJoinButton != null)   cancelJoinButton.onClick.RemoveListener(OnCancelJoinClicked);
        if (joinConfirmButton != null)  joinConfirmButton.onClick.RemoveListener(OnJoinConfirmClicked);
        if (backButton != null)         backButton.onClick.RemoveListener(OnBackClicked);

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnRoomCodeGenerated -= OnRoomCodeGenerated;
            MultiplayerManager.Instance.OnOpponentJoined    -= OnOpponentJoined;
            MultiplayerManager.Instance.OnConnectionFailed  -= OnConnectionFailed;
        }
    }

    // ---- Panel open / close ----

    public void Show()
    {
        if (competitionPanel == null) return;
        competitionPanel.SetActive(true);
        competitionPanel.transform.localScale = Vector3.zero;
        competitionPanel.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack).SetLink(competitionPanel);
        ShowModeChooser();
    }

    public void Hide(System.Action onComplete = null)
    {
        if (competitionPanel == null) { onComplete?.Invoke(); return; }
        competitionPanel.transform.DOScale(0f, animDuration).SetEase(Ease.InBack)
            .OnComplete(() => { competitionPanel.SetActive(false); onComplete?.Invoke(); })
            .SetLink(competitionPanel);
    }

    // ---- Sub-group helpers ----

    private void ShowModeChooser()
    {
        SetGroupActive(modeChooserGroup, true);
        SetGroupActive(hostDifficultyGroup, false);
        SetGroupActive(hostWaitGroup, false);
        SetGroupActive(joinGroup, false);
    }

    private void SetGroupActive(GameObject g, bool active)
    {
        if (g != null) g.SetActive(active);
    }

    // ---- Host flow ----

    private void OnHostClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        SetGroupActive(modeChooserGroup, false);
        SetGroupActive(hostDifficultyGroup, true);
    }

    private void OnHostDifficultySelected(UIManager.Difficulty difficulty)
    {
        SoundManager.Instance?.PlaySFX(UIManager.GetDifficultyButtonSfxId(difficulty));
        selectedHostDifficulty = difficulty;
        SetGroupActive(hostDifficultyGroup, false);
        SetGroupActive(hostWaitGroup, true);

        if (hostStatusText != null) hostStatusText.text = "Creating room…";
        if (roomCodeText != null)   roomCodeText.text = "------";

        _ = CreateRoomAsync(difficulty);
    }

    private async Task CreateRoomAsync(UIManager.Difficulty difficulty)
    {
        if (MultiplayerManager.Instance == null) return;
        string code = await MultiplayerManager.Instance.CreateCompetitionRoom(difficulty);
        if (!string.IsNullOrEmpty(code))
        {
            if (roomCodeText != null) roomCodeText.text = code;
            if (hostStatusText != null) hostStatusText.text = "Share this code. Waiting for opponent…";
        }
        else
        {
            if (hostStatusText != null && !hostStatusText.text.StartsWith("Failed:"))
            {
                hostStatusText.text = "Failed to create room. Try again.";
            }
        }
    }

    private void OnRoomCodeGenerated(string code)
    {
        if (roomCodeText != null) roomCodeText.text = code;
        if (hostStatusText != null) hostStatusText.text = "Share this code. Waiting for opponent…";
    }

    private void OnOpponentJoined()
    {
        if (hostStatusText != null) hostStatusText.text = "Opponent joined! Starting…";
    }

    private void OnCancelHostClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        ShowModeChooser();
    }

    // ---- Join flow ----

    private void OnJoinClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        SetGroupActive(modeChooserGroup, false);
        SetGroupActive(joinGroup, true);
        if (joinStatusText != null)      joinStatusText.text = "";
        if (guestDifficultyText != null) guestDifficultyText.text = "";
        if (roomCodeInput != null)       roomCodeInput.text = "";
    }

    private void OnJoinConfirmClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        string code = roomCodeInput != null ? roomCodeInput.text.Trim().ToUpper() : "";

        if (code.Length != 6)
        {
            if (joinStatusText != null) joinStatusText.text = "Enter a valid 6-character code.";
            return;
        }

        if (joinStatusText != null)         joinStatusText.text = "Joining…";
        if (joinConfirmButton != null)      joinConfirmButton.interactable = false;

        MultiplayerManager.Instance?.JoinCompetitionRoom(code);
    }

    private void OnConnectionFailed(string reason)
    {
        Debug.LogError($"[CompetitionRoomUI] Connection failed: {reason}");
        if (hostStatusText != null) hostStatusText.text = $"Failed: {reason}";
        if (joinStatusText != null) joinStatusText.text = $"Could not join: {reason}";
        if (joinConfirmButton != null) joinConfirmButton.interactable = true;
    }

    private void OnCancelJoinClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        ShowModeChooser();
    }

    // ---- Back ----

    private void OnBackClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        Hide();
        // Notify the MultiplayerLobbyUI to reshow its mode panel
        MultiplayerLobbyUI.Instance?.ShowModePanel();
    }
}
