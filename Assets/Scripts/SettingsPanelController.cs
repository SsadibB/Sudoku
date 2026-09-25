using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class SettingsPanelController : MonoBehaviour
{
    public static SettingsPanelController Instance { get; private set; }

    [Header("Panel Root & Animation")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private float animDuration = 0.25f;

    [Header("Close Button")]
    [SerializeField] private Button closeButton;

    [Header("Sound (SFX) Toggle")]
    [SerializeField] private Button soundToggleButton;
    [SerializeField] private GameObject soundOnBG;
    [SerializeField] private GameObject soundOffBG;

    [Header("Music Toggle")]
    [SerializeField] private Button musicToggleButton;
    [SerializeField] private GameObject musicOnBG;
    [SerializeField] private GameObject musicOffBG;

    [Header("Vibration Toggle")]
    [SerializeField] private Button vibrationToggleButton;
    [SerializeField] private GameObject vibrationOnBG;
    [SerializeField] private GameObject vibrationOffBG;

    [Header("Volume Slider")]
    [SerializeField] private Slider volumeSlider;

    [Header("Gameplay Buttons")]
    [SerializeField] private GameObject buttonsContainer;
    [SerializeField] private Button homeButton;
    [SerializeField] private GameObject homeImage;
    [SerializeField] private Button restartButton;
    [SerializeField] private GameObject restartImage;

    private const string VibrationPrefKey = "VibrationEnabled";
    private bool isSfxOn = true;
    private bool isMusicOn = true;
    private bool isVibrationOn = true;

    private Sequence panelAnimSequence;
    private Sequence soundToggleSeq;
    private Sequence musicToggleSeq;
    private Sequence vibrationToggleSeq;

    private void Awake()
    {
        Instance = this;
        AutoFindReferences();
        EnsureCanvasGroups();
        RegisterListeners();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnregisterListeners();
        KillAllTweens();
    }

    private void Start()
    {
        // Deactivate gameplay buttons by default on startup
        DeactivateGameplayButtons();
    }

    public void AutoFindReferences()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (panelCanvasGroup == null) panelCanvasGroup = GetOrAddCanvasGroup(panelRoot);

        Transform bg = transform.Find("BG") ?? transform;
        if (closeButton == null && bg != null)
        {
            Transform cb = bg.Find("CloseButton");
            if (cb != null) closeButton = cb.GetComponent<Button>();
        }

        Transform contents = bg != null ? bg.Find("Contents") : null;
        if (contents != null)
        {
            // Sound
            Transform soundToggle = contents.Find("Sound/Content/Text&Button/ToggleButton");
            if (soundToggleButton == null && soundToggle != null) soundToggleButton = soundToggle.GetComponent<Button>();
            if (soundToggle != null)
            {
                if (soundOnBG == null) soundOnBG = soundToggle.Find("ToggleONBG")?.gameObject ?? soundToggle.Find("ToggleOnBG")?.gameObject;
                if (soundOffBG == null) soundOffBG = soundToggle.Find("ToggleOffBG")?.gameObject;
            }

            // Music
            Transform musicToggle = contents.Find("Music/Content/Text&Button/ToggleButton");
            if (musicToggleButton == null && musicToggle != null) musicToggleButton = musicToggle.GetComponent<Button>();
            if (musicToggle != null)
            {
                if (musicOnBG == null) musicOnBG = musicToggle.Find("ToggleONBG")?.gameObject ?? musicToggle.Find("ToggleOnBG")?.gameObject;
                if (musicOffBG == null) musicOffBG = musicToggle.Find("ToggleOffBG")?.gameObject;
            }

            // Vibration
            Transform vibToggle = contents.Find("Vibration/Content/Text&Button/ToggleButton");
            if (vibrationToggleButton == null && vibToggle != null) vibrationToggleButton = vibToggle.GetComponent<Button>();
            if (vibToggle != null)
            {
                if (vibrationOnBG == null) vibrationOnBG = vibToggle.Find("ToggleONBG")?.gameObject ?? vibToggle.Find("ToggleOnBG")?.gameObject;
                if (vibrationOffBG == null) vibrationOffBG = vibToggle.Find("ToggleOffBG")?.gameObject;
            }

            // Volume Slider
            Transform sliderT = contents.Find("Volume/Content/SliderContents/Slider");
            if (volumeSlider == null && sliderT != null) volumeSlider = sliderT.GetComponent<Slider>();

            // Buttons
            Transform btnsT = contents.Find("Buttons");
            if (buttonsContainer == null && btnsT != null) buttonsContainer = btnsT.gameObject;

            if (btnsT != null)
            {
                Transform hbT = btnsT.Find("HomeButton");
                if (hbT != null)
                {
                    if (homeButton == null) homeButton = hbT.GetComponent<Button>() ?? hbT.gameObject.AddComponent<Button>();
                    if (homeImage == null) homeImage = hbT.Find("HOME")?.gameObject;
                    if (homeButton != null && homeImage != null && homeButton.targetGraphic == null)
                        homeButton.targetGraphic = homeImage.GetComponent<Graphic>();
                }

                Transform rbT = btnsT.Find("RestartButton");
                if (rbT != null)
                {
                    if (restartButton == null) restartButton = rbT.GetComponent<Button>() ?? rbT.gameObject.AddComponent<Button>();
                    if (restartImage == null) restartImage = rbT.Find("RESTART")?.gameObject;
                    if (restartButton != null && restartImage != null && restartButton.targetGraphic == null)
                        restartButton.targetGraphic = restartImage.GetComponent<Graphic>();
                }
            }
        }
    }

    private void EnsureCanvasGroups()
    {
        if (soundOnBG != null) GetOrAddCanvasGroup(soundOnBG);
        if (soundOffBG != null) GetOrAddCanvasGroup(soundOffBG);
        if (musicOnBG != null) GetOrAddCanvasGroup(musicOnBG);
        if (musicOffBG != null) GetOrAddCanvasGroup(musicOffBG);
        if (vibrationOnBG != null) GetOrAddCanvasGroup(vibrationOnBG);
        if (vibrationOffBG != null) GetOrAddCanvasGroup(vibrationOffBG);
    }

    private void RegisterListeners()
    {
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
        if (soundToggleButton != null) soundToggleButton.onClick.AddListener(OnSoundToggleClicked);
        if (musicToggleButton != null) musicToggleButton.onClick.AddListener(OnMusicToggleClicked);
        if (vibrationToggleButton != null) vibrationToggleButton.onClick.AddListener(OnVibrationToggleClicked);
        if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
        if (homeButton != null) homeButton.onClick.AddListener(OnHomeClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
    }

    private void UnregisterListeners()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        if (soundToggleButton != null) soundToggleButton.onClick.RemoveListener(OnSoundToggleClicked);
        if (musicToggleButton != null) musicToggleButton.onClick.RemoveListener(OnMusicToggleClicked);
        if (vibrationToggleButton != null) vibrationToggleButton.onClick.RemoveListener(OnVibrationToggleClicked);
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeSliderChanged);
        if (homeButton != null) homeButton.onClick.RemoveListener(OnHomeClicked);
        if (restartButton != null) restartButton.onClick.RemoveListener(OnRestartClicked);
    }

    // ---------------- Public Open / Close API ----------------

    public void OpenSettings(bool isGameplay = false)
    {
        AutoFindReferences();
        KillAllTweens();

        bool isGameplayActive = isGameplay || (SceneManager.GetActiveScene().name == "GameScene");

        if (isGameplayActive)
        {
            if (buttonsContainer != null) buttonsContainer.SetActive(true);
            if (homeButton != null) homeButton.gameObject.SetActive(true);
            if (restartButton != null) restartButton.gameObject.SetActive(true);
            if (homeImage != null) homeImage.SetActive(true);
            if (restartImage != null) restartImage.SetActive(true);
        }
        else
        {
            DeactivateGameplayButtons();
        }

        // Sync states
        SyncAudioAndVibrationStates();

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
            panelRoot.transform.localScale = Vector3.zero;

            if (panelCanvasGroup == null) panelCanvasGroup = GetOrAddCanvasGroup(panelRoot);
            panelCanvasGroup.alpha = 0f;

            panelAnimSequence = DOTween.Sequence()
                .Append(panelRoot.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack))
                .Join(panelCanvasGroup.DOFade(1f, animDuration))
                .SetUpdate(true)
                .SetLink(panelRoot);
        }
    }

    public void CloseSettings(bool immediate = false)
    {
        KillAllTweens();

        // Always deactivate HOME and RESTART image GameObjects when closing
        DeactivateGameplayButtons();

        if (panelRoot == null || !panelRoot.activeSelf) return;

        if (immediate)
        {
            panelRoot.SetActive(false);
            return;
        }

        if (panelCanvasGroup == null) panelCanvasGroup = GetOrAddCanvasGroup(panelRoot);

        panelAnimSequence = DOTween.Sequence()
            .Append(panelRoot.transform.DOScale(0f, animDuration).SetEase(Ease.InBack))
            .Join(panelCanvasGroup.DOFade(0f, animDuration))
            .SetUpdate(true)
            .OnComplete(() =>
            {
                panelRoot.SetActive(false);
                DeactivateGameplayButtons();
            })
            .SetLink(panelRoot);
    }

    public void DeactivateGameplayButtons()
    {
        if (homeImage != null) homeImage.SetActive(false);
        if (restartImage != null) restartImage.SetActive(false);
        if (homeButton != null) homeButton.gameObject.SetActive(false);
        if (restartButton != null) restartButton.gameObject.SetActive(false);
        if (buttonsContainer != null) buttonsContainer.SetActive(false);
    }

    // ---------------- Audio & Vibration Sync ----------------

    public void SyncAudioAndVibrationStates()
    {
        isSfxOn = SoundManager.Instance == null || !SoundManager.Instance.IsSfxMuted;
        isMusicOn = SoundManager.Instance == null || !SoundManager.Instance.IsMusicMuted;
        isVibrationOn = PlayerPrefs.GetInt(VibrationPrefKey, 1) == 1;

        SetToggleInstant(soundOnBG, soundOffBG, isSfxOn, ref soundToggleSeq);
        SetToggleInstant(musicOnBG, musicOffBG, isMusicOn, ref musicToggleSeq);
        SetToggleInstant(vibrationOnBG, vibrationOffBG, isVibrationOn, ref vibrationToggleSeq);

        if (volumeSlider != null)
        {
            float curVol = SoundManager.Instance != null ? SoundManager.Instance.MusicVolume : 1f;
            volumeSlider.SetValueWithoutNotify(curVol);
        }
    }

    // ---------------- Event Handlers ----------------

    private void OnCloseClicked()
    {
        PlayButtonSfx();
        CloseSettings();
    }

    private void OnSoundToggleClicked()
    {
        isSfxOn = !isSfxOn;
        if (isSfxOn) PlayButtonSfx();
        SoundManager.Instance?.SetSfxMuted(!isSfxOn);
        AnimateToggle(soundOnBG, soundOffBG, isSfxOn, ref soundToggleSeq);

        if (UIManager.Instance != null)
            UIManager.Instance.SyncQuickSettingsFromSoundManager();
    }

    private void OnMusicToggleClicked()
    {
        PlayButtonSfx();
        isMusicOn = !isMusicOn;
        SoundManager.Instance?.SetMusicMuted(!isMusicOn);
        AnimateToggle(musicOnBG, musicOffBG, isMusicOn, ref musicToggleSeq);

        if (UIManager.Instance != null)
            UIManager.Instance.SyncQuickSettingsFromSoundManager();
    }

    private void OnVibrationToggleClicked()
    {
        PlayButtonSfx();
        isVibrationOn = !isVibrationOn;
        PlayerPrefs.SetInt(VibrationPrefKey, isVibrationOn ? 1 : 0);
        PlayerPrefs.Save();

        if (isVibrationOn)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        AnimateToggle(vibrationOnBG, vibrationOffBG, isVibrationOn, ref vibrationToggleSeq);

        if (UIManager.Instance != null)
            UIManager.Instance.SyncQuickSettingsFromSoundManager();
    }

    private void OnVolumeSliderChanged(float val)
    {
        // Volume slider controls both music and SFX volume
        SoundManager.Instance?.SetMusicVolume(val);
        SoundManager.Instance?.SetSfxVolume(val);
    }

    private void OnHomeClicked()
    {
        PlayButtonSfx();
        CloseSettings(immediate: true);
        SceneManager.LoadScene("MainMenu");
    }

    private void OnRestartClicked()
    {
        PlayButtonSfx();
        CloseSettings();

        if (SudokuGameManager.Instance != null)
        {
            SudokuGameManager.Instance.RestartCurrentLevel();
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    // ---------------- Smooth Toggle Animation ----------------

    private void AnimateToggle(GameObject onBG, GameObject offBG, bool toOn, ref Sequence seq)
    {
        if (onBG == null || offBG == null) return;
        seq?.Kill();

        CanvasGroup onCG = GetOrAddCanvasGroup(onBG);
        CanvasGroup offCG = GetOrAddCanvasGroup(offBG);

        onBG.SetActive(true);
        offBG.SetActive(true);

        const float duration = 0.22f;

        if (toOn)
        {
            onCG.alpha = 0f;
            offCG.alpha = 1f;

            Transform onKnob = onBG.transform.Find("ON");
            if (onKnob != null) onKnob.localScale = Vector3.one * 0.85f;

            seq = DOTween.Sequence()
                .Join(onCG.DOFade(1f, duration).SetEase(Ease.OutQuad))
                .Join(offCG.DOFade(0f, duration).SetEase(Ease.OutQuad));

            if (onKnob != null)
                seq.Join(onKnob.DOScale(1f, duration).SetEase(Ease.OutBack));

            seq.OnComplete(() =>
            {
                if (offBG != null) offBG.SetActive(false);
            });
        }
        else
        {
            offCG.alpha = 0f;
            onCG.alpha = 1f;

            Transform offKnob = offBG.transform.Find("OFF");
            if (offKnob != null) offKnob.localScale = Vector3.one * 0.85f;

            seq = DOTween.Sequence()
                .Join(offCG.DOFade(1f, duration).SetEase(Ease.OutQuad))
                .Join(onCG.DOFade(0f, duration).SetEase(Ease.OutQuad));

            if (offKnob != null)
                seq.Join(offKnob.DOScale(1f, duration).SetEase(Ease.OutBack));

            seq.OnComplete(() =>
            {
                if (onBG != null) onBG.SetActive(false);
            });
        }

        seq.SetUpdate(true);
        seq.SetLink(gameObject);
    }

    private void SetToggleInstant(GameObject onBG, GameObject offBG, bool isOn, ref Sequence seq)
    {
        seq?.Kill();
        if (onBG != null)
        {
            onBG.SetActive(isOn);
            CanvasGroup cg = GetOrAddCanvasGroup(onBG);
            cg.alpha = isOn ? 1f : 0f;
        }
        if (offBG != null)
        {
            offBG.SetActive(!isOn);
            CanvasGroup cg = GetOrAddCanvasGroup(offBG);
            cg.alpha = !isOn ? 1f : 0f;
        }
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private void PlayButtonSfx()
    {
        SoundManager.Instance?.PlaySFX("Button");
    }

    private void KillAllTweens()
    {
        panelAnimSequence?.Kill();
        soundToggleSeq?.Kill();
        musicToggleSeq?.Kill();
        vibrationToggleSeq?.Kill();
    }
}
