using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Renders the opponent's live board state as a read-only 9×9 mini-grid.
/// Reads NetworkSudokuPlayer.Remote every frame to keep the display current.
/// Toggle with the "Board" button in the GameScene header.
/// </summary>
public class OpponentBoardPanel : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Mini-Grid")]
    [SerializeField] private Transform gridParent;   // 81 cells will be created here

    [Header("Header")]
    [SerializeField] private TMP_Text opponentNameText;
    [SerializeField] private TMP_Text opponentProgressText;

    [Header("Close")]
    [SerializeField] private Button closeButton;

    [Header("Appearance")]
    [SerializeField] private Color fixedCellColor    = new Color(0.88f, 0.85f, 0.78f, 1f);
    [SerializeField] private Color emptyCellColor    = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Color completedCellColor = new Color(0.55f, 0.85f, 0.55f, 0.9f);
    [SerializeField] private float cellSize          = 28f;

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.25f;

    // 81 cell Image refs (row*9+col)
    private Image[] cellImages;
    private TMP_Text[] cellTexts;

    // Snapshot of the puzzle's fixed cells (set during Initialize)
    private bool[] isFixedCell = new bool[81];

    private bool isInitialized;
    private bool isVisible;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
    }

    /// <summary>Call once after the puzzle grid has been populated.</summary>
    public void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;

        BuildMiniGrid();
        MarkFixedCells();
    }

    // ---- Public toggle / hide ----

    public void Toggle()
    {
        if (isVisible) Hide();
        else Show();
    }

    public void Show()
    {
        if (!isInitialized) Initialize();

        isVisible = true;
        if (panelRoot == null) return;

        panelRoot.SetActive(true);
        panelRoot.transform.localScale = Vector3.one * 0.8f;
        panelRoot.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack).SetLink(panelRoot);

        RefreshDisplay();
    }

    public void Hide()
    {
        isVisible = false;
        if (panelRoot == null) return;

        panelRoot.transform.DOScale(0.8f, animDuration).SetEase(Ease.InBack)
            .OnComplete(() => panelRoot.SetActive(false))
            .SetLink(panelRoot);
    }

    // ---- Update ----

    private void Update()
    {
        if (!isVisible) return;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var remote = NetworkSudokuPlayer.Remote;
        if (remote == null)
        {
            if (opponentNameText != null) opponentNameText.text = "Opponent";
            if (opponentProgressText != null) opponentProgressText.text = "Waiting…";
            return;
        }

        // Header
        if (opponentNameText != null)
            opponentNameText.text = remote.PlayerName.ToString();

        if (opponentProgressText != null)
            opponentProgressText.text = remote.IsFinished
                ? "Finished!"
                : $"{remote.CompletedCells}/81";

        // Cells
        if (cellImages == null) return;
        for (int i = 0; i < 81; i++)
        {
            byte val = remote.BoardSnapshot[i];
            if (cellTexts != null && cellTexts[i] != null)
                cellTexts[i].text = val > 0 ? val.ToString() : "";

            if (cellImages[i] != null)
            {
                if (isFixedCell[i])
                    cellImages[i].color = fixedCellColor;
                else if (val > 0)
                    cellImages[i].color = completedCellColor;
                else
                    cellImages[i].color = emptyCellColor;
            }
        }
    }

    // ---- Mini-grid builder ----

    private void BuildMiniGrid()
    {
        if (gridParent == null) return;

        // Clear any existing children
        for (int i = gridParent.childCount - 1; i >= 0; i--)
            Destroy(gridParent.GetChild(i).gameObject);

        // Add a GridLayoutGroup if not present
        var glg = gridParent.GetComponent<GridLayoutGroup>();
        if (glg == null) glg = gridParent.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(cellSize, cellSize);
        glg.spacing = new Vector2(1f, 1f);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 9;

        cellImages = new Image[81];
        cellTexts  = new TMP_Text[81];

        for (int i = 0; i < 81; i++)
        {
            var go = new GameObject($"OCell_{i}", typeof(RectTransform));
            go.transform.SetParent(gridParent, false);

            var bg = go.AddComponent<Image>();
            bg.color = emptyCellColor;
            cellImages[i] = bg;

            // Number label
            var textGO = new GameObject("Num", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var tmp = textGO.AddComponent<TMP_Text>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 4f;
            tmp.fontSizeMax = 18f;
            tmp.color = new Color(0.15f, 0.1f, 0.05f, 1f);
            cellTexts[i] = tmp;
        }
    }

    private void MarkFixedCells()
    {
        // Read the local puzzle's fixed cells and mirror them for display
        var gm = SudokuGameManager.Instance;
        if (gm == null) return;

        var puzzle = gm.GetCurrentPuzzle();
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                isFixedCell[r * 9 + c] = puzzle[r, c] != 0;
    }
}
