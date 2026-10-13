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
    [SerializeField] private TMP_Text opponentLevelText;

    [Header("Name And Level")]
    [SerializeField] private TMP_FontAsset identityFont;

    [Header("Grid Layout Matching Game Board")]
    [SerializeField] private Vector2 bigGridCellSize = new Vector2(287f, 345f);
    [SerializeField] private Vector2 bigGridSpacing = new Vector2(20f, 25f);
    [SerializeField] private Color boxBorderColor = new Color(0.35f, 0.20f, 0.08f, 1f); // Wood/rope divider
    [SerializeField] private Vector2 smallGridCellSize = new Vector2(92.8f, 112f);
    [SerializeField] private Vector2 smallGridSpacing = new Vector2(3.6f, 4f);
    [SerializeField] private float cellFontSize = 70f;

    [Header("Cell Colors")]
    [SerializeField] private Color emptyCellColor = new Color(0.12f, 0.16f, 0.24f, 1f);
    [SerializeField] private Color fixedCellColor = new Color(0.24f, 0.22f, 0.17f, 1f);    // Dark Warm Bronze
    [SerializeField] private Color fixedTextColor = new Color(0.92f, 0.78f, 0.58f, 1f);    // Light Amber Sand
    [SerializeField] private Color completedCellColor = new Color(0.08f, 0.44f, 0.28f, 1f);
    [SerializeField] private Color wrongCellColor = new Color(0.48f, 0.14f, 0.18f, 1f);
    [SerializeField] private Color wrongTextColor = new Color(0.98f, 0.72f, 0.75f, 1f);    // Light Rose Peach
    [Header("Animation")]
    [SerializeField] private float animDuration = 0.35f;

    // Copied from the live game board so both boards share one look.
    private const float NameHeight = 60f;
    private const float LevelHeight = 48f;
    private const float HeaderGap = 8f;
    private const float HeaderBand = NameHeight + HeaderGap + LevelHeight;
    private const float CardPad = 28f;
    private static readonly Color PanelColor = new Color(58f / 255f, 74f / 255f, 93f / 255f, 1f); // #3A4A5D
    private static readonly Color IdentityColor = Color.white;
    private Sprite boardFrameSprite;
    private Sprite cellSprite;
    private TMP_FontAsset cellFont;
    private Color altBoxCellColor;
    private bool themeMatched;
    private bool profileHeaderReady;

    // 81 cell Image and Text references (ordered row-major: r*9 + c)
    private Image[] cellImages;
    private TMP_Text[] cellTexts;

    // Snapshot of the puzzle's fixed initial clue cells
    private bool[] isFixedCell = new bool[81];
    private readonly byte[] _shownCells = new byte[81];
    private bool _cellsPrimed;
    private string _shownName;
    private int _shownLevel = int.MinValue;

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

        EnableOutsideTapToClose();
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
        MatchGameBoardTheme();
        BuildMiniGrid();
        MarkFixedCells();
        ApplyColorTheme();
    }

    private void MatchGameBoardTheme()
    {
        altBoxCellColor = emptyCellColor;
        var board = FindAnyObjectByType<SudokuGridLayout>();
        if (board == null) return;

        emptyCellColor = board.CellDefaultBgColor;
        altBoxCellColor = board.CellAltBoxBgColor;
        fixedCellColor = emptyCellColor;
        fixedTextColor = board.CellTextColor;
        completedCellColor = new Color(0.80f, 0.93f, 0.78f, 1f);
        wrongCellColor = new Color(1f, 0.84f, 0.84f, 1f);
        wrongTextColor = new Color(0.85f, 0.15f, 0.15f, 1f);
        boxBorderColor = Color.clear;
        cellFont = board.CellFontAsset;

        if (board.transform.parent != null)
        {
            Image frame = board.transform.parent.GetComponent<Image>();
            if (frame != null)
                boardFrameSprite = frame.sprite;
        }

        if (board.Cells != null && board.Cells[0, 0] != null)
        {
            var cell = board.Cells[0, 0].GetComponent<Image>();
            if (cell != null) cellSprite = cell.sprite;
        }
        themeMatched = true;
    }

    private Color BaseCellColor(int row, int col)
    {
        bool altBox = ((row / 3) + (col / 3)) % 2 != 0;
        return altBox ? altBoxCellColor : emptyCellColor;
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

        panelRoot.transform.localScale = Vector3.zero;

        if (panelCG != null)
            panelCG.alpha = 0f;

        panelRoot.SetActive(true);
        ApplyColorTheme();
        Canvas.ForceUpdateCanvases();
        FitBoardGrid();

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

    private void ApplyOpponentIdentity()
    {
        EnsureOpponentHeader();

        int level = MultiplayerManager.Instance != null
            ? MultiplayerManager.Instance.OpponentProfileLevel
            : 1;

        string levelLabel = $"Level {Mathf.Max(1, level):00}";
        if (opponentLevelText != null && (_shownLevel != level || opponentLevelText.text != levelLabel))
        {
            _shownLevel = level;
            opponentLevelText.text = levelLabel;
        }
    }

    private void EnsureOpponentHeader()
    {
        if (panelRoot == null || profileHeaderReady) return;

        Transform card = FindCard();
        if (card == null) card = panelRoot.transform;

        Transform rowTransform = card.Find("OpponentProfile");
        RectTransform row;
        if (rowTransform == null)
        {
            var rowObject = new GameObject("OpponentProfile", typeof(RectTransform));
            rowObject.transform.SetParent(card, false);
            row = rowObject.GetComponent<RectTransform>();
        }
        else
        {
            row = (RectTransform)rowTransform;
        }

        // Name, then level, in the band above the board and inset from the panel edges.
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(CardPad, -(CardPad + HeaderBand));
        row.offsetMax = new Vector2(-CardPad, -CardPad);
        row.SetAsLastSibling();

        if (opponentNameText != null)
        {
            opponentNameText.transform.SetParent(row, false);
            StyleIdentityLabel(opponentNameText, 0f, NameHeight, 45f);
        }

        if (opponentLevelText == null)
        {
            Transform levelTransform = row.Find("OpponentLevelText");
            if (levelTransform == null)
            {
                var levelObject = new GameObject("OpponentLevelText", typeof(RectTransform));
                levelObject.transform.SetParent(row, false);
                TextMeshProUGUI created = levelObject.AddComponent<TextMeshProUGUI>();
                if (opponentNameText != null)
                    created.font = opponentNameText.font;
                opponentLevelText = created;
            }
            else
            {
                opponentLevelText = levelTransform.GetComponent<TMP_Text>();
            }
        }
        else if (opponentLevelText.transform.parent != row)
        {
            opponentLevelText.transform.SetParent(row, false);
        }

        StyleIdentityLabel(opponentLevelText, NameHeight + HeaderGap, HeaderBand, 35f);
        profileHeaderReady = true;

        RectTransform gridRect = gridParent as RectTransform;
        if (gridRect != null)
            PlaceIdentity();
    }

    private void StyleIdentityLabel(TMP_Text label, float top, float bottom, float size)
    {
        if (label == null) return;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -bottom);
        rect.offsetMax = new Vector2(0f, -top);
        label.alignment = TextAlignmentOptions.Center;
        label.color = IdentityColor;
        if (identityFont != null)
            label.font = identityFont;
        label.enableAutoSizing = false;
        label.fontSize = size;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.outlineWidth = 0f;
        label.gameObject.SetActive(true);
    }

    // ---- Update ----

    private float _nextBoardRefresh;

    private void Update()
    {
        if (!isVisible) return;
        if (Time.unscaledTime < _nextBoardRefresh) return;
        _nextBoardRefresh = Time.unscaledTime + 0.1f;
        RefreshDisplay();
    }

    private NetworkSudokuPlayer _foundRemote;
    private float _nextRemoteSearch;

    private NetworkSudokuPlayer ResolveRemote()
    {
        if (NetworkSudokuPlayer.Remote != null) return NetworkSudokuPlayer.Remote;
        if (_foundRemote != null && _foundRemote.Object != null && _foundRemote.Object.IsValid)
            return _foundRemote;
        if (Time.unscaledTime < _nextRemoteSearch) return null;
        _nextRemoteSearch = Time.unscaledTime + 0.5f;

        _foundRemote = null;
        var players = FindObjectsByType<NetworkSudokuPlayer>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p != null && p != NetworkSudokuPlayer.Local && p.Object != null && p.Object.IsValid && !p.HasStateAuthority)
            {
                _foundRemote = p;
                break;
            }
        }
        return _foundRemote;
    }

    public void RefreshDisplay()
    {
        var remote = ResolveRemote();

        // Header texts
        if (remote != null)
        {
            if (opponentNameText != null)
            {
                string pName = MultiplayerManager.Instance != null
                    ? MultiplayerManager.Instance.OpponentName
                    : null;
                if (string.IsNullOrWhiteSpace(pName))
                    pName = remote.PlayerName.ToString();
                pName = ProfileManager.ShortDisplayName(pName);
                if (string.IsNullOrEmpty(pName)) pName = "Opponent";
                if (_shownName != pName)
                {
                    _shownName = pName;
                    opponentNameText.text = pName;
                    opponentNameText.color = IdentityColor;
                    opponentNameText.alignment = TextAlignmentOptions.Center;
                }
            }

            ApplyOpponentIdentity();
        }
        else
        {
            if (opponentNameText != null)
            {
                string known = MultiplayerManager.Instance != null ? MultiplayerManager.Instance.OpponentName : null;
                string pName = string.IsNullOrWhiteSpace(known) ? "Opponent" : ProfileManager.ShortDisplayName(known);
                if (_shownName != pName)
                {
                    _shownName = pName;
                    opponentNameText.text = pName;
                    opponentNameText.color = IdentityColor;
                    opponentNameText.alignment = TextAlignmentOptions.Center;
                }
            }

            ApplyOpponentIdentity();
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

                byte rawVal = 0;
                if (remote != null)
                    rawVal = remote.BoardSnapshot[i];
                if (_cellsPrimed && _shownCells[i] == rawVal)
                    continue;
                _shownCells[i] = rawVal;

                int num = rawVal & 0x0F;
                bool isWrong = (rawVal & 0x10) != 0;

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

                // Update colors (strictly non-white) and text.
                // NOTE: Correctly completed non-fixed cells intentionally show NO number
                // so the local player cannot read and copy the opponent's answer.
                if (num == 0)
                {
                    cellImages[i].color = themeMatched ? BaseCellColor(r, c) : emptyCellColor;
                    if (cellTexts[i] != null)
                        cellTexts[i].text = "";
                }
                else if (isFixed)
                {
                    // Fixed/given clue cells: show number normally
                    if (cellTexts[i] != null)
                    {
                        cellTexts[i].text = num.ToString();
                        cellTexts[i].color = fixedTextColor;
                    }
                    cellImages[i].color = themeMatched ? BaseCellColor(r, c) : fixedCellColor;
                }
                else if (isWrong)
                {
                    // Wrong guess: show the (wrong) number so the opponent sees their mistake
                    if (cellTexts[i] != null)
                    {
                        cellTexts[i].text = num.ToString();
                        cellTexts[i].color = wrongTextColor;
                    }
                    cellImages[i].color = wrongCellColor;
                }
                else
                {
                    // Correctly completed cell: show green color but NO number
                    // to prevent the local player from copying the answer
                    if (cellTexts[i] != null)
                        cellTexts[i].text = "";
                    cellImages[i].color = completedCellColor;
                }
            }
        }

        if (puzzle != null)
            _cellsPrimed = true;
    }

    // ---- Board grid builder matching game board layout ----

    // Squares the board under the profile row and gives every cell the same thin gap,
    // including across the nine 3x3 boxes. The old 20x25 box spacing left empty bands.
    private void FitBoardGrid()
    {
        if (gridParent == null) return;

        RectTransform gridRect = gridParent as RectTransform;
        if (gridRect == null) return;
        Transform dim = EnsureDimBackground();
        Canvas.ForceUpdateCanvases();
        RectTransform card = gridRect.parent as RectTransform;

        RectTransform panel = panelRoot != null ? panelRoot.transform as RectTransform : null;
        float panelWidth = panel != null ? panel.rect.width : 0f;
        float panelHeight = panel != null ? panel.rect.height : 0f;
        if (panelWidth < 100f) panelWidth = 980f;
        if (panelHeight < 100f) panelHeight = 1400f;

        const float screenMargin = 48f;
        const float belowHeader = 16f;
        float side = Mathf.Min(
            panelWidth - screenMargin * 2f - CardPad * 2f,
            panelHeight - screenMargin * 2f - CardPad * 2f - HeaderBand - belowHeader);
        side = Mathf.Max(280f, side);

        if (card != null)
        {
            if (dim != null && card.parent != dim)
                card.SetParent(dim, false);

            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(
                side + CardPad * 2f,
                CardPad + HeaderBand + belowHeader + side + CardPad);
            if (card.GetComponent<RectMask2D>() == null)
                card.gameObject.AddComponent<RectMask2D>();
        }

        gridRect.anchorMin = new Vector2(0.5f, 1f);
        gridRect.anchorMax = new Vector2(0.5f, 1f);
        gridRect.pivot = new Vector2(0.5f, 1f);
        gridRect.anchoredPosition = new Vector2(0f, -(CardPad + HeaderBand + belowHeader));
        gridRect.sizeDelta = new Vector2(side, side);

        // Same proportions as the game board: a thin gap between cells and a wider
        // gap between the 3x3 boxes, with the frame margin matching that box gap.
        const float cellUnit = 87f;
        float cell = (side - 8f) / (9f + (6f * 5f + 2f * 14f + 2f * 14f) / cellUnit);
        float cellGap = cell * (5f / cellUnit);
        float boxGap = cell * (14f / cellUnit);
        int pad = Mathf.Max(4, Mathf.RoundToInt(boxGap));
        smallGridCellSize = new Vector2(cell, cell);
        smallGridSpacing = new Vector2(cellGap, cellGap);
        bigGridSpacing = new Vector2(boxGap, boxGap);
        bigGridCellSize = new Vector2(cell * 3f + cellGap * 2f, cell * 3f + cellGap * 2f);

        GridLayoutGroup outer = gridParent.GetComponent<GridLayoutGroup>();
        if (outer != null)
        {
            outer.cellSize = bigGridCellSize;
            outer.spacing = bigGridSpacing;
            outer.padding = new RectOffset(pad, pad, pad, pad);
        }

        ApplyBoardFrame();
        ApplyPanelBackground();
        EnsureDimBackground();
        PlaceIdentity();

        for (int i = 0; i < gridParent.childCount; i++)
        {
            GridLayoutGroup inner = gridParent.GetChild(i).GetComponent<GridLayoutGroup>();
            if (inner == null) continue;
            inner.cellSize = smallGridCellSize;
            inner.spacing = smallGridSpacing;
        }
    }

    private void BuildMiniGrid()
    {
        if (gridParent == null) return;

        ApplyBoardFrame();

        // Clear any existing children
        for (int i = gridParent.childCount - 1; i >= 0; i--)
            Destroy(gridParent.GetChild(i).gameObject);

        FitBoardGrid();

        // Outer boxes use the game board's wider 3x3 gap. Padding matches that gap
        // so the frame margin looks like the real board.
        var outerGrid = gridParent.GetComponent<GridLayoutGroup>();
        if (outerGrid == null) outerGrid = gridParent.gameObject.AddComponent<GridLayoutGroup>();
        outerGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        outerGrid.constraintCount = 3;
        outerGrid.cellSize = bigGridCellSize;
        outerGrid.spacing = bigGridSpacing;
        outerGrid.childAlignment = TextAnchor.MiddleCenter;
        int framePad = Mathf.Max(4, Mathf.RoundToInt(bigGridSpacing.x));
        outerGrid.padding = new RectOffset(framePad, framePad, framePad, framePad);

        cellImages = new Image[81];
        cellTexts = new TMP_Text[81];
        _cellsPrimed = false;

        for (int boxRow = 0; boxRow < 3; boxRow++)
        {
            for (int boxCol = 0; boxCol < 3; boxCol++)
            {
                GameObject boxGO = new GameObject($"Box_{boxRow}_{boxCol}", typeof(RectTransform));
                boxGO.transform.SetParent(gridParent, false);

                Image boxBg = boxGO.AddComponent<Image>();
                boxBg.color = boxBorderColor;
                boxBg.raycastTarget = false;

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
                        if (cellSprite != null) cellImg.sprite = cellSprite;
                        cellImg.color = themeMatched ? BaseCellColor(globalRow, globalCol) : emptyCellColor;
                        cellImg.raycastTarget = false;
                        cellImages[idx] = cellImg;

                        GameObject textGO = new GameObject("Number", typeof(RectTransform));
                        textGO.transform.SetParent(cellGO.transform, false);

                        RectTransform textRT = textGO.GetComponent<RectTransform>();
                        textRT.anchorMin = Vector2.zero;
                        textRT.anchorMax = Vector2.one;
                        textRT.offsetMin = Vector2.zero;
                        textRT.offsetMax = Vector2.zero;

                        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
                        if (cellFont != null) tmp.font = cellFont;
                        tmp.raycastTarget = false;
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
            if (bgImg != null)
            {
                bgImg.color = Color.clear;
                bgImg.raycastTarget = true;
            }

            Transform nested = panelRoot.transform.Find("PanelRoot");
            if (nested != null)
            {
                Image nestedImage = nested.GetComponent<Image>();
                if (nestedImage != null)
                    nestedImage.color = Color.clear;
            }
        }

        ApplyBoardFrame();
        ApplyPanelBackground();
        EnsureDimBackground();

        if (opponentNameText != null) opponentNameText.color = IdentityColor;
    }

    private void ApplyBoardFrame()
    {
        if (gridParent == null) return;
        Image frame = gridParent.GetComponent<Image>();
        if (frame == null)
            frame = gridParent.gameObject.AddComponent<Image>();
        if (boardFrameSprite != null)
            frame.sprite = boardFrameSprite;
        frame.color = Color.white;
        frame.type = Image.Type.Simple;
        frame.preserveAspect = false;
        frame.raycastTarget = false;
    }

    private void PlaceIdentity()
    {
        if (panelRoot == null) return;
        Transform card = FindCard();
        if (card == null) card = panelRoot.transform;
        RectTransform row = card.Find("OpponentProfile") as RectTransform;
        if (row == null) return;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(CardPad, -(CardPad + HeaderBand));
        row.offsetMax = new Vector2(-CardPad, -CardPad);
    }

    private void ApplyPanelBackground()
    {
        if (panelRoot == null) return;
        Transform card = FindCard();
        if (card == null) return;

        Image image = card.GetComponent<Image>();
        if (image == null)
            image = card.gameObject.AddComponent<Image>();
        image.sprite = null;
        image.color = PanelColor;
        image.type = Image.Type.Simple;
        image.raycastTarget = true;
    }

    private Transform EnsureDimBackground()
    {
        if (panelRoot == null) return null;

        RectTransform root = panelRoot.transform as RectTransform;
        if (root != null)
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }

        Transform existing = panelRoot.transform.Find("DimBackground");
        RectTransform rect;
        if (existing == null)
        {
            var backdrop = new GameObject("DimBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backdrop.transform.SetParent(panelRoot.transform, false);
            rect = backdrop.GetComponent<RectTransform>();
        }
        else
        {
            rect = existing as RectTransform;
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();

        Image image = rect.GetComponent<Image>();
        image.sprite = null;
        image.color = new Color(0f, 0f, 0f, 200f / 255f);
        image.raycastTarget = false;
        return rect;
    }

    private Transform FindCard()
    {
        if (panelRoot == null) return null;
        Transform card = panelRoot.transform.Find("DimBackground/Card");
        if (card == null)
            card = panelRoot.transform.Find("PanelRoot/Card");
        if (card == null)
            card = panelRoot.transform.Find("Card");
        return card;
    }

    private void EnableOutsideTapToClose()
    {
        if (panelRoot == null) return;
        Button outside = panelRoot.GetComponent<Button>();
        if (outside == null)
            outside = panelRoot.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.RemoveListener(Hide);
        outside.onClick.AddListener(Hide);
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