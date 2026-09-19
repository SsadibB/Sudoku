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
    [SerializeField] private GameObject bg1;

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
    private Coroutine pastedFeedbackCoroutine;
    private Coroutine holdHintCoroutine;

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

        // Ensure the "Copied" feedback text component is ready
        EnsureCopiedFeedbackText();

        // Tap & hold the generated room code -> automatically copy the code
        WireRoomCodeCopy();

        // Tap the Enter Code input field -> automatically paste the copied code
        SetupJoinInputPaste();

        // Hide copy feedback label initially.
        if (copiedFeedbackText != null)
            copiedFeedbackText.gameObject.SetActive(false);
    }

    // ---- Room code tap & hold copy ----

    private void WireRoomCodeCopy()
    {
        if (roomCodeText == null) return;

        roomCodeText.raycastTarget = true;

        // Remove any old Button component so it does not intercept pointer events
        var oldBtn = roomCodeText.GetComponent<Button>();
        if (oldBtn != null)
        {
            if (Application.isPlaying) Destroy(oldBtn);
            else DestroyImmediate(oldBtn);
        }

        var holdTrigger = roomCodeText.GetComponent<RoomCodeHoldTrigger>();
        if (holdTrigger == null)
        {
            holdTrigger = roomCodeText.gameObject.AddComponent<RoomCodeHoldTrigger>();
        }

        holdTrigger.holdDuration = 0.45f;
        holdTrigger.onPointerDownAction = () =>
        {
            roomCodeText.transform.DOScale(0.94f, 0.12f).SetLink(roomCodeText.gameObject);
        };
        holdTrigger.onPointerUpAction = (wasHoldTriggered) =>
        {
            roomCodeText.transform.DOScale(1f, 0.1f).SetLink(roomCodeText.gameObject);
            if (!wasHoldTriggered)
            {
                ShowHoldHint();
            }
        };
        holdTrigger.onHoldComplete = OnRoomCodeHoldSuccess;
    }

    private void OnRoomCodeHoldSuccess()
    {
        if (roomCodeText == null) return;
        string code = roomCodeText.text.Trim();
        if (string.IsNullOrEmpty(code) || code == "------") return;

        // Copy via cross-platform ClipboardHelper (Android native JNI, Unity buffer, session cache)
        ClipboardHelper.CopyToClipboard(code);

        // Tactile punch animation
        roomCodeText.transform.DOKill();
        roomCodeText.transform.localScale = Vector3.one;
        roomCodeText.transform.DOPunchScale(Vector3.one * 0.15f, 0.25f, 5, 0.5f).SetLink(roomCodeText.gameObject);

        // Mobile haptic vibration if supported
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try { Handheld.Vibrate(); } catch { }
#endif
        SoundManager.Instance?.PlaySFX("Button");

        // Immediately show "Copied" feedback
        EnsureCopiedFeedbackText();
        if (copiedFeedbackText != null)
        {
            copiedFeedbackText.text = "Copied";
            copiedFeedbackText.gameObject.SetActive(true);

            CanvasGroup cg = copiedFeedbackText.GetComponent<CanvasGroup>();
            if (cg == null) cg = copiedFeedbackText.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.DOKill();

            copiedFeedbackText.transform.DOKill();
            copiedFeedbackText.transform.localScale = Vector3.one * 0.85f;
            copiedFeedbackText.transform.DOScale(1f, 0.15f).SetEase(Ease.OutBack).SetLink(copiedFeedbackText.gameObject);
        }

        if (hostStatusText != null)
        {
            hostStatusText.text = "Copied to clipboard!";
        }

        // Flash "Copied" feedback fadeout
        if (copiedFeedbackCoroutine != null) StopCoroutine(copiedFeedbackCoroutine);
        copiedFeedbackCoroutine = StartCoroutine(ShowCopiedFeedback());
    }

    private void ShowHoldHint()
    {
        if (hostStatusText != null && hostStatusText.text.StartsWith("Share this code"))
        {
            if (holdHintCoroutine != null) StopCoroutine(holdHintCoroutine);
            holdHintCoroutine = StartCoroutine(ShowHoldHintRoutine());
        }
    }

    private IEnumerator ShowHoldHintRoutine()
    {
        string original = hostStatusText.text;
        hostStatusText.text = "Tap & hold code to copy!";
        yield return new WaitForSeconds(1.5f);
        if (hostStatusText != null && hostStatusText.text == "Tap & hold code to copy!")
        {
            hostStatusText.text = original;
        }
    }

    private void EnsureCopiedFeedbackText()
    {
        if (copiedFeedbackText != null) return;

        if (hostWaitGroup != null)
        {
            var tr = hostWaitGroup.transform.Find("CopiedFeedbackText");
            if (tr != null)
            {
                copiedFeedbackText = tr.GetComponent<TMP_Text>();
                return;
            }
        }

        if (hostWaitGroup != null && roomCodeText != null)
        {
            var go = new GameObject("CopiedFeedbackText");
            go.transform.SetParent(hostWaitGroup.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 65f);
            rt.sizeDelta = new Vector2(260f, 50f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = roomCodeText.font;
            tmp.fontSize = 42;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.2f, 0.8f, 0.4f, 1f);
            tmp.text = "Copied";
            tmp.raycastTarget = false;

            go.AddComponent<CanvasGroup>();
            go.SetActive(false);
            copiedFeedbackText = tmp;
        }
    }

    private IEnumerator ShowCopiedFeedback()
    {
        EnsureCopiedFeedbackText();
        if (copiedFeedbackText != null)
        {
            copiedFeedbackText.text = "Copied";
            copiedFeedbackText.gameObject.SetActive(true);

            CanvasGroup cg = copiedFeedbackText.GetComponent<CanvasGroup>();
            if (cg == null) cg = copiedFeedbackText.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.DOKill();
            cg.DOFade(1f, 0.15f).SetLink(copiedFeedbackText.gameObject);

            copiedFeedbackText.transform.DOKill();
            copiedFeedbackText.transform.localScale = Vector3.one * 0.85f;
            copiedFeedbackText.transform.DOScale(1f, 0.15f).SetEase(Ease.OutBack).SetLink(copiedFeedbackText.gameObject);
        }

        if (hostStatusText != null)
        {
            hostStatusText.text = "Copied to clipboard!";
        }

        yield return new WaitForSeconds(1.5f);

        if (copiedFeedbackText != null)
        {
            CanvasGroup cg = copiedFeedbackText.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOFade(0f, 0.25f)
                  .OnComplete(() => copiedFeedbackText.gameObject.SetActive(false))
                  .SetLink(copiedFeedbackText.gameObject);
            }
            else
            {
                copiedFeedbackText.gameObject.SetActive(false);
            }
        }

        if (hostStatusText != null && hostStatusText.text == "Copied to clipboard!")
        {
            hostStatusText.text = "Share this code. Waiting for opponent…";
        }
    }

    // ---- Join input tap-to-paste ----

    /// <summary>
    /// Attaches tap-to-paste behaviour to the room code input field.
    /// </summary>
    private void SetupJoinInputPaste()
    {
        if (roomCodeInput == null) return;

        var oldTrigger = roomCodeInput.GetComponent<EventTrigger>();
        if (oldTrigger != null)
        {
            if (Application.isPlaying) Destroy(oldTrigger);
            else DestroyImmediate(oldTrigger);
        }

        var pasteTrigger = roomCodeInput.GetComponent<RoomCodeTapPasteTrigger>();
        if (pasteTrigger == null)
        {
            pasteTrigger = roomCodeInput.gameObject.AddComponent<RoomCodeTapPasteTrigger>();
        }

        pasteTrigger.onTap = TryPasteFromClipboard;

        roomCodeInput.onSelect.RemoveListener(OnInputSelected);
        roomCodeInput.onSelect.AddListener(OnInputSelected);
    }

    private void OnInputSelected(string _)
    {
        TryPasteFromClipboard();
    }

    private void TryPasteFromClipboard()
    {
        if (roomCodeInput == null) return;

        string clip = ClipboardHelper.GetFromClipboard();
        if (string.IsNullOrEmpty(clip)) return;

        string code = ClipboardHelper.ExtractRoomCode(clip);
        if (!string.IsNullOrEmpty(code))
        {
            roomCodeInput.text = code;
            SoundManager.Instance?.PlaySFX("Button");

            // Immediately display "Pasted" feedback
            if (joinStatusText != null)
            {
                joinStatusText.text = "Pasted";
                joinStatusText.alignment = TextAlignmentOptions.Center;

                joinStatusText.transform.DOKill();
                joinStatusText.transform.localScale = Vector3.one;
                joinStatusText.transform.DOPunchScale(Vector3.one * 0.12f, 0.2f, 4, 0.5f).SetLink(joinStatusText.gameObject);
            }

            if (pastedFeedbackCoroutine != null) StopCoroutine(pastedFeedbackCoroutine);
            pastedFeedbackCoroutine = StartCoroutine(ShowPastedFeedback());
        }
    }

    private IEnumerator ShowPastedFeedback()
    {
        yield return new WaitForSeconds(1.5f);

        if (joinStatusText != null && joinStatusText.text == "Pasted")
        {
            joinStatusText.text = "";
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

        if (g == hostWaitGroup)
        {
            UpdateHostWaitState(active);
        }
    }

    private void UpdateHostWaitState(bool isHostWaitActive)
    {
        EnsureBg1Reference();

        if (bg1 != null)
            bg1.SetActive(!isHostWaitActive);

        if (backButton != null)
            backButton.gameObject.SetActive(!isHostWaitActive);
    }

    private void EnsureBg1Reference()
    {
        if (bg1 == null)
        {
            if (competitionPanel != null)
            {
                Transform t = competitionPanel.transform.Find("BG1");
                if (t != null) bg1 = t.gameObject;
            }
            if (bg1 == null)
            {
                Transform t = transform.Find("BG1");
                if (t != null) bg1 = t.gameObject;
            }
        }
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

/// <summary>
/// Detects a tap-and-hold (long press) gesture on the room code text.
/// </summary>
public class RoomCodeHoldTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float holdDuration = 0.45f;
    public System.Action onHoldComplete;
    public System.Action onPointerDownAction;
    public System.Action<bool> onPointerUpAction;

    private Coroutine holdCoroutine;
    private bool hasTriggered;

    public void OnPointerDown(PointerEventData eventData)
    {
        hasTriggered = false;
        if (holdCoroutine != null) StopCoroutine(holdCoroutine);
        holdCoroutine = StartCoroutine(HoldRoutine());
        onPointerDownAction?.Invoke();
    }

    private IEnumerator HoldRoutine()
    {
        float timer = 0f;
        while (timer < holdDuration)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        hasTriggered = true;
        holdCoroutine = null;
        onHoldComplete?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        bool triggered = hasTriggered;
        CancelHold();
        onPointerUpAction?.Invoke(triggered);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        bool triggered = hasTriggered;
        CancelHold();
        onPointerUpAction?.Invoke(triggered);
    }

    private void CancelHold()
    {
        if (holdCoroutine != null)
        {
            StopCoroutine(holdCoroutine);
            holdCoroutine = null;
        }
    }
}

/// <summary>
/// Detects taps/clicks on the room code input field to automatically paste.
/// </summary>
public class RoomCodeTapPasteTrigger : MonoBehaviour, IPointerClickHandler
{
    public System.Action onTap;

    public void OnPointerClick(PointerEventData eventData)
    {
        onTap?.Invoke();
    }
}

