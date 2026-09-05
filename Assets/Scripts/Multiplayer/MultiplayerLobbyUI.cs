using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Manages the main Multiplayer lobby panel shown after tapping the
/// Multiplayer button: lets the player pick International or Competition mode.
/// Also owns the International difficulty + searching sub-flow.
/// </summary>
public class MultiplayerLobbyUI : MonoBehaviour
{
    public static MultiplayerLobbyUI Instance { get; private set; }

    [Header("Lobby Root Panel")]
    [SerializeField] private GameObject lobbyPanel;

    [Header("Mode Selection")]
    [SerializeField] private GameObject modePanelGroup;
    [SerializeField] private Button internationalButton;
    [SerializeField] private Button competitionButton;
    [SerializeField] private Button backToMainMenuButton;

    [Header("International Flow — Difficulty")]
    [SerializeField] private GameObject intlDifficultyGroup;
    [SerializeField] private Button intlEasyButton;
    [SerializeField] private Button intlMediumButton;
    [SerializeField] private Button intlHardButton;
    [SerializeField] private Button intlDifficultyBackButton;

    [Header("International Flow — Searching")]
    [SerializeField] private GameObject searchingGroup;
    [SerializeField] private TMP_Text searchingStatusText;
    [SerializeField] private RectTransform searchingSpinner;
    [SerializeField] private Button cancelSearchButton;

    [Header("Competition Sub-Panel")]
    [SerializeField] private CompetitionRoomUI competitionRoomUI;

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.35f;

    private Tween spinnerTween;

    // Guards against Unity's "fast Play Mode" leaving a stale DontDestroyOnLoad
    // copy alive between Play sessions (when Reload Domain/Reload Scene are
    // disabled in Enter Play Mode Settings). Without this, the OLD leftover
    // instance still holds a non-null Instance when the freshly-loaded, fully
    // wired scene object runs Awake — so the fresh one self-destructs via the
    // singleton check below, leaving the stale, unwired leftover as Instance.
    // Running this before the scene loads destroys any leftover and clears
    // the static reference, so the singleton check always resolves in favor
    // of the current scene's real, Inspector-wired object.
    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        RegisterListeners();
    }

    private void OnDisable()
    {
        UnregisterListeners();
        StopSpinner();
    }

    private void RegisterListeners()
    {
        if (internationalButton != null) internationalButton.onClick.AddListener(OnInternationalClicked);
        if (competitionButton != null) competitionButton.onClick.AddListener(OnCompetitionClicked);
        if (backToMainMenuButton != null) backToMainMenuButton.onClick.AddListener(OnBackToMainMenuClicked);

        if (intlEasyButton != null) intlEasyButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Easy));
        if (intlMediumButton != null) intlMediumButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Medium));
        if (intlHardButton != null) intlHardButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Hard));
        if (intlDifficultyBackButton != null) intlDifficultyBackButton.onClick.AddListener(ShowModePanel);

        if (cancelSearchButton != null) cancelSearchButton.onClick.AddListener(OnCancelSearch);

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout += OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed += OnConnectionFailed;
        }
    }

    private void UnregisterListeners()
    {
        if (internationalButton != null) internationalButton.onClick.RemoveListener(OnInternationalClicked);
        if (competitionButton != null) competitionButton.onClick.RemoveListener(OnCompetitionClicked);
        if (backToMainMenuButton != null) backToMainMenuButton.onClick.RemoveListener(OnBackToMainMenuClicked);

        if (intlEasyButton != null) intlEasyButton.onClick.RemoveListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Easy));
        if (intlMediumButton != null) intlMediumButton.onClick.RemoveListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Medium));
        if (intlHardButton != null) intlHardButton.onClick.RemoveListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Hard));
        if (intlDifficultyBackButton != null) intlDifficultyBackButton.onClick.RemoveListener(ShowModePanel);

        if (cancelSearchButton != null) cancelSearchButton.onClick.RemoveListener(OnCancelSearch);

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout -= OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed -= OnConnectionFailed;
        }
    }

    // ====================================================================
    //  PUBLIC API
    // ====================================================================

    public void Show()
    {
        if (lobbyPanel == null) return;
        lobbyPanel.SetActive(true);
        lobbyPanel.transform.localScale = Vector3.zero;

        CanvasGroup cg = GetOrAddCG(lobbyPanel);
        cg.alpha = 0f;

        DOTween.Sequence()
            .Append(lobbyPanel.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack))
            .Join(cg.DOFade(1f, animDuration))
            .SetLink(lobbyPanel);

        ShowModePanel();
    }

    public void Hide(System.Action onComplete = null)
    {
        if (lobbyPanel == null) { onComplete?.Invoke(); return; }
        CanvasGroup cg = GetOrAddCG(lobbyPanel);
        DOTween.Sequence()
            .Append(lobbyPanel.transform.DOScale(0f, animDuration).SetEase(Ease.InBack))
            .Join(cg.DOFade(0f, animDuration))
            .OnComplete(() => { lobbyPanel.SetActive(false); onComplete?.Invoke(); })
            .SetLink(lobbyPanel);
    }

    public void ShowModePanel()
    {
        SetGroupActive(modePanelGroup, true);
        SetGroupActive(intlDifficultyGroup, false);
        SetGroupActive(searchingGroup, false);
        if (competitionRoomUI != null) competitionRoomUI.gameObject.SetActive(false);
    }

    // ====================================================================
    //  BUTTON HANDLERS
    // ====================================================================

    private void OnInternationalClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        SetGroupActive(modePanelGroup, false);
        SetGroupActive(intlDifficultyGroup, true);
    }

    private void OnCompetitionClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        SetGroupActive(modePanelGroup, false);
        if (competitionRoomUI != null)
        {
            competitionRoomUI.gameObject.SetActive(true);
            competitionRoomUI.Show();
        }
    }

    private void OnBackToMainMenuClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        Hide(() =>
        {
            // Tell UIManager to restore the main menu buttons
            UIManager.Instance?.RestoreMainMenuButtons();
        });
    }

    // ---- International difficulty ----

    private void OnIntlDifficultySelected(UIManager.Difficulty difficulty)
    {
        SoundManager.Instance?.PlaySFX(UIManager.GetDifficultyButtonSfxId(difficulty));
        SetGroupActive(intlDifficultyGroup, false);
        SetGroupActive(searchingGroup, true);

        if (searchingStatusText != null)
            searchingStatusText.text = $"Searching for a {difficulty} opponent…";

        StartSpinner();
        MultiplayerManager.Instance?.StartInternational(difficulty);
    }

    private void OnCancelSearch()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();
        StopSpinner();
        ShowModePanel();
    }

    // ---- Callbacks ----

    private void OnMatchmakingTimeout()
    {
        StopSpinner();
        if (searchingStatusText != null) searchingStatusText.text = "No opponent found. Try again later.";
        // Let user cancel back to menu
    }

    private void OnConnectionFailed(string reason)
    {
        StopSpinner();
        if (searchingStatusText != null) searchingStatusText.text = $"Connection failed: {reason}";
    }

    // ====================================================================
    //  HELPERS
    // ====================================================================

    private void SetGroupActive(GameObject g, bool active)
    {
        if (g != null) g.SetActive(active);
    }

    private CanvasGroup GetOrAddCG(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private void StartSpinner()
    {
        if (searchingSpinner == null) return;
        spinnerTween?.Kill();
        searchingSpinner.localRotation = Quaternion.identity;
        spinnerTween = searchingSpinner.DORotate(new Vector3(0f, 0f, -360f), 1.2f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear).SetLoops(-1).SetLink(searchingSpinner.gameObject);
    }

    private void StopSpinner()
    {
        spinnerTween?.Kill();
        spinnerTween = null;
    }
}