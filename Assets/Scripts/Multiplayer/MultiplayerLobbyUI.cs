using System.Collections;
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
    public enum SearchContext { None, International, Competition, Rematch }

    public static MultiplayerLobbyUI Instance { get; private set; }

    private const float InternationalAutoStartDelay = 3f;

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
    [SerializeField] private Button startGameButton;

    [Header("Competition Sub-Panel")]
    [SerializeField] private CompetitionRoomUI competitionRoomUI;

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.35f;

    private Tween spinnerTween;
    private PlayerSearchVisuals searchVisuals;
    private SearchContext searchContext = SearchContext.None;
    private Coroutine autoStartCoroutine;
    private bool opponentFound;

    private void Awake()
    {
        Instance = this;
        EnsureReferences();

        if (internationalButton != null) internationalButton.onClick.AddListener(OnInternationalClicked);
        if (competitionButton != null) competitionButton.onClick.AddListener(OnCompetitionClicked);
        if (backToMainMenuButton != null) backToMainMenuButton.onClick.AddListener(OnBackToMainMenuClicked);

        if (intlEasyButton != null) intlEasyButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Easy));
        if (intlMediumButton != null) intlMediumButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Medium));
        if (intlHardButton != null) intlHardButton.onClick.AddListener(() => OnIntlDifficultySelected(UIManager.Difficulty.Hard));
        if (intlDifficultyBackButton != null) intlDifficultyBackButton.onClick.AddListener(ShowModePanel);

        if (cancelSearchButton != null) cancelSearchButton.onClick.AddListener(OnCancelSearch);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGameClicked);

        if (searchingGroup != null)
            searchVisuals = PlayerSearchVisuals.Bind(searchingGroup.transform);
    }

    private void Start()
    {
        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.WantsRematchLobby)
            ShowRematchLobby();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout += OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed += OnConnectionFailed;
            MultiplayerManager.Instance.OnOpponentJoined += OnOpponentJoined;
            MultiplayerManager.Instance.OnOpponentIdentity += RefreshOpponentIdentity;
            MultiplayerManager.Instance.OnRematchStateChanged += RefreshRematchState;
        }
    }

    private void OnDisable()
    {
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout -= OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed -= OnConnectionFailed;
            MultiplayerManager.Instance.OnOpponentJoined -= OnOpponentJoined;
            MultiplayerManager.Instance.OnOpponentIdentity -= RefreshOpponentIdentity;
            MultiplayerManager.Instance.OnRematchStateChanged -= RefreshRematchState;
        }
        StopSpinner();
        CancelAutoStart();
    }

    public void Show()
    {
        if (lobbyPanel == null) return;
        lobbyPanel.SetActive(true);

        RectTransform rt = lobbyPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            Vector3 pos = rt.anchoredPosition3D;
            pos.z = 0f;
            rt.anchoredPosition3D = pos;
        }

        lobbyPanel.transform.localScale = Vector3.zero;

        CanvasGroup cg = GetOrAddCG(lobbyPanel);
        cg.alpha = 0f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

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
        CancelAutoStart();
        searchContext = SearchContext.None;
        opponentFound = false;
        SetGroupActive(modePanelGroup, true);
        SetGroupActive(intlDifficultyGroup, false);
        SetGroupActive(searchingGroup, false);
        if (competitionRoomUI != null) competitionRoomUI.gameObject.SetActive(false);
        SetStartGameVisible(false);
    }

    public void ShowRematchLobby()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        searchContext = SearchContext.Rematch;
        SetGroupActive(modePanelGroup, false);
        SetGroupActive(intlDifficultyGroup, false);
        if (competitionRoomUI != null) competitionRoomUI.gameObject.SetActive(false);
        OpenSearching("Waiting for your opponent to rematch...", autoStart: false);
        RefreshRematchState();
    }

    public void ShowCompetitionSearching()
    {
        searchContext = SearchContext.Competition;
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        SetGroupActive(modePanelGroup, false);
        SetGroupActive(intlDifficultyGroup, false);
        if (competitionRoomUI != null) competitionRoomUI.HidePanelsForSearch();
        OpenSearching("Searching for the host...", autoStart: false);
    }

    public void PresentOpponentFound()
    {
        opponentFound = true;
        ApplyFoundPlayers();

        bool showStart = searchContext == SearchContext.Competition || searchContext == SearchContext.Rematch;
        if (showStart)
            SetStartGameVisible(IsLocalHost());
        else
            SetStartGameVisible(false);

        if (searchContext == SearchContext.International)
            BeginAutoStart();
    }

    private void OnInternationalClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        ButtonSelectionVisual.Apply(internationalButton, internationalButton, competitionButton);
        SetGroupActive(modePanelGroup, false);
        SetGroupActive(intlDifficultyGroup, true);
        ButtonSelectionVisual.Apply(null, intlEasyButton, intlMediumButton, intlHardButton);
    }

    private void OnCompetitionClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        ButtonSelectionVisual.Apply(competitionButton, internationalButton, competitionButton);
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
        Hide(() => UIManager.Instance?.RestoreMainMenuButtons());
    }

    private void OnIntlDifficultySelected(UIManager.Difficulty difficulty)
    {
        SoundManager.Instance?.PlaySFX(UIManager.GetDifficultyButtonSfxId(difficulty));
        Button selected = difficulty == UIManager.Difficulty.Easy ? intlEasyButton
            : difficulty == UIManager.Difficulty.Medium ? intlMediumButton
            : intlHardButton;
        ButtonSelectionVisual.Apply(selected, intlEasyButton, intlMediumButton, intlHardButton);

        searchContext = SearchContext.International;
        SetGroupActive(intlDifficultyGroup, false);
        OpenSearching($"Searching for a {difficulty} opponent…", autoStart: false);
        MultiplayerManager.Instance?.StartInternational(difficulty);
    }

    private void OnCancelSearch()
    {
        SoundManager.Instance?.PlaySFX("Button");
        CancelAutoStart();
        MultiplayerManager.Instance?.Disconnect();
        StopSpinner();
        searchVisuals?.Stop();

        if (searchContext == SearchContext.Competition && competitionRoomUI != null)
        {
            SetGroupActive(searchingGroup, false);
            competitionRoomUI.gameObject.SetActive(true);
            competitionRoomUI.ReturnToHostJoin();
            searchContext = SearchContext.None;
            return;
        }

        if (searchContext == SearchContext.Rematch)
        {
            searchContext = SearchContext.None;
            Hide(() => UIManager.Instance?.RestoreMainMenuButtons());
            return;
        }

        ShowModePanel();
    }

    private void OnStartGameClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        if (!IsLocalHost()) return;
        if (startGameButton != null) startGameButton.interactable = false;
        CancelAutoStart();
        MultiplayerManager.Instance?.HostStartMatch();
    }

    private void OnOpponentJoined()
    {
        if (searchContext == SearchContext.None) return;
        PresentOpponentFound();
    }

    private void RefreshOpponentIdentity()
    {
        if (!opponentFound) return;
        ApplyFoundPlayers();
    }

    private void RefreshRematchState()
    {
        if (searchContext != SearchContext.Rematch) return;

        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.BothWantRematch)
            PresentOpponentFound();
        else
        {
            opponentFound = false;
            searchVisuals?.BeginSearching();
            if (searchingStatusText != null)
                searchingStatusText.text = "Waiting for your opponent to rematch...";
            SetStartGameVisible(false);
        }
    }

    private void OnMatchmakingTimeout()
    {
        StopSpinner();
        searchVisuals?.Stop();
        CancelAutoStart();
        if (searchingStatusText != null) searchingStatusText.text = "No opponent found. Try again later.";
    }

    private void OnConnectionFailed(string reason)
    {
        StopSpinner();
        searchVisuals?.Stop();
        CancelAutoStart();
        if (searchingStatusText != null) searchingStatusText.text = $"Connection failed: {reason}";
        if (startGameButton != null) startGameButton.interactable = true;
    }

    private void OpenSearching(string status, bool autoStart)
    {
        opponentFound = false;
        SetGroupActive(searchingGroup, true);
        if (searchingGroup != null && searchVisuals == null)
            searchVisuals = PlayerSearchVisuals.Bind(searchingGroup.transform);

        if (searchingStatusText != null) searchingStatusText.text = status;
        ShowLocalPlayer();
        searchVisuals?.BeginSearching();
        SetStartGameVisible(false);
        StartSpinner();

        if (autoStart) BeginAutoStart();
    }

    private void ApplyFoundPlayers()
    {
        StopSpinner();
        ShowLocalPlayer();

        var mp = MultiplayerManager.Instance;
        string opponentName = mp != null && !string.IsNullOrEmpty(mp.OpponentName) ? mp.OpponentName : "Opponent";
        Sprite opponentAvatar = mp != null ? mp.GetOpponentAvatar() : null;
        searchVisuals?.ShowOpponent(opponentName, opponentAvatar, false);

        if (searchingStatusText != null)
            searchingStatusText.text = opponentName;
    }

    private void ShowLocalPlayer()
    {
        var mp = MultiplayerManager.Instance;
        string localName = mp != null ? mp.LocalPlayerName : "You";
        Sprite localAvatar = ProfileManager.Instance != null ? ProfileManager.Instance.CurrentAvatarSprite : null;
        searchVisuals?.ShowLocal(localName, localAvatar);
    }

    private void BeginAutoStart()
    {
        CancelAutoStart();
        autoStartCoroutine = StartCoroutine(AutoStartRoutine());
    }

    private IEnumerator AutoStartRoutine()
    {
        yield return new WaitForSeconds(InternationalAutoStartDelay);
        autoStartCoroutine = null;
        MultiplayerManager.Instance?.HostStartMatch();
    }

    private void CancelAutoStart()
    {
        if (autoStartCoroutine != null)
        {
            StopCoroutine(autoStartCoroutine);
            autoStartCoroutine = null;
        }
    }

    private void SetStartGameVisible(bool visible)
    {
        if (startGameButton == null) EnsureStartButton();
        if (startGameButton == null) return;
        startGameButton.gameObject.SetActive(visible);
        startGameButton.interactable = visible;
    }

    private bool IsLocalHost()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null) return false;
        if (mp.IsHost) return true;
        return mp.Runner != null && mp.Runner.IsSharedModeMasterClient && searchContext != SearchContext.Competition;
    }

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
        searchingSpinner.gameObject.SetActive(false);
    }

    private void StopSpinner()
    {
        spinnerTween?.Kill();
        spinnerTween = null;
    }

    private void EnsureReferences()
    {
        if (lobbyPanel == null)
        {
            var found = GameObject.Find("MultiplayerLobbyPanel");
            if (found != null) lobbyPanel = found;
        }

        if (searchingGroup == null && lobbyPanel != null)
        {
            Transform t = lobbyPanel.transform.Find("SearchingGroup");
            if (t != null) searchingGroup = t.gameObject;
        }

        if (cancelSearchButton == null && searchingGroup != null)
        {
            Transform t = FindDeep(searchingGroup.transform, "CancelSearchButton");
            if (t != null) cancelSearchButton = t.GetComponent<Button>();
        }

        if (searchingStatusText == null && searchingGroup != null)
        {
            Transform t = FindDeep(searchingGroup.transform, "SearchingStatusText");
            if (t != null) searchingStatusText = t.GetComponent<TMP_Text>();
        }

        EnsureStartButton();
    }

    private void EnsureStartButton()
    {
        if (startGameButton != null || searchingGroup == null) return;

        Transform existing = FindDeep(searchingGroup.transform, "StartGameButton");
        if (existing != null)
        {
            startGameButton = existing.GetComponent<Button>();
            return;
        }

        if (cancelSearchButton == null) return;

        var clone = Instantiate(cancelSearchButton.gameObject, cancelSearchButton.transform.parent);
        clone.name = "StartGameButton";
        startGameButton = clone.GetComponent<Button>();
        if (startGameButton != null) startGameButton.onClick.RemoveAllListeners();

        var label = clone.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Start Game";

        clone.SetActive(false);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}

/// <summary>
/// Opponent slot used while waiting for a match: AvatarImage plus a rotating
/// CircleLoading wedge, and SearchText for the opponent's name.
/// </summary>
public class PlayerSearchVisuals
{
    public Image AvatarImage;
    public Image CircleLoading;
    public RectTransform CircleSpinner;
    public TMP_Text SearchText;
    public Image LocalAvatar;
    public TMP_Text LocalNameText;

    private Tween spinTween;

    public static PlayerSearchVisuals Bind(Transform root)
    {
        var visuals = new PlayerSearchVisuals();
        if (root == null) return visuals;

        Transform avatar = FindNamed(root, "AvatarImage");
        if (avatar == null)
        {
            Transform opponent = FindNamed(root, "Opponent");
            if (opponent != null)
            {
                Transform image = FindNamed(opponent, "Image");
                if (image != null)
                {
                    image.name = "AvatarImage";
                    avatar = image;
                }
            }
        }

        if (avatar == null)
            avatar = CreateOpponentSlot(root);

        if (avatar != null)
        {
            visuals.AvatarImage = avatar.GetComponent<Image>();
            if (visuals.AvatarImage == null)
                visuals.AvatarImage = avatar.GetComponentInChildren<Image>(true);

            Transform ring = avatar.Find("CircleLoading");
            if (ring == null)
                ring = CreateCircleLoading(visuals.AvatarImage != null ? visuals.AvatarImage.transform : avatar);

            if (ring != null)
                visuals.CircleLoading = ring.GetComponent<Image>();
        }

        Transform search = FindNamed(root, "SearchText");
        if (search == null)
        {
            Transform opponent = FindNamed(root, "Opponent");
            if (opponent != null)
            {
                TMP_Text[] texts = opponent.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i].gameObject.name.Contains("Who")) continue;
                    search = texts[i].transform;
                    break;
                }
            }
        }

        if (search != null)
            visuals.SearchText = search.GetComponent<TMP_Text>();

        Transform spinner = null;
        Transform opponentRoot = FindNamed(root, "Opponent");
        if (opponentRoot != null) spinner = FindNamed(opponentRoot, "CircleSpinner");
        if (spinner == null) spinner = FindNamed(root, "CircleSpinner");
        if (spinner != null) visuals.CircleSpinner = spinner as RectTransform;

        Transform player = FindNamed(root, "Player");
        if (player != null)
        {
            Transform playerImage = FindNamed(player, "Image");
            if (playerImage != null) visuals.LocalAvatar = playerImage.GetComponent<Image>();
            TMP_Text[] texts = player.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].gameObject.name.Contains("Who")) continue;
                visuals.LocalNameText = texts[i];
                break;
            }
        }

        visuals.ConfigureRing();
        return visuals;
    }

    public void ShowLocal(string playerName, Sprite avatar)
    {
        if (LocalNameText != null && !string.IsNullOrEmpty(playerName))
            LocalNameText.text = playerName;
        if (LocalAvatar != null && avatar != null)
            LocalAvatar.sprite = avatar;
    }

    public void BeginSearching()
    {
        if (CircleLoading != null)
        {
            CircleLoading.gameObject.SetActive(true);
            CircleLoading.fillAmount = 0.2f;
        }

        if (CircleSpinner != null)
            CircleSpinner.gameObject.SetActive(true);

        StartSpin();
    }

    public void ShowOpponent(string opponentName, Sprite avatar, bool writeNameLabel)
    {
        StopSpin();

        if (CircleLoading != null)
            CircleLoading.gameObject.SetActive(false);

        if (CircleSpinner != null)
            CircleSpinner.gameObject.SetActive(false);

        if (writeNameLabel && SearchText != null)
            SearchText.text = string.IsNullOrEmpty(opponentName) ? "Opponent" : opponentName;

        if (AvatarImage != null && avatar != null)
            AvatarImage.sprite = avatar;
    }

    public void Stop()
    {
        StopSpin();
    }

    private void ConfigureRing()
    {
        if (CircleLoading == null) return;

        CircleLoading.type = Image.Type.Filled;
        CircleLoading.fillMethod = Image.FillMethod.Radial360;
        CircleLoading.fillOrigin = (int)Image.Origin360.Top;
        CircleLoading.fillAmount = 0.2f;
        CircleLoading.raycastTarget = false;
    }

    private void StartSpin()
    {
        RectTransform target = CircleSpinner != null
            ? CircleSpinner
            : (CircleLoading != null ? CircleLoading.rectTransform : null);
        if (target == null) return;

        StopSpin();
        target.localRotation = Quaternion.identity;
        spinTween = target
            .DORotate(new Vector3(0f, 0f, -360f), 1f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart)
            .SetLink(target.gameObject);
    }

    private void StopSpin()
    {
        spinTween?.Kill();
        spinTween = null;
    }

    private static Transform CreateCircleLoading(Transform parent)
    {
        if (parent == null) return null;

        var go = new GameObject("CircleLoading", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-8f, -8f);
        rt.offsetMax = new Vector2(8f, 8f);

        var image = go.GetComponent<Image>();
        image.color = new Color(0.85f, 0.85f, 0.88f, 0.95f);
        image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        return go.transform;
    }

    private static Transform CreateOpponentSlot(Transform root)
    {
        var avatarGo = new GameObject("AvatarImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        avatarGo.transform.SetParent(root, false);
        var avatarRt = avatarGo.GetComponent<RectTransform>();
        avatarRt.anchorMin = new Vector2(0.5f, 0.5f);
        avatarRt.anchorMax = new Vector2(0.5f, 0.5f);
        avatarRt.sizeDelta = new Vector2(150f, 150f);
        avatarRt.anchoredPosition = new Vector2(0f, -210f);

        var image = avatarGo.GetComponent<Image>();
        image.color = new Color(0.82f, 0.82f, 0.84f, 1f);
        image.raycastTarget = false;

        var textGo = new GameObject("SearchText", typeof(RectTransform));
        textGo.transform.SetParent(root, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 0.5f);
        textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.sizeDelta = new Vector2(420f, 56f);
        textRt.anchoredPosition = new Vector2(0f, -310f);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 32f;
        tmp.color = new Color(0.25f, 0.16f, 0.08f, 1f);
        tmp.text = "Searching...";
        tmp.raycastTarget = false;

        return avatarGo.transform;
    }

    private static Transform FindNamed(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
