using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    public enum Difficulty { Easy, Medium, Hard }

    [Header("Main Menu Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private RectTransform playButtonText;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button aboutButton;
    [Tooltip("Same GameObject wired into ProfileManager's own 'Profile Icon Button' field — this reference only controls when it's shown; ProfileManager still owns the click behavior.")]
    [SerializeField] private Button profileIconButton;

    [Header("Difficulty Panel")]
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private Button backButton;

    [Header("Difficulty Panel - Mode Selection")]
    [Tooltip("SinglePlayer is the only playable mode right now. Shown together with the difficulty posters, not gating them.")]
    [SerializeField] private Button singlePlayerButton;
    [Tooltip("Opens the Multiplayer lobby panel (International + Competition).")]
    [SerializeField] private Button multiPlayerButton;

    [Header("Multiplayer Lobby")]
    [SerializeField] private MultiplayerLobbyUI multiplayerLobbyUI;

    [Header("Difficulty Posters")]
    [SerializeField] private Button easyPoster;
    [SerializeField] private Button mediumPoster;
    [SerializeField] private Button hardPoster;

    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button closeSettingsButton;

    [Header("Settings - Sound / Music Toggle Buttons")]
    [Tooltip("Each toggle button has ToggleBG -> ON circle, OFF circle as children. Only one is active at a time.")]
    [SerializeField] private Button musicToggleButton;
    [SerializeField] private GameObject musicOnCircle;
    [SerializeField] private GameObject musicOffCircle;
    [SerializeField] private TMP_Text musicStatusLabel;

    [SerializeField] private Button sfxToggleButton;
    [SerializeField] private GameObject sfxOnCircle;
    [SerializeField] private GameObject sfxOffCircle;
    [SerializeField] private TMP_Text sfxStatusLabel;

    [Header("Settings - Language")]
    [SerializeField] private TMP_Dropdown languageDropdown;
    [SerializeField] private TMP_Text languageLabel;

    [Header("Scene Names")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Pulse Settings")]
    [SerializeField] private float pulseScale = 1.1f;
    [SerializeField] private float pulseDuration = 0.6f;

    [Header("Panel Animation Settings")]
    [SerializeField] private float panelAnimDuration = 0.35f;

    // ---- NEW: Currency / Profile Display (all optional — leave unassigned
    // if this scene doesn't show them) ----
    [Header("Currency / Profile Display (Optional)")]
    [SerializeField] private TMP_Text coinsLabel;
    [SerializeField] private TMP_Text profileLevelLabel;
    [Tooltip("An Image with Image Type = Filled, used as the XP progress bar.")]
    [SerializeField] private Image profileXPFillImage;
    [Tooltip("e.g. shows '15,000/20,000'.")]
    [SerializeField] private TMP_Text profileXPLabel;

    // Key used to pass the chosen difficulty to the game scene
    public const string DifficultyPrefKey = "SelectedDifficulty";

    // Key used to tell this scene to open straight into the difficulty
    // panel (e.g. when reloaded from a mid-game restart) instead of
    // booting into the main menu buttons.
    public const string OpenDifficultyOnLoadKey = "OpenDifficultyOnLoad";

    // Shared Sound Library ID lookups, so this class and SudokuGameManager
    // always agree on which SFX/music ID goes with which difficulty.
    public static string GetDifficultyButtonSfxId(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Easy: return "EasyButton";
            case Difficulty.Medium: return "MediumButton";
            case Difficulty.Hard: return "HardButton";
            default: return "Button";
        }
    }

    public static string GetDifficultyMusicId(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Easy: return "EasyMusic";
            case Difficulty.Medium: return "MediumMusic";
            case Difficulty.Hard: return "HardMusic";
            default: return "EasyMusic";
        }
    }

    private Tween playButtonPulseTween;
    private Sequence difficultyPanelSequence;
    private Sequence settingsPanelSequence;

    private bool isMusicOn = true;
    private bool isSfxOn = true;

    // Dropdown option labels, in the same order as the Language enum.
    // Each language's own name is always shown in that language, so the
    // dropdown stays readable no matter which language is currently active.
    private static readonly string[] LanguageNativeNames =
    {
        "English",   // Language.English
        "日本語",     // Language.Japanese
        "Español",   // Language.Spanish
        "Português", // Language.Portuguese
        "বাংলা",      // Language.Bangla
        "한국어",     // Language.Korean
        "中文"        // Language.Chinese
    };

    // Source (English) text for the toggle labels — translated live via
    // LocalizationManager, same as everything else.
    private const string OnText = "ON";
    private const string OffText = "OFF";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CleanUpDuplicateEventSystems();
    }

    // SoundManager / LocalizationManager / ProfileManager / LevelManager all
    // dedupe themselves via DontDestroyOnLoad on first Awake, so they're
    // fine. EventSystem has no such dedupe built in, and every reload of
    // this scene (e.g. the flagged reload PromptDifficultySelection does
    // from the in-level Back button) instantiates a brand new one, on top
    // of whichever one already persisted from before. Multiple active
    // EventSystems is a known Unity failure mode: UI clicks — including
    // things like the Profile panel's Close button, which lives on a
    // DontDestroyOnLoad object — become unreliable or stop registering.
    // UIManager itself isn't persistent, so it re-runs this cleanup fresh
    // on every MainMenu load and keeps only the oldest EventSystem alive.
    private void CleanUpDuplicateEventSystems()
    {
        EventSystem[] systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude);
        if (systems.Length <= 1) return;

        EventSystem keep = systems[0];
        for (int i = 1; i < systems.Length; i++)
        {
            if (systems[i] != keep) Destroy(systems[i].gameObject);
        }
    }

    private void Start()
    {
        // ProfileManager is DontDestroyOnLoad, so whatever state its panel
        // was left in (open or closed) survives any reload of this scene —
        // including the flagged reload below from the in-level Back button.
        // Force it closed every time MainMenu boots so it can never come
        // back up already open with no way to dismiss it.
        ProfileManager.Instance?.ForceClosePanel();

        // Make sure panel starts hidden and scaled down (in case it was left active in editor)
        if (difficultyPanel != null)
        {
            difficultyPanel.transform.localScale = Vector3.one;
            difficultyPanel.SetActive(false);
        }

        if (settingsPanel != null)
        {
            settingsPanel.transform.localScale = Vector3.one;
            settingsPanel.SetActive(false);
        }

        // MultiPlayer — now implemented; make it interactable and register listener
        if (multiPlayerButton != null) multiPlayerButton.interactable = true;

        SetupLanguageDropdown();
        SyncLanguageLabel();
        SyncToggleVisualsFromSoundManager();

        // ---- NEW: Currency / Profile Display ----
        RefreshCoinsDisplay(CoinManager.Instance != null ? CoinManager.Instance.TotalCoins : 0);
        RefreshProfileDisplay();

        if (CoinManager.Instance != null) CoinManager.Instance.OnCoinsChanged += RefreshCoinsDisplay;
        if (ProfileManager.Instance != null) ProfileManager.Instance.OnXPChanged += HandleProfileXPChanged;

        SoundManager.Instance?.PlayMusic("MenuMusic");

        RegisterListeners();

        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged += RefreshLocalizedLabels;

        if (PlayerPrefs.GetInt(OpenDifficultyOnLoadKey, 0) == 1)
        {
            PlayerPrefs.DeleteKey(OpenDifficultyOnLoadKey);
            PlayerPrefs.Save();

            // Skip the main menu buttons entirely and open straight into
            // the difficulty panel, mirroring what OnPlayClicked() does.
            if (settingsButton != null) settingsButton.gameObject.SetActive(false);
            if (aboutButton != null) aboutButton.gameObject.SetActive(false);
            if (playButton != null) playButton.gameObject.SetActive(false);
            if (profileIconButton != null) profileIconButton.gameObject.SetActive(false);

            ShowDifficultyPanel();
        }
        else
        {
            StartPlayButtonPulse();
        }
    }

    private void RegisterListeners()
    {
        if (playButton != null) playButton.onClick.AddListener(OnPlayClicked);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);

        if (singlePlayerButton != null) singlePlayerButton.onClick.AddListener(OnSinglePlayerClicked);
        if (multiPlayerButton != null)  multiPlayerButton.onClick.AddListener(OnMultiPlayerClicked);

        if (easyPoster != null) easyPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Easy));
        if (mediumPoster != null) mediumPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Medium));
        if (hardPoster != null) hardPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Hard));

        if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
        if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(OnCloseSettingsClicked);

        if (musicToggleButton != null) musicToggleButton.onClick.AddListener(OnMusicToggleClicked);
        if (sfxToggleButton != null) sfxToggleButton.onClick.AddListener(OnSfxToggleClicked);

        if (languageDropdown != null) languageDropdown.onValueChanged.AddListener(OnLanguageChanged);

        // aboutButton onClick hooked up to your own panel if you have one.
    }

    private void OnDestroy()
    {
        playButtonPulseTween?.Kill();
        difficultyPanelSequence?.Kill();
        settingsPanelSequence?.Kill();

        if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
        if (singlePlayerButton != null) singlePlayerButton.onClick.RemoveListener(OnSinglePlayerClicked);
        if (multiPlayerButton != null)  multiPlayerButton.onClick.RemoveListener(OnMultiPlayerClicked);

        if (settingsButton != null) settingsButton.onClick.RemoveListener(OnSettingsClicked);
        if (closeSettingsButton != null) closeSettingsButton.onClick.RemoveListener(OnCloseSettingsClicked);

        if (musicToggleButton != null) musicToggleButton.onClick.RemoveListener(OnMusicToggleClicked);
        if (sfxToggleButton != null) sfxToggleButton.onClick.RemoveListener(OnSfxToggleClicked);

        if (languageDropdown != null) languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);

        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged -= RefreshLocalizedLabels;

        // ---- NEW: Currency / Profile Display ----
        if (CoinManager.Instance != null) CoinManager.Instance.OnCoinsChanged -= RefreshCoinsDisplay;
        if (ProfileManager.Instance != null) ProfileManager.Instance.OnXPChanged -= HandleProfileXPChanged;
    }

    // ---------------- Play Button Pulse ----------------

    private void StartPlayButtonPulse()
    {
        if (playButtonText == null) return;

        playButtonPulseTween?.Kill();
        playButtonText.localScale = Vector3.one;

        playButtonPulseTween = playButtonText
            .DOScale(pulseScale, pulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(playButtonText.gameObject);
    }

    private void StopPlayButtonPulse()
    {
        playButtonPulseTween?.Kill();
        if (playButtonText != null) playButtonText.localScale = Vector3.one;
    }

    // ---------------- Main Menu -> Difficulty Panel ----------------

    private void OnPlayClicked()
    {
        PlayButtonSfx();
        StopPlayButtonPulse();

        if (settingsButton != null) settingsButton.gameObject.SetActive(false);
        if (aboutButton != null) aboutButton.gameObject.SetActive(false);
        if (playButton != null) playButton.gameObject.SetActive(false);
        if (profileIconButton != null) profileIconButton.gameObject.SetActive(false);

        ShowDifficultyPanel();
    }

    private void OnBackClicked()
    {
        PlayButtonSfx();
        HideDifficultyPanel(() =>
        {
            if (playButton != null) playButton.gameObject.SetActive(true);
            if (settingsButton != null) settingsButton.gameObject.SetActive(true);
            if (aboutButton != null) aboutButton.gameObject.SetActive(true);
            if (profileIconButton != null) profileIconButton.gameObject.SetActive(true);

            StartPlayButtonPulse();
        });
    }

    private void ShowDifficultyPanel()
    {
        if (difficultyPanel == null) return;

        difficultyPanelSequence?.Kill();

        difficultyPanel.SetActive(true);
        difficultyPanel.transform.localScale = Vector3.zero;

        CanvasGroup cg = GetOrAddCanvasGroup(difficultyPanel);
        cg.alpha = 0f;

        difficultyPanelSequence = DOTween.Sequence()
            .Append(difficultyPanel.transform.DOScale(1f, panelAnimDuration).SetEase(Ease.OutBack))
            .Join(cg.DOFade(1f, panelAnimDuration))
            .SetLink(difficultyPanel);
    }

    private void HideDifficultyPanel(System.Action onComplete = null)
    {
        if (difficultyPanel == null)
        {
            onComplete?.Invoke();
            return;
        }

        difficultyPanelSequence?.Kill();

        CanvasGroup cg = GetOrAddCanvasGroup(difficultyPanel);

        difficultyPanelSequence = DOTween.Sequence()
            .Append(difficultyPanel.transform.DOScale(0f, panelAnimDuration).SetEase(Ease.InBack))
            .Join(cg.DOFade(0f, panelAnimDuration))
            .OnComplete(() =>
            {
                difficultyPanel.SetActive(false);
                onComplete?.Invoke();
            })
            .SetLink(difficultyPanel);
    }

    // ---------------- Difficulty Panel - Mode Selection ----------------

    // SinglePlayer is the only playable mode right now, shown together with
    // the difficulty posters rather than gating them. Tapping it is just
    // confirmation feedback.
    private void OnSinglePlayerClicked()
    {
        PlayButtonSfx();
    }

    // Opens the multiplayer lobby panel.
    private void OnMultiPlayerClicked()
    {
        PlayButtonSfx();

        // Hide difficulty panel buttons while the lobby is open
        if (settingsButton != null)  settingsButton.gameObject.SetActive(false);
        if (aboutButton != null)     aboutButton.gameObject.SetActive(false);
        if (playButton != null)      playButton.gameObject.SetActive(false);
        if (profileIconButton != null) profileIconButton.gameObject.SetActive(false);

        HideDifficultyPanel(() =>
        {
            if (multiplayerLobbyUI != null) multiplayerLobbyUI.Show();
        });
    }

    /// <summary>
    /// Called by MultiplayerLobbyUI when the player backs out to the main menu.
    /// Restores the main menu buttons and restarts the play button pulse.
    /// </summary>
    public void RestoreMainMenuButtons()
    {
        if (playButton != null)        playButton.gameObject.SetActive(true);
        if (settingsButton != null)    settingsButton.gameObject.SetActive(true);
        if (aboutButton != null)       aboutButton.gameObject.SetActive(true);
        if (profileIconButton != null) profileIconButton.gameObject.SetActive(true);
        StartPlayButtonPulse();
    }

    // Shared click SFX played by every button on this screen.
    private void PlayButtonSfx()
    {
        SoundManager.Instance?.PlaySFX("Button");
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // ---------------- Settings Panel ----------------

    private void OnSettingsClicked()
    {
        PlayButtonSfx();

        // Reflect current audio state on the switches each time the panel opens.
        SyncToggleVisualsFromSoundManager();

        ShowSettingsPanel();
    }

    private void OnCloseSettingsClicked()
    {
        PlayButtonSfx();
        HideSettingsPanel();
    }

    private void ShowSettingsPanel()
    {
        if (settingsPanel == null) return;

        settingsPanelSequence?.Kill();

        settingsPanel.SetActive(true);
        settingsPanel.transform.localScale = Vector3.zero;

        CanvasGroup cg = GetOrAddCanvasGroup(settingsPanel);
        cg.alpha = 0f;

        settingsPanelSequence = DOTween.Sequence()
            .Append(settingsPanel.transform.DOScale(1f, panelAnimDuration).SetEase(Ease.OutBack))
            .Join(cg.DOFade(1f, panelAnimDuration))
            .SetLink(settingsPanel);
    }

    private void HideSettingsPanel()
    {
        if (settingsPanel == null) return;

        settingsPanelSequence?.Kill();

        CanvasGroup cg = GetOrAddCanvasGroup(settingsPanel);

        settingsPanelSequence = DOTween.Sequence()
            .Append(settingsPanel.transform.DOScale(0f, panelAnimDuration).SetEase(Ease.InBack))
            .Join(cg.DOFade(0f, panelAnimDuration))
            .OnComplete(() => settingsPanel.SetActive(false))
            .SetLink(settingsPanel);
    }

    // ---------------- Settings - Sound / Music Switches ----------------

    // Reads SoundManager's current mute state and updates both switches to
    // match, without going through OnMusicToggleClicked / OnSfxToggleClicked
    // (so it never re-triggers SetMusicMuted/SetSfxMuted).
    private void SyncToggleVisualsFromSoundManager()
    {
        isMusicOn = SoundManager.Instance == null || !SoundManager.Instance.IsMusicMuted;
        isSfxOn = SoundManager.Instance == null || !SoundManager.Instance.IsSfxMuted;

        ApplyToggleVisual(isMusicOn, musicOnCircle, musicOffCircle);
        ApplyToggleVisual(isSfxOn, sfxOnCircle, sfxOffCircle);

        SetStatusLabel(musicStatusLabel, isMusicOn);
        SetStatusLabel(sfxStatusLabel, isSfxOn);
    }

    private void OnMusicToggleClicked()
    {
        PlayButtonSfx();
        isMusicOn = !isMusicOn;
        ApplyToggleVisual(isMusicOn, musicOnCircle, musicOffCircle);
        SetStatusLabel(musicStatusLabel, isMusicOn);
        SoundManager.Instance?.SetMusicMuted(!isMusicOn);
    }

    private void OnSfxToggleClicked()
    {
        PlayButtonSfx();
        isSfxOn = !isSfxOn;
        ApplyToggleVisual(isSfxOn, sfxOnCircle, sfxOffCircle);
        SetStatusLabel(sfxStatusLabel, isSfxOn);
        SoundManager.Instance?.SetSfxMuted(!isSfxOn);
    }

    private void SetStatusLabel(TMP_Text label, bool on)
    {
        if (label == null) return;

        string source = on ? OnText : OffText;
        label.text = LocalizationManager.Instance != null
            ? LocalizationManager.Instance.Translate(source)
            : source;
    }

    // Re-applies ON/OFF text in the current language. Needed because these
    // two labels are set directly in code rather than via LocalizedText.
    private void RefreshLocalizedLabels()
    {
        SetStatusLabel(musicStatusLabel, isMusicOn);
        SetStatusLabel(sfxStatusLabel, isSfxOn);
    }

    // ON: activate the ON object, deactivate the OFF object.
    // OFF: activate the OFF object, deactivate the ON object.
    private void ApplyToggleVisual(bool on, GameObject onCircle, GameObject offCircle)
    {
        if (onCircle != null) onCircle.SetActive(on);
        if (offCircle != null) offCircle.SetActive(!on);
    }

    // ---------------- Settings - Language ----------------

    // Keeps the label beside the dropdown in sync with whatever option is
    // currently selected, both on scene load and on every selection change.
    // Fills the dropdown with the 7 supported languages and selects
    // whichever one is currently active (from the saved PlayerPrefs
    // selection), without firing OnLanguageChanged in the process.
    private void SetupLanguageDropdown()
    {
        if (languageDropdown == null) return;

        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(new List<string>(LanguageNativeNames));

        int current = LocalizationManager.Instance != null ? (int)LocalizationManager.Instance.CurrentLanguage : 0;
        languageDropdown.SetValueWithoutNotify(current);
    }

    private void OnLanguageChanged(int index)
    {
        PlayButtonSfx();

        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.SetLanguage((Language)index);

        SyncLanguageLabel();
    }

    private void SyncLanguageLabel()
    {
        if (languageDropdown == null || languageLabel == null) return;
        if (languageDropdown.options == null || languageDropdown.options.Count == 0) return;

        int index = Mathf.Clamp(languageDropdown.value, 0, languageDropdown.options.Count - 1);
        languageLabel.text = languageDropdown.options[index].text;
    }

    // ---------------- Currency / Profile Display ----------------

    private void RefreshCoinsDisplay(int totalCoins)
    {
        if (coinsLabel != null) coinsLabel.text = totalCoins.ToString("N0");
    }

    private void RefreshProfileDisplay()
    {
        if (ProfileManager.Instance == null) return;

        HandleProfileXPChanged(
            ProfileManager.Instance.CurrentXP,
            ProfileManager.Instance.XPRequiredForCurrentLevel,
            ProfileManager.Instance.ProfileLevel);
    }

    private void HandleProfileXPChanged(int currentXP, int xpRequired, int level)
    {
        if (profileLevelLabel != null) profileLevelLabel.text = $"Level. {level}";
        if (profileXPLabel != null) profileXPLabel.text = $"{currentXP:N0}/{xpRequired:N0}";
        if (profileXPFillImage != null)
            profileXPFillImage.fillAmount = xpRequired > 0 ? Mathf.Clamp01((float)currentXP / xpRequired) : 0f;
    }

    // ---------------- Difficulty Selection -> Game Scene ----------------

    private void OnDifficultySelected(Difficulty difficulty)
    {
        SoundManager.Instance?.PlaySFX(GetDifficultyButtonSfxId(difficulty));

        PlayerPrefs.SetString(DifficultyPrefKey, difficulty.ToString());
        PlayerPrefs.Save();

        // Small punch feedback then load, purely optional polish
        DOTween.Sequence()
            .AppendCallback(() => LoadGameScene());
    }

    private void LoadGameScene()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}