using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
    [SerializeField] private TMP_Text copiedFeedbackText;  // Optional label that flashes "Copied!"
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
    private Coroutine copiedFeedbackCoroutine;

    private void Awake()
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

        // Make the room code label tappable: clicking it copies the code.
        WireRoomCodeCopyButton();

        // Hide copy feedback label initially.
        if (copiedFeedbackText != null)
            copiedFeedbackText.gameObject.SetActive(false);
    }

    // ---- Room code copy ----

    /// <summary>
    /// Adds a transparent Button (or EventTrigger) on the roomCodeText GameObject so
    /// a single tap copies the code to the system clipboard and flashes "Copied!".
    /// </summary>
    private void WireRoomCodeCopyButton()
    {
        if (roomCodeText == null) return;

        // Ensure the image component exists so Button raycasts work.
        var img = roomCodeText.GetComponent<UnityEngine.UI.Image>();
        if (img == null)
        {
            img = roomCodeText.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // fully transparent
        }

        // Add a Button if not already present.
        var btn = roomCodeText.GetComponent<Button>();
        if (btn == null)
            btn = roomCodeText.gameObject.AddComponent<Button>();

        btn.transition = Selectable.Transition.None; // no colour flicker on the text
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnRoomCodeTapped);
    }

    private void OnRoomCodeTapped()
    {
        if (roomCodeText == null) return;
        string code = roomCodeText.text.Trim();
        if (string.IsNullOrEmpty(code) || code == "------") return;

        // Copy to clipboard.
        GUIUtility.systemCopyBuffer = code;

        // Flash "Copied!" feedback.
        if (copiedFeedbackCoroutine != null) StopCoroutine(copiedFeedbackCoroutine);
        copiedFeedbackCoroutine = StartCoroutine(ShowCopiedFeedback());
    }

    private IEnumerator ShowCopiedFeedback()
    {
        if (copiedFeedbackText == null) yield break;
        copiedFeedbackText.text = "Copied!";
        copiedFeedbackText.gameObject.SetActive(true);

        // Fade in.
        CanvasGroup cg = copiedFeedbackText.GetComponent<CanvasGroup>();
        if (cg == null) cg = copiedFeedbackText.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.DOFade(1f, 0.15f).SetLink(copiedFeedbackText.gameObject);

        yield return new WaitForSeconds(1.4f);

        // Fade out.
        cg.DOFade(0f, 0.3f)
          .OnComplete(() => copiedFeedbackText.gameObject.SetActive(false))
          .SetLink(copiedFeedbackText.gameObject);
    }

    // ---- Join input paste support ----

    /// <summary>
    /// Called after the join group becomes active to attach paste-on-hold behaviour
    /// to the room code input field.
    /// </summary>
    private void SetupJoinInputPaste()
    {
        if (roomCodeInput == null) return;

        // On mobile the native keyboard context menu already offers Paste when
        // the user long-presses an InputField, but we make it explicit by also
        // wiring a PointerDown EventTrigger that pastes clipboard text after a hold.
        EventTrigger trigger = roomCodeInput.GetComponent<EventTrigger>();
        if (trigger == null) trigger = roomCodeInput.gameObject.AddComponent<EventTrigger>();

        // Remove any old entries to avoid duplicates.
        trigger.triggers.RemoveAll(e => e.eventID == EventTriggerType.PointerDown
                                     && e.callback.GetPersistentEventCount() == 0);

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        entry.callback.AddListener((_) => StartCoroutine(CheckHoldForPaste()));
        trigger.triggers.Add(entry);
    }

    private Coroutine holdPasteCoroutine;
    private bool pointerStillDown;

    private IEnumerator CheckHoldForPaste()
    {
        pointerStillDown = true;
        float holdTime = 0f;
        bool pasted = false;

        while (pointerStillDown && holdTime < 0.6f)
        {
            holdTime += Time.unscaledDeltaTime;
            yield return null;
        }

        if (pointerStillDown && !pasted)
        {
            string clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip))
            {
                roomCodeInput.text = clip.Trim().ToUpper();
                // Show brief "Pasted" feedback in join status label.
                if (joinStatusText != null)
                {
                    joinStatusText.text = "Pasted from clipboard";
                    yield return new WaitForSeconds(1.5f);
                    if (joinStatusText.text == "Pasted from clipboard")
                        joinStatusText.text = "";
                }
            }
        }
    }

    // Pointer-up listener to cancel the hold check.
    private void Update()
    {
        if (!UnityEngine.Input.GetMouseButton(0) &&
            UnityEngine.Input.touchCount == 0)
        {
            pointerStillDown = false;
        }
    }

    private void OnEnable()
    {
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnRoomCodeGenerated += OnRoomCodeGenerated;
            MultiplayerManager.Instance.OnOpponentJoined    += OnOpponentJoined;
            MultiplayerManager.Instance.OnConnectionFailed  += OnConnectionFailed;
        }
        SetButtonsInteractable(true);
        ShowModeChooser();
    }

    private void OnDisable()
    {
        if (MultiplayerManager.Instance != null)
        {
            MultiplayerManager.Instance.OnRoomCodeGenerated -= OnRoomCodeGenerated;
            MultiplayerManager.Instance.OnOpponentJoined    -= OnOpponentJoined;
            MultiplayerManager.Instance.OnConnectionFailed  -= OnConnectionFailed;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostEasyButton != null)    hostEasyButton.interactable = interactable;
        if (hostMediumButton != null)  hostMediumButton.interactable = interactable;
        if (hostHardButton != null)    hostHardButton.interactable = interactable;
        if (joinConfirmButton != null) joinConfirmButton.interactable = interactable;
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
        SetButtonsInteractable(false);
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

        // Always re-enable the join button in case it was left disabled from a
        // previous attempt that was cancelled before the connection resolved.
        if (joinConfirmButton != null) joinConfirmButton.interactable = true;

        // Wire paste-on-hold for the input field each time the join panel is shown.
        SetupJoinInputPaste();
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
        SetButtonsInteractable(true);
        if (hostStatusText != null) hostStatusText.text = $"Failed: {reason}";
        if (joinStatusText != null) joinStatusText.text = $"Could not join: {reason}";
        if (joinConfirmButton != null) joinConfirmButton.interactable = true;
    }

    private void OnCancelJoinClicked()
    {
        SoundManager.Instance?.PlaySFX("Button");
        MultiplayerManager.Instance?.Disconnect();

        // Restore the join button so it works on the next attempt.
        if (joinConfirmButton != null) joinConfirmButton.interactable = true;
        if (joinStatusText != null)    joinStatusText.text = "";

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
