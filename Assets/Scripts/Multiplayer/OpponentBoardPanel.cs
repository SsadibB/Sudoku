using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Renders the opponent's live board state matching the game board grid layout.
/// Features a full 3×3 outer boxes with 3×3 inner cells structure, matching cell sizes
/// and prominent bold numbers.
/// Animates smoothly from the "Board" button upon opening, and retreats/scales
/// back into that button upon closing.
/// Keeps display live in real-time as the opponent fills, changes, or erases cells.
/// Adheres strictly to a curated non-white, non-pink visual aesthetic palette.
/// </summary>
public class OpponentBoardPanel : MonoBehaviour
{
    [Header("Panel Root & Positioning")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelCG;
    [SerializeField] private RectTransform boardButtonTransform;

    [Header("Mini-Grid Container")]
    [SerializeField] private Transform gridParent;

    [Header("Header")]
    [SerializeField] private TMP_Text opponentNameText;
    [SerializeField] private TMP_Text opponentProgressText;

    [Header("Close Button")]
    [SerializeField] private Button closeButton;

    [Header("Grid Layout Matching Game Board")]
    [SerializeField] private Vector2 bigGridCellSize = new Vector2(287f, 345f);
    [SerializeField] private Vector2 bigGridSpacing = new Vector2(20f, 25f);
    [SerializeField] private Color boxBorderColor = new Color(0.35f, 0.20f, 0.08f, 1f); // Wood/rope divider
    [SerializeField] private Vector2 smallGridCellSize = new Vector2(92.8f, 112f);
    [SerializeField] private Vector2 smallGridSpacing = new Vector2(3.6f, 4f);
    [SerializeField] private float cellFontSize = 70f;

    [Header("Non-White Aesthetic Color Palette")]
    [SerializeField] private Color panelBgColor         = new Color(0.06f, 0.08f, 0.14f, 0.98f); // Obsidian Midnight Navy
    [SerializeField] private Color gridBgColor          = new Color(0.03f, 0.04f, 0.08f, 0.90f); // Deep Cavity
    [SerializeField] private Color headerNameColor      = new Color(0.96f, 0.78f, 0.35f, 1f);    // Luminous Amber Gold
    [SerializeField] private Color progressTextColor    = new Color(0.25f, 0.88f, 0.70f, 1f);    // Electric Cyan Mint
    [SerializeField] private Color emptyCellColor       = new Color(0.12f, 0.16f, 0.24f, 1f);    // Deep Twilight Slate
    [SerializeField] private Color fixedCellColor       = new Color(0.24f, 0.22f, 0.17f, 1f);    // Dark Warm Bronze
    [SerializeField] private Color fixedTextColor       = new Color(0.92f, 0.78f, 0.58f, 1f);    // Light Amber Sand
    [SerializeField] private Color completedCellColor   = new Color(0.08f, 0.44f, 0.28f, 1f);    // Vibrant Emerald Jade
    [SerializeField] private Color completedTextColor   = new Color(0.60f, 0.96f, 0.78f, 1f);    // Bright Mint Aqua
    [SerializeField] private Color wrongCellColor       = new Color(0.48f, 0.14f, 0.18f, 1f);    // Deep Ruby Crimson
    [SerializeField] private Color wrongTextColor       = new Color(0.98f, 0.72f, 0.75f, 1f);    // Light Rose Peach
    [SerializeField] private Color closeButtonColor     = new Color(0.65f, 0.18f, 0.22f, 1f);    // Garnet Crimson
    [SerializeField] private Color closeButtonTextColor = new Color(0.96f, 0.82f, 0.55f, 1f);    // Warm Gold Icon

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.35f;

    // 81 cell Image and Text references (ordered row-major: r*9 + c)
    private Image[] cellImages;
    private TMP_Text[] cellTexts;

    // Snapshot of the puzzle's fixed initial clue cells
    private bool[] isFixedCell = new bool[81];

    private bool isInitialized;
    private bool isVisible;
    private Vector3 defaultPanelWorldPos;
    private Vector3 defaultPanelLocalScale = Vector3.one;
    private bool hasCapturedTransform;

    private Tween posTween;
    private Tween scaleTween;
    private Tween fadeTween;

    private void Awake()
    {
        CaptureDefaultTransform();
        ApplyColorTheme();

        if (panelRoot != null)
        {
            if (panelCG == null)
                panelCG = panelRoot.GetComponent<CanvasGroup>();
            if (panelCG == null)
                panelCG = panelRoot.AddComponent<CanvasGroup>();

            panelRoot.SetActive(false);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }
    }

    private void Start()
    {
        Initialize();
    }

    private void CaptureDefaultTransform()
    {
        if (hasCapturedTransform || panelRoot == null) return;

        defaultPanelWorldPos = panelRoot.transform.position;
        defaultPanelLocalScale = panelRoot.transform.localScale;
        if (defaultPanelLocalScale.sqrMagnitude < 0.01f)
            defaultPanelLocalScale = Vector3.one;

        hasCapturedTransform = true;
    }

    /// <summary>Injects the Board button's transform for origin-to-target animations.</summary>
    public void SetBoardButton(RectTransform btnRect)
    {
        boardButtonTransform = btnRect;
    }

    /// <summary>Builds the board grid and marks fixed puzzle cells.</summary>
    public void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        CaptureDefaultTransform();
        BuildMiniGrid();
        MarkFixedCells();
        ApplyColorTheme();
    }

    // ---- Public toggle / show / hide ----

    public void Toggle()
    {
        if (isVisible) Hide();
        else Show();
    }

    public void Show()
    {
        CaptureDefaultTransform();

        if (!isInitialized)
            Initialize();

        isVisible = true;
        if (panelRoot == null) return;

        KillTweens();

        if (boardButtonTransform == null)
        {
            var btn = GameObject.Find("BoardButton");
            if (btn != null) boardButtonTransform = btn.GetComponent<RectTransform>();
        }

        if (defaultPanelLocalScale.sqrMagnitude < 0.01f)
            defaultPanelLocalScale = Vector3.one;

        // Start animation from the Board button's position and scale zero
        if (boardButtonTransform != null)
        {
            panelRoot.transform.position = boardButtonTransform.position;
        }
        else
        {
            panelRoot.transform.position = defaultPanelWorldPos;
        }
        panelRoot.transform.localScale = Vector3.zero;

        if (panelCG != null)
            panelCG.alpha = 0f;

        panelRoot.SetActive(true);
        ApplyColorTheme();

        // Animate out from button to default center position
        posTween = panelRoot.transform.DOMove(defaultPanelWorldPos, animDuration)
            .SetEase(Ease.OutBack)
            .SetLink(panelRoot);

        scaleTween = panelRoot.transform.DOScale(defaultPanelLocalScale, animDuration)
            .SetEase(Ease.OutBack)
            .SetLink(panelRoot);

        if (panelCG != null)
        {
            fadeTween = panelCG.DOFade(1f, animDuration)
                .SetLink(panelRoot);
        }

        // Immediately update all cells and numbers
        RefreshDisplay();
    }

    public void Hide()
    {
        isVisible = false;
        if (panelRoot == null) return;

        KillTweens();

        // Animate back into the Board button
        Vector3 targetPos = boardButtonTransform != null ? boardButtonTransform.position : defaultPanelWorldPos;

        posTween = panelRoot.transform.DOMove(targetPos, animDuration)
            .SetEase(Ease.InBack)
            .SetLink(panelRoot);

        scaleTween = panelRoot.transform.DOScale(Vector3.zero, animDuration)
            .SetEase(Ease.InBack)
            .SetLink(panelRoot);

        if (panelCG != null)
        {
            fadeTween = panelCG.DOFade(0f, animDuration)
                .SetLink(panelRoot)
                .OnComplete(() =>
                {
                    panelRoot.SetActive(false);
                    panelRoot.transform.position = defaultPanelWorldPos;
                    panelRoot.transform.localScale = defaultPanelLocalScale;
                });
        }
        else
        {
            scaleTween.OnComplete(() =>
            {
                panelRoot.SetActive(false);
                panelRoot.transform.position = defaultPanelWorldPos;
                panelRoot.transform.localScale = defaultPanelLocalScale;
            });
        }
    }

    private void KillTweens()
    {
        posTween?.Kill();
        scaleTween?.Kill();
        fadeTween?.Kill();
    }

    // ---- Update ----

    private void Update()
    {
        if (!isVisible) return;
        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        var remote = NetworkSudokuPlayer.Remote;

        // Header texts
        if (remote != null)
        {
            if (opponentNameText != null)
            {
                string pName = remote.PlayerName.ToString();
                opponentNameText.text = string.IsNullOrEmpty(pName) ? "Opponent" : pName;
                opponentNameText.color = headerNameColor;
            }

            if (opponentProgressText != null)
            {
                opponentProgressText.text = remote.IsFinished
                    ? "Finished!"
                    : $"{remote.CompletedCells}/81";
                opponentProgressText.color = progressTextColor;
            }
        }
        else
        {
            if (opponentNameText != null)
            {
                opponentNameText.text = "Opponent";
                opponentNameText.color = headerNameColor;
            }

            if (opponentProgressText != null)
            {
                opponentProgressText.text = "Waiting for Opponent...";
                opponentProgressText.color = progressTextColor;
            }
        }

        // Live grid cells
        if (cellImages == null || cellTexts == null) return;

        var gm = SudokuGameManager.Instance;
        int[,] puzzle = gm != null ? gm.GetCurrentPuzzle() : null;

        for (int r = 0; r < 9; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                int i = r * 9 + c;
                if (i >= cellImages.Length || cellImages[i] == null) continue;

                int num = 0;
                bool isWrong = false;

                // Read remote player's cell state if connected
                if (remote != null)
                {
                    byte rawVal = remote.BoardSnapshot[i];
                    num = rawVal & 0x0F;
                    isWrong = (rawVal & 0x10) != 0;
                }

                // Check fixed initial puzzle clues
                bool isFixed = isFixedCell[i];
                if (puzzle != null && puzzle[r, c] != 0)
                {
                    isFixed = true;
                    isFixedCell[i] = true;
                }

                if (isFixed)
                {
                    if (num == 0 && puzzle != null)
                        num = puzzle[r, c];
                }

                // Update text
                if (cellTexts[i] != null)
                {
                    cellTexts[i].text = num > 0 ? num.ToString() : "";
                }

                // Update colors (strictly non-white)
                if (num == 0)
                {
                    cellImages[i].color = emptyCellColor;
                }
                else if (isFixed)
                {
                    cellImages[i].color = fixedCellColor;
                    if (cellTexts[i] != null)
                        cellTexts[i].color = fixedTextColor;
                }
                else if (isWrong)
                {
                    cellImages[i].color = wrongCellColor;
                    if (cellTexts[i] != null)
                        cellTexts[i].color = wrongTextColor;
                }
                else
                {
                    // Opponent correctly completed cell
                    cellImages[i].color = completedCellColor;
                    if (cellTexts[i] != null)
                        cellTexts[i].color = completedTextColor;
                }
            }
        }
    }

    // ---- Board grid builder matching game board layout ----

    private void BuildMiniGrid()
    {
        if (gridParent == null) return;

        // Ensure gridParent has dark cavity background
        var parentBg = gridParent.GetComponent<Image>();
        if (parentBg != null)
        {
            parentBg.color = gridBgColor;
        }

        // Clear any existing children
        for (int i = gridParent.childCount - 1; i >= 0; i--)
            Destroy(gridParent.GetChild(i).gameObject);

        // Configure outer grid (3×3 outer boxes)
        var outerGrid = gridParent.GetComponent<GridLayoutGroup>();
        if (outerGrid == null) outerGrid = gridParent.gameObject.AddComponent<GridLayoutGroup>();
        outerGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        outerGrid.constraintCount = 3;
        outerGrid.cellSize = bigGridCellSize;
        outerGrid.spacing = bigGridSpacing;
        outerGrid.childAlignment = TextAnchor.MiddleCenter;
        outerGrid.padding = new RectOffset(39, 39, 43, 43);

        cellImages = new Image[81];
        cellTexts  = new TMP_Text[81];

        for (int boxRow = 0; boxRow < 3; boxRow++)
        {
            for (int boxCol = 0; boxCol < 3; boxCol++)
            {
                GameObject boxGO = new GameObject($"Box_{boxRow}_{boxCol}", typeof(RectTransform));
                boxGO.transform.SetParent(gridParent, false);

                Image boxBg = boxGO.AddComponent<Image>();
                boxBg.color = boxBorderColor;

                GridLayoutGroup innerGrid = boxGO.AddComponent<GridLayoutGroup>();
                innerGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                innerGrid.constraintCount = 3;
                innerGrid.cellSize = smallGridCellSize;
                innerGrid.spacing = smallGridSpacing;
                innerGrid.childAlignment = TextAnchor.MiddleCenter;
                innerGrid.padding = new RectOffset(0, 0, 0, 0);

                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int globalRow = boxRow * 3 + r;
                        int globalCol = boxCol * 3 + c;
                        int idx = globalRow * 9 + globalCol;

                        GameObject cellGO = new GameObject($"Cell_{globalRow}_{globalCol}", typeof(RectTransform));
                        cellGO.transform.SetParent(boxGO.transform, false);

                        Image cellImg = cellGO.AddComponent<Image>();
                        cellImg.color = emptyCellColor;
                        cellImages[idx] = cellImg;

                        GameObject textGO = new GameObject("Number", typeof(RectTransform));
                        textGO.transform.SetParent(cellGO.transform, false);

                        RectTransform textRT = textGO.GetComponent<RectTransform>();
                        textRT.anchorMin = Vector2.zero;
                        textRT.anchorMax = Vector2.one;
                        textRT.offsetMin = Vector2.zero;
                        textRT.offsetMax = Vector2.zero;

                        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
                        tmp.alignment = TextAlignmentOptions.Center;
                        tmp.enableAutoSizing = true;
                        tmp.fontSizeMin = 20f;
                        tmp.fontSizeMax = cellFontSize;
                        tmp.fontStyle = FontStyles.Bold;
                        tmp.color = fixedTextColor;
                        tmp.text = "";
                        cellTexts[idx] = tmp;
                    }
                }
            }
        }
    }

    public void ApplyColorTheme()
    {
        if (panelRoot != null)
        {
            var bgImg = panelRoot.GetComponent<Image>();
            if (bgImg != null) bgImg.color = panelBgColor;

            // Apply panel background color to Card and container images (eliminates pink / white)
            foreach (var img in panelRoot.GetComponentsInChildren<Image>(true))
            {
                string n = img.gameObject.name;
                if (n == "Card" || n == "PanelRoot" || n == "OpponentBoardPanel")
                {
                    img.color = panelBgColor;
                }
            }
        }

        if (gridParent != null)
        {
            var parentBg = gridParent.GetComponent<Image>();
            if (parentBg != null) parentBg.color = gridBgColor;
        }

        if (opponentNameText != null) opponentNameText.color = headerNameColor;
        if (opponentProgressText != null) opponentProgressText.color = progressTextColor;

        if (closeButton != null)
        {
            if (closeButton.image != null)
                closeButton.image.color = closeButtonColor;
            var closeText = closeButton.GetComponentInChildren<TMP_Text>();
            if (closeText != null)
                closeText.color = closeButtonTextColor;
        }
    }

    private void MarkFixedCells()
    {
        var gm = SudokuGameManager.Instance;
        if (gm == null) return;

        var puzzle = gm.GetCurrentPuzzle();
        if (puzzle == null) return;

        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                isFixedCell[r * 9 + c] = puzzle[r, c] != 0;
    }
}
