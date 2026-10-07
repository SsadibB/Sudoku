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
    private bool eventsBound;

    private void Awake()
    {
        // The lobby panel carries a second copy of this component. Both were
        // listening to the same difficulty buttons, so one search started twice
        // and the second call was dropped as "already starting".
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

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
        if (Instance != null && Instance != this) return;
        if (eventsBound) return;

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout += OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed += OnConnectionFailed;
            MultiplayerManager.Instance.OnOpponentJoined += OnOpponentJoined;
            MultiplayerManager.Instance.OnOpponentLeft += HandleOpponentDeparted;
            MultiplayerManager.Instance.OnLocalSessionLost += HandleLocalSessionLost;
            MultiplayerManager.Instance.OnOpponentIdentity += RefreshOpponentIdentity;
            MultiplayerManager.Instance.OnRematchStateChanged += RefreshRematchState;
        }

        if (ProfileManager.Instance != null)
            ProfileManager.Instance.OnLocalIdentityChanged += RefreshLocalSearchIdentity;
        eventsBound = true;
    }

    private void OnDisable()
    {
        if (!eventsBound) return;

        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnMatchmakingTimeout -= OnMatchmakingTimeout;
            MultiplayerManager.Instance.OnConnectionFailed -= OnConnectionFailed;
            MultiplayerManager.Instance.OnOpponentJoined -= OnOpponentJoined;
            MultiplayerManager.Instance.OnOpponentLeft -= HandleOpponentDeparted;
            MultiplayerManager.Instance.OnLocalSessionLost -= HandleLocalSessionLost;
            MultiplayerManager.Instance.OnOpponentIdentity -= RefreshOpponentIdentity;
            MultiplayerManager.Instance.OnRematchStateChanged -= RefreshRematchState;
        }
        if (ProfileManager.Instance != null)
            ProfileManager.Instance.OnLocalIdentityChanged -= RefreshLocalSearchIdentity;
        eventsBound = false;
        StopSpinner();
        CancelAutoStart();
    }

    public void Show()
    {
        if (lobbyPanel == null) return;
        lobbyPanel.SetActive(true);
        lobbyPanel.transform.SetAsLastSibling();

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
        OpenSearching(RematchWaitingStatus(), autoStart: false);
        RefreshRematchState();
    }

    public void ShowCompetitionHostWaiting(string roomCode)
    {
        // The host stays on HostWaitGroup. The searching panel is only for the joining player.
        searchContext = SearchContext.None;
        opponentFound = false;
        if (searchingGroup != null) searchingGroup.SetActive(false);
        SetStartGameVisible(false);
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

    public void PresentCompetitionOpponent()
    {
        if (searchContext != SearchContext.Competition)
            ShowCompetitionSearching();
        PresentOpponentFound();
    }

    public void PresentOpponentFound()
    {
        if (searchContext == SearchContext.Rematch)
        {
            var mp = MultiplayerManager.Instance;
            if (mp == null || !mp.IsExpectedRematchOpponent())
                return;
        }

        bool already = opponentFound;
        opponentFound = true;
        SetGroupActive(searchingGroup, true);
        if (searchingStatusText != null && searchContext != SearchContext.Rematch)
            searchingStatusText.text = "Opponent Found";
        ApplyFoundPlayers();

        bool hostMayStart = false;
        SetStartGameVisible(hostMayStart && IsLocalHost());

        if (searchContext == SearchContext.Rematch && searchingStatusText != null)
            searchingStatusText.text = "Starting rematch...";

        if (!already && searchContext == SearchContext.International)
            BeginAutoStart();
    }

    private void Update()
    {
        if (opponentFound || searchContext == SearchContext.None) return;
        if (searchingGroup == null || !searchingGroup.activeInHierarchy) return;

        var runner = MultiplayerManager.Instance != null ? MultiplayerManager.Instance.Runner : null;
        if (runner == null || !runner.IsRunning) return;

        if (searchContext == SearchContext.Rematch)
        {
            var mp = MultiplayerManager.Instance;
            if (!opponentFound && mp != null && mp.IsExpectedRematchOpponent())
                PresentOpponentFound();
            return;
        }

        int count = 0;
        foreach (var _ in runner.ActivePlayers) count++;
        if (count >= 2)
            PresentOpponentFound();
    }

    private void LateUpdate()
    {
        if (searchContext == SearchContext.Competition)
        {
            SetStartGameVisible(false);
            if (opponentFound && searchingGroup != null && searchingGroup.activeInHierarchy)
                ApplyFoundPlayers();
            return;
        }

        if (searchContext != SearchContext.International && searchContext != SearchContext.Rematch)
            return;
        if (searchingGroup == null || !searchingGroup.activeInHierarchy) return;

        var remote = NetworkSudokuPlayer.Remote;
        bool remoteReady = remote != null && remote.Object != null && remote.Object.IsValid;
        var mp = MultiplayerManager.Instance;
        bool named = mp != null && !string.IsNullOrWhiteSpace(mp.OpponentName);
        if (!opponentFound && !remoteReady && !named) return;

        if (searchVisuals == null)
            searchVisuals = PlayerSearchVisuals.Bind(searchingGroup.transform);
        if (searchingStatusText == null) EnsureReferences();

        if (!opponentFound)
            PresentOpponentFound();
        else
            ApplyFoundPlayers();
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
        if (MultiplayerManager.Instance != null && !MultiplayerManager.Instance.OpponentStillConnected())
            return;
        if (startGameButton != null) startGameButton.interactable = false;
        CancelAutoStart();
        MultiplayerManager.Instance?.HostStartMatch();
    }

    private void OnOpponentJoined()
    {
        if (searchContext == SearchContext.None) return;
        PresentOpponentFound();
    }

    private void HandleOpponentDeparted()
    {
        if (searchContext == SearchContext.None || !opponentFound) return;
        CancelAutoStart();
        opponentFound = false;
        SetStartGameVisible(false);
        if (searchingStatusText != null)
            searchingStatusText.text = "Opponent disconnected";
    }

    private void HandleLocalSessionLost()
    {
        CancelAutoStart();
        StopSpinner();
        searchVisuals?.Stop();
        opponentFound = false;
        if (searchingStatusText != null)
            searchingStatusText.text = "Connection lost";
    }

    private void RefreshOpponentIdentity()
    {
        if (!opponentFound) return;
        ApplyFoundPlayers();
    }

    private void RefreshLocalSearchIdentity()
    {
        if (searchingGroup != null && searchingGroup.activeInHierarchy)
            ShowLocalPlayer();
        if (opponentFound)
            ApplyFoundPlayers();
    }

    private void RefreshRematchState()
    {
        if (searchContext != SearchContext.Rematch) return;

        var mp = MultiplayerManager.Instance;
        if (mp != null && mp.IsExpectedRematchOpponent())
            PresentOpponentFound();
        else
        {
            opponentFound = false;
            searchVisuals?.BeginSearching();
            if (searchingStatusText != null)
                searchingStatusText.text = RematchWaitingStatus();
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
        string opponentName = mp != null ? mp.OpponentName : null;
        if (!IsDisplayName(opponentName))
        {
            var remote = NetworkSudokuPlayer.Remote;
            if (remote != null && remote.Object != null && remote.Object.IsValid)
            {
                try
                {
                    string networked = remote.PlayerName.ToString();
                    if (IsDisplayName(networked))
                        opponentName = networked;
                }
                catch { }
            }
        }
        if (!IsDisplayName(opponentName) && mp != null)
            opponentName = mp.RematchOpponentName;
        Sprite opponentAvatar = mp != null ? mp.GetOpponentAvatar() : null;
        bool waitingForPhoto = mp != null && opponentAvatar == null
            && (!string.IsNullOrEmpty(mp.OpponentAvatarUrl) || mp.OpponentAvatarIndex < 0);
        if (!waitingForPhoto && opponentAvatar == null && mp != null && mp.RematchOpponentAvatar >= 0 && ProfileManager.Instance != null)
            opponentAvatar = ProfileManager.Instance.GetPresetAvatar(mp.RematchOpponentAvatar);
        searchVisuals?.ShowOpponent(opponentName, opponentAvatar, true);
        AttachOpponentProfile(opponentName, opponentAvatar);
        if (opponentAvatar != null)
            mp.PaintOpponentAvatars();

        if (searchingStatusText != null)
            searchingStatusText.text = "Opponent Found";
    }

    // Writes the found opponent straight onto the searching card:
    // the avatar image and OpponentName_Text.
    private void AttachOpponentProfile(string fullName, Sprite avatar)
    {
        if (searchingGroup == null) return;

        if (avatar != null)
        {
            Transform avatarObject = FindDeep(searchingGroup.transform, "OpponentAvatarImage");
            if (avatarObject != null)
                MultiplayerManager.AssignExistingOpponentImage(avatarObject.GetComponent<Image>(), avatar);
        }

        Transform opponent = FindDeep(searchingGroup.transform, "Opponent");
        if (opponent == null) return;

        string shown = PlayerSearchVisuals.VisibleName(fullName);
        if (!string.IsNullOrEmpty(shown))
        {
            TMP_Text[] texts = opponent.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) continue;
                if (!texts[i].gameObject.name.StartsWith("OpponentName_Text", System.StringComparison.Ordinal))
                    continue;
                texts[i].text = shown;
                texts[i].gameObject.SetActive(true);
                ProfileManager.FitNameLabel(texts[i]);
            }
        }
    }

    private void ShowLocalPlayer()
    {
        var mp = MultiplayerManager.Instance;
        string localName = mp != null ? mp.LocalPlayerName : "You";
        localName = ProfileManager.ShortDisplayName(localName);
        if (string.IsNullOrEmpty(localName)) localName = "You";
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
        var mp = MultiplayerManager.Instance;
        if (mp == null || !mp.OpponentStillConnected()) yield break;
        mp.HostStartMatch();
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

    private string RematchWaitingStatus()
    {
        var mp = MultiplayerManager.Instance;
        if (mp != null && IsDisplayName(mp.RematchOpponentName))
            return $"Waiting for {mp.RematchOpponentName} to rematch...";
        return "Waiting for your opponent to rematch...";
    }

    private static bool IsDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Equals("Opponent", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Player", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("You", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Searching...", System.StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private bool IsLocalHost()
    {
        var mp = MultiplayerManager.Instance;
        if (mp == null) return false;
        if (searchContext == SearchContext.Competition)
            return mp.IsHost;
        if (searchContext == SearchContext.Rematch)
        {
            if (mp.IsCompetitionMatch)
                return mp.IsHost;
            return mp.Runner != null && mp.Runner.IsSharedModeMasterClient;
        }
        if (mp.IsHost) return true;
        return mp.Runner != null && mp.Runner.IsSharedModeMasterClient;
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
        searchingSpinner.gameObject.SetActive(true);
        searchingSpinner.localRotation = Quaternion.identity;
        spinnerTween = searchingSpinner
            .DORotate(new Vector3(0f, 0f, -360f), 1f, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart)
            .SetLink(searchingSpinner.gameObject);
    }

    private void StopSpinner()
    {
        spinnerTween?.Kill();
        spinnerTween = null;
        if (searchingSpinner != null)
            searchingSpinner.gameObject.SetActive(false);
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
        if (searchContext == SearchContext.Competition) return;
        if (startGameButton != null || searchingGroup == null) return;

        Transform existing = FindDeep(searchingGroup.transform, "StartGameButton");
        if (existing == null)
            existing = FindDeep(searchingGroup.transform, "StartButton");
        if (existing != null)
        {
            startGameButton = existing.GetComponent<Button>();
            if (startGameButton != null)
                startGameButton.gameObject.SetActive(false);
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

        Transform avatar = FindNamed(root, "OpponentAvatarImage");

        if (avatar != null)
        {
            visuals.AvatarImage = avatar.GetComponent<Image>();

            Transform ring = FindNamed(avatar, "CircleLoading");
            if (ring != null)
                visuals.CircleLoading = ring.GetComponent<Image>();
        }

        Transform search = FindNamedPrefix(root, "OpponentName_Text");
        if (search == null)
            search = FindNamed(root, "SearchText");

        if (search != null)
            visuals.SearchText = search.GetComponent<TMP_Text>() ?? search.GetComponentInChildren<TMP_Text>(true);

        Transform spinner = FindNamed(root, "CircleSpinner");
        if (spinner != null) visuals.CircleSpinner = spinner as RectTransform;

        Transform player = FindNamed(root, "Player");
        if (player != null)
        {
            Transform playerImage = FindNamed(player, "AvatarImage");
            if (playerImage == null)
                playerImage = FindNamed(player, "Image");
            if (playerImage == null)
                playerImage = FindNamed(player, "PlayerImage");
            if (playerImage != null) visuals.LocalAvatar = ProfileManager.ApplyCircularMask(playerImage.GetComponent<Image>());

            Transform playerName = FindNamed(root, "PlayerName_Text");
            if (playerName != null)
                visuals.LocalNameText = playerName.GetComponent<TMP_Text>() ?? playerName.GetComponentInChildren<TMP_Text>(true);
            if (visuals.LocalNameText == null)
                visuals.LocalNameText = FindSideName(player);
        }

        visuals.ConfigureRing();
        return visuals;
    }

    public void ShowLocal(string playerName, Sprite avatar)
    {
        if (LocalNameText != null && !string.IsNullOrEmpty(playerName))
        {
            LocalNameText.text = ProfileManager.ShortDisplayName(playerName);
            ProfileManager.FitNameLabel(LocalNameText);
        }
        LocalAvatar = ProfileManager.ApplyCircularMask(LocalAvatar);
        if (LocalAvatar != null && avatar != null)
        {
            LocalAvatar.sprite = avatar;
            LocalAvatar.color = Color.white;
            LocalAvatar.enabled = true;
            LocalAvatar.preserveAspect = false;
            LocalAvatar.gameObject.SetActive(true);
        }
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

        if (SearchText != null)
            SearchText.text = "Searching...";

        StartSpin();
    }

    public void ShowOpponent(string opponentName, Sprite avatar, bool writeNameLabel)
    {
        StopSpin();

        if (CircleLoading != null)
            CircleLoading.gameObject.SetActive(false);

        if (CircleSpinner != null)
            CircleSpinner.gameObject.SetActive(false);

        if (SearchText != null && writeNameLabel)
        {
            string shown = VisibleName(opponentName);
            if (!string.IsNullOrEmpty(shown))
            {
                SearchText.text = shown;
                ProfileManager.FitNameLabel(SearchText);
                SearchText.gameObject.SetActive(true);
            }
        }

        if (AvatarImage == null || avatar == null) return;
        MultiplayerManager.AssignExistingOpponentImage(AvatarImage, avatar);
    }

    // Name label on a player card. Prefer the text that sits with the avatar
    // (PlayerName_Text / OpponentName_Text). Skip the static "You" / "Opponent"
    // caption parented under WhoText when a dedicated name label exists.
    public static string VisibleName(string name)
    {
        if (!IsShownPlayerName(name)) return "";
        string shown = ProfileManager.ShortDisplayName(name);
        if (!string.IsNullOrEmpty(shown)) return shown;
        return name.Trim().Replace('_', ' ');
    }

    private static bool IsShownPlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Equals("Opponent", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Player", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("You", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("Searching...", System.StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static TMP_Text FindSideName(Transform side)
    {
        if (side == null) return null;

        TMP_Text[] texts = side.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text named = null;
        TMP_Text outsideWho = null;
        TMP_Text any = null;
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null) continue;
            string objectName = texts[i].gameObject.name;
            if (objectName.StartsWith("OpponentName", System.StringComparison.Ordinal)
                || objectName.StartsWith("PlayerName", System.StringComparison.Ordinal))
            {
                named = texts[i];
                break;
            }

            if (objectName.IndexOf("Who", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            if (any == null)
                any = texts[i];

            if (outsideWho == null && !IsUnderNamed(texts[i].transform, side, "WhoText"))
                outsideWho = texts[i];
        }

        return named != null ? named : (outsideWho != null ? outsideWho : any);
    }

    private static void HideGenericOpponentCaption(Transform nameLabel)
    {
        Transform side = nameLabel;
        for (int i = 0; i < 8 && side != null; i++)
        {
            if (side.name == "Opponent") break;
            side = side.parent;
        }
        if (side == null || side.name != "Opponent") return;

        TMP_Text[] texts = side.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null || texts[i].transform == nameLabel) continue;
            string value = texts[i].text != null ? texts[i].text.Trim() : "";
            if (value.Equals("Opponent", System.StringComparison.OrdinalIgnoreCase))
                texts[i].gameObject.SetActive(false);
        }
    }

    private static Transform FindNamedPrefix(Transform root, string name)
    {
        Transform exact = FindNamed(root, name);
        if (exact != null) return exact;
        if (root == null) return null;
        if (root.name.StartsWith(name, System.StringComparison.Ordinal)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamedPrefix(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsUnderNamed(Transform node, Transform stop, string ancestorName)
    {
        Transform current = node;
        while (current != null && current != stop)
        {
            if (current.name == ancestorName) return true;
            current = current.parent;
        }
        return false;
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
