using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

    [Header("Difficulty Panel")]
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private Button backButton;

    [Header("Difficulty Posters")]
    [SerializeField] private Button easyPoster;
    [SerializeField] private Button mediumPoster;
    [SerializeField] private Button hardPoster;

    [Header("Scene Names")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Pulse Settings")]
    [SerializeField] private float pulseScale = 1.1f;
    [SerializeField] private float pulseDuration = 0.6f;

    [Header("Panel Animation Settings")]
    [SerializeField] private float panelAnimDuration = 0.35f;

    // Key used to pass the chosen difficulty to the game scene
    public const string DifficultyPrefKey = "SelectedDifficulty";

    private Tween playButtonPulseTween;
    private Sequence difficultyPanelSequence;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Make sure panel starts hidden and scaled down (in case it was left active in editor)
        if (difficultyPanel != null)
        {
            difficultyPanel.transform.localScale = Vector3.one;
            difficultyPanel.SetActive(false);
        }

        RegisterListeners();
        StartPlayButtonPulse();
    }

    private void RegisterListeners()
    {
        if (playButton != null) playButton.onClick.AddListener(OnPlayClicked);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);

        if (easyPoster != null) easyPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Easy));
        if (mediumPoster != null) mediumPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Medium));
        if (hardPoster != null) hardPoster.onClick.AddListener(() => OnDifficultySelected(Difficulty.Hard));

        // settingsButton / aboutButton onClick hooked up to your own panels if you have them.
        // They're only toggled active/inactive here, per the flow you described.
    }

    private void OnDestroy()
    {
        playButtonPulseTween?.Kill();
        difficultyPanelSequence?.Kill();

        if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
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
        StopPlayButtonPulse();

        if (settingsButton != null) settingsButton.gameObject.SetActive(false);
        if (aboutButton != null) aboutButton.gameObject.SetActive(false);
        if (playButton != null) playButton.gameObject.SetActive(false);

        ShowDifficultyPanel();
    }

    private void OnBackClicked()
    {
        HideDifficultyPanel(() =>
        {
            if (playButton != null) playButton.gameObject.SetActive(true);
            if (settingsButton != null) settingsButton.gameObject.SetActive(true);
            if (aboutButton != null) aboutButton.gameObject.SetActive(true);

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

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // ---------------- Difficulty Selection -> Game Scene ----------------

    private void OnDifficultySelected(Difficulty difficulty)
    {
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