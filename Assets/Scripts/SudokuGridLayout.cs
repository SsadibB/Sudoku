using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(RectTransform))]
public class SudokuGridLayout : MonoBehaviour
{
    private const int BoardDimension = 9; // 9x9 grid
    private const int BoxSize = 3;        // 3x3 grouping

    [Header("Cell Prefab")]
    [Tooltip("Prefab for a single board cell. Must have a SudokuCell component on its root, an Image on the root (used as the cell background), a child named exactly \"Number\" with a TextMeshProUGUI, and optionally a child named exactly \"Notes\" with a TextMeshProUGUI for pencil marks.")]
    [SerializeField] private SudokuCell cellPrefab;

    [Header("Grid Size")]
    [Tooltip("When enabled, the 9x9 grid is exactly Grid Size x Grid Size pixels and the Board is resized to fit it (Grid Size + Board Padding). When disabled, the grid fills whatever space the Board already has.")]
    [SerializeField] private bool useFixedGridSize = true;
    [Tooltip("Width AND height of the 9x9 grid in pixels (the grid is always square).")]
    [SerializeField] private float gridSize = 840f;

    [Header("Grid Layout & Padding")]
    [Tooltip("When enabled, the gap between the board border and the cells equals the sub-box spacing (X for left/right, Y for top/bottom), plus Extra Border Offset. Overrides Board Padding below.")]
    [SerializeField] private bool matchPaddingToSubBoxSpacing = true;
    [Tooltip("Extra pixels added on every side when matching, e.g. the thickness of the board's drawn border. Use 0 to match the sub-box gap exactly.")]
    [SerializeField] private float extraBorderOffset = 0f;
    [Tooltip("Padding inside the Board RectTransform to keep cells safely within the dark board borders. Ignored while Match Padding To Sub Box Spacing is on.")]
    [SerializeField] private RectOffset boardPadding;
    [Tooltip("Spacing between adjacent individual cells inside each 3x3 sub-box in pixels.")]
    [SerializeField] private Vector2 cellSpacing = new Vector2(5f, 5f);

    [Header("Sub-Box Spacing (Distance between the 9 3x3 sub-boxes)")]
    [Tooltip("Horizontal gap between the left, middle, and right 3x3 sub-boxes.")]
    [SerializeField] private float subBoxSpacingX = 14f;
    [Tooltip("Vertical gap between the top, middle, and bottom 3x3 sub-boxes.")]
    [SerializeField] private float subBoxSpacingY = 14f;

    [Header("Cell Appearance")]
    [SerializeField] private TMP_FontAsset cellFontAsset;
    [SerializeField] private Color cellTextColor = new Color(0.25f, 0.15f, 0.05f, 1f);
    [Tooltip("Default background color for cells in even 3x3 boxes (corner boxes & center box).")]
    [SerializeField] private Color cellDefaultBgColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Subtle background tint for cells in odd 3x3 boxes (edge boxes) to enhance 3x3 visual grouping.")]
    [SerializeField] private Color cellAltBoxBgColor = new Color(0.94f, 0.95f, 0.98f, 1f);
    [SerializeField] private Color cellSelectedBgColor = new Color(1f, 0.86f, 0.40f, 1f);
    [SerializeField] private Color cellHighlightBgColor = new Color(0.84f, 0.88f, 0.95f, 1f);
    [Tooltip("When enabled, cell number font size is automatically calculated from cell height.")]
    [SerializeField] private bool autoScaleFontSize = true;
    [Tooltip("Fixed point size fallback for every cell's number if autoScaleFontSize is disabled.")]
    [SerializeField] private float cellNumberFontSize = 52f;
    [Tooltip("Scale applied to every cell on the board that shares the selected cell's number, for as long as that cell stays selected.")]
    [SerializeField] private float sameNumberEnlargeScale = 1.18f;
    [SerializeField] private float sameNumberEnlargeAnimDuration = 0.2f;

    [Header("3x3 Grouping Dividers")]
    [SerializeField] private bool showDividerLines = true;
    [SerializeField] private float dividerLineWidth = 3f;
    [SerializeField] private Color dividerLineColor = new Color(0.18f, 0.22f, 0.28f, 1f);

    private GridLayoutGroup gridLayoutGroup;
    private RectTransform gridRect;
    private RectTransform boardRect;
    private RectTransform dividerContainer;
    private float calculatedCellSide = 85f;
    private float calculatedFontSize = 50f;

    public float SubBoxSpacingX
    {
        get => subBoxSpacingX;
        set { subBoxSpacingX = value; RecalculateLayout(); }
    }

    public float SubBoxSpacingY
    {
        get => subBoxSpacingY;
        set { subBoxSpacingY = value; RecalculateLayout(); }
    }

    // Indexed by global row/col, 0-8, 0-8
    public SudokuCell[,] Cells { get; private set; }
    public SudokuCell SelectedCell { get; private set; }

    private void Awake()
    {
        gridLayoutGroup = GetComponent<GridLayoutGroup>();
        gridRect = GetComponent<RectTransform>();
        if (transform.parent != null) boardRect = transform.parent as RectTransform;

        if (boardPadding == null) boardPadding = new RectOffset(36, 36, 36, 36);

        // Safeguard against stale zero-alpha serialized color values
        if (cellDefaultBgColor.a < 0.05f) cellDefaultBgColor = Color.white;
        if (cellAltBoxBgColor.a < 0.05f) cellAltBoxBgColor = new Color(0.94f, 0.95f, 0.98f, 1f);
        if (cellSelectedBgColor.a < 0.05f) cellSelectedBgColor = new Color(1f, 0.86f, 0.40f, 1f);
        if (cellHighlightBgColor.a < 0.05f) cellHighlightBgColor = new Color(0.84f, 0.88f, 0.95f, 1f);

        // Built in Awake (not Start) so the grid exists before ANY other
        // script's Start() runs.
        BuildBoard();
    }

    private void OnValidate()
    {
        if (gridRect != null && boardRect != null)
        {
            RecalculateLayout();
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled && gridRect != null && boardRect != null)
        {
            RecalculateLayout();
        }
    }

    public void RecalculateLayout()
    {
        if (gridRect == null) gridRect = GetComponent<RectTransform>();
        if (gridLayoutGroup == null) gridLayoutGroup = GetComponent<GridLayoutGroup>();
        if (boardRect == null && transform.parent != null) boardRect = transform.parent as RectTransform;

        if (boardRect == null) return;

        if (boardPadding == null) boardPadding = new RectOffset(36, 36, 36, 36);
        RectOffset pad = GetEffectivePadding();

        // Ensure Board has a RectMask2D for absolute hardware clipping
        var mask = boardRect.GetComponent<RectMask2D>();
        if (mask == null)
        {
            boardRect.gameObject.AddComponent<RectMask2D>();
        }

        float availSide;
        if (useFixedGridSize)
        {
            availSide = Mathf.Max(50f, gridSize);

            // The Board has a RectMask2D, so it must be big enough to hold the
            // grid plus padding or the edges would be clipped.
            boardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availSide + pad.left + pad.right);
            boardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, availSide + pad.top + pad.bottom);
        }
        else
        {
            // Available square area inside the Board's RectTransform
            float availW = Mathf.Max(50f, boardRect.rect.width - (pad.left + pad.right));
            float availH = Mathf.Max(50f, boardRect.rect.height - (pad.top + pad.bottom));
            availSide = Mathf.Min(availW, availH);
        }

        // 6 internal cell spacings (2 inside each of the 3 sub-boxes) + 2 sub-box spacings
        float totalSpacingX = cellSpacing.x * 6f + subBoxSpacingX * 2f;
        float totalSpacingY = cellSpacing.y * 6f + subBoxSpacingY * 2f;

        float maxCellW = (availSide - totalSpacingX) / BoardDimension;
        float maxCellH = (availSide - totalSpacingY) / BoardDimension;
        calculatedCellSide = Mathf.Min(maxCellW, maxCellH);
        // Whole-pixel cells normally, but keep the exact fractional size when a
        // fixed grid size is requested so the grid is exactly gridSize wide/tall.
        if (!useFixedGridSize) calculatedCellSide = Mathf.Floor(calculatedCellSide);
        calculatedCellSide = Mathf.Max(10f, calculatedCellSide);

        if (autoScaleFontSize)
        {
            calculatedFontSize = Mathf.Round(calculatedCellSide * 0.58f);
        }
        else
        {
            calculatedFontSize = cellNumberFontSize;
        }

        float totalGridW = calculatedCellSide * BoardDimension + totalSpacingX;
        float totalGridH = calculatedCellSide * BoardDimension + totalSpacingY;

        // Position and size Grid centered inside Board
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.anchoredPosition = Vector2.zero;
        gridRect.sizeDelta = new Vector2(totalGridW, totalGridH);

        // Keep GridLayoutGroup metadata consistent, but disable it so custom
        // sub-box spacing positioning is used without uniform cell override
        gridLayoutGroup.enabled = false;
        gridLayoutGroup.cellSize = new Vector2(calculatedCellSide, calculatedCellSide);
        gridLayoutGroup.spacing = cellSpacing;

        // Position all cells with separate cell spacing and sub-box spacing
        PositionCells(totalGridW, totalGridH);

        // Update divider lines if present
        UpdateDividerLines(totalGridW, totalGridH);

        // Update font size on existing cells if already built
        if (Cells != null)
        {
            for (int r = 0; r < BoardDimension; r++)
            {
                for (int c = 0; c < BoardDimension; c++)
                {
                    SudokuCell cell = Cells[r, c];
                    if (cell != null)
                    {
                        var txt = cell.transform.Find("Number")?.GetComponent<TextMeshProUGUI>();
                        if (txt != null)
                        {
                            txt.fontSize = calculatedFontSize;
                        }
                    }
                }
            }
        }
    }

    // Gap between the Board edge and the cells. When matching is on, it equals
    // the gap between the 3x3 sub-boxes so the outer margin looks identical.
    private RectOffset GetEffectivePadding()
    {
        if (!matchPaddingToSubBoxSpacing) return boardPadding;

        int x = Mathf.RoundToInt(subBoxSpacingX + extraBorderOffset);
        int y = Mathf.RoundToInt(subBoxSpacingY + extraBorderOffset);
        return new RectOffset(x, x, y, y);
    }

    private void PositionCells(float gridW, float gridH)
    {
        for (int r = 0; r < BoardDimension; r++)
        {
            int boxR = r / BoxSize;
            float yFromTop = r * calculatedCellSide + (r - boxR) * cellSpacing.y + boxR * subBoxSpacingY + calculatedCellSide * 0.5f;
            float anchoredY = gridH * 0.5f - yFromTop;

            for (int c = 0; c < BoardDimension; c++)
            {
                RectTransform rt = null;
                if (Cells != null && Cells[r, c] != null)
                {
                    rt = Cells[r, c].GetComponent<RectTransform>();
                }
                else
                {
                    Transform t = transform.Find($"Cell_{r}_{c}");
                    if (t != null) rt = t as RectTransform;
                }

                if (rt != null)
                {
                    int boxC = c / BoxSize;
                    float xFromLeft = c * calculatedCellSide + (c - boxC) * cellSpacing.x + boxC * subBoxSpacingX + calculatedCellSide * 0.5f;
                    float anchoredX = xFromLeft - gridW * 0.5f;

                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(calculatedCellSide, calculatedCellSide);
                    rt.anchoredPosition = new Vector2(anchoredX, anchoredY);
                }
            }
        }
    }

    private void UpdateDividerLines(float gridW, float gridH)
    {
        if (!showDividerLines || boardRect == null)
        {
            if (dividerContainer != null) dividerContainer.gameObject.SetActive(false);
            return;
        }

        if (dividerContainer == null)
        {
            Transform existing = boardRect.Find("DividerLines");
            if (existing != null)
            {
                dividerContainer = existing as RectTransform;
            }
            else
            {
                GameObject divGO = new GameObject("DividerLines", typeof(RectTransform));
                divGO.transform.SetParent(boardRect, false);
                divGO.transform.SetSiblingIndex(gridRect.GetSiblingIndex() + 1);
                dividerContainer = divGO.GetComponent<RectTransform>();
            }
        }

        dividerContainer.gameObject.SetActive(true);
        dividerContainer.anchorMin = new Vector2(0.5f, 0.5f);
        dividerContainer.anchorMax = new Vector2(0.5f, 0.5f);
        dividerContainer.pivot = new Vector2(0.5f, 0.5f);
        dividerContainer.anchoredPosition = Vector2.zero;
        dividerContainer.sizeDelta = new Vector2(gridW, gridH);

        // Gap centers between cols 2&3 and cols 5&6:
        float x1 = 3 * calculatedCellSide + 2 * cellSpacing.x + subBoxSpacingX * 0.5f - gridW * 0.5f;
        float x2 = 6 * calculatedCellSide + 4 * cellSpacing.x + 1.5f * subBoxSpacingX - gridW * 0.5f;
        // Gap centers between rows 2&3 and rows 5&6:
        float y1 = gridH * 0.5f - (3 * calculatedCellSide + 2 * cellSpacing.y + subBoxSpacingY * 0.5f);
        float y2 = gridH * 0.5f - (6 * calculatedCellSide + 4 * cellSpacing.y + 1.5f * subBoxSpacingY);

        SetupLine("Line_V1", new Vector2(x1, 0), new Vector2(dividerLineWidth, gridH));
        SetupLine("Line_V2", new Vector2(x2, 0), new Vector2(dividerLineWidth, gridH));
        SetupLine("Line_H1", new Vector2(0, y1), new Vector2(gridW, dividerLineWidth));
        SetupLine("Line_H2", new Vector2(0, y2), new Vector2(gridW, dividerLineWidth));
    }

    private void SetupLine(string lineName, Vector2 pos, Vector2 size)
    {
        Transform t = dividerContainer.Find(lineName);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(lineName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(dividerContainer, false);
        }
        else
        {
            go = t.gameObject;
        }

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = dividerLineColor;
        img.raycastTarget = false;
    }

    private void BuildBoard()
    {
        // Clear any leftover children (useful if regenerating on restart)
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        // Clear existing divider lines to prevent duplicates
        if (boardRect != null)
        {
            Transform existingLines = boardRect.Find("DividerLines");
            if (existingLines != null)
            {
                Destroy(existingLines.gameObject);
                dividerContainer = null;
            }
        }

        RecalculateLayout();

        Cells = new SudokuCell[BoardDimension, BoardDimension];

        for (int r = 0; r < BoardDimension; r++)
        {
            for (int c = 0; c < BoardDimension; c++)
            {
                Cells[r, c] = CreateCell(r, c);
            }
        }

        // Apply exact positions for all 81 instantiated cells
        float totalSpacingX = cellSpacing.x * 6f + subBoxSpacingX * 2f;
        float totalSpacingY = cellSpacing.y * 6f + subBoxSpacingY * 2f;
        float totalGridW = calculatedCellSide * BoardDimension + totalSpacingX;
        float totalGridH = calculatedCellSide * BoardDimension + totalSpacingY;
        PositionCells(totalGridW, totalGridH);
    }

    private SudokuCell CreateCell(int globalRow, int globalCol)
    {
        if (cellPrefab == null)
        {
            Debug.LogError("SudokuGridLayout: no Cell Prefab assigned in the Inspector.", this);
            return null;
        }

        SudokuCell cell = Instantiate(cellPrefab, transform);
        cell.name = $"Cell_{globalRow}_{globalCol}";
        cell.transform.localScale = Vector3.one;

        Image bgImage = cell.GetComponent<Image>();
        Button button = cell.GetComponent<Button>();

        if (bgImage != null)
        {
            bgImage.enabled = true;
        }

        if (button != null)
        {
            button.interactable = true;
        }

        int boxRow = globalRow / BoxSize;
        int boxCol = globalCol / BoxSize;
        bool isAltBox = (boxRow + boxCol) % 2 != 0;
        Color defaultBg = isAltBox ? cellAltBoxBgColor : cellDefaultBgColor;

        // Expected child names on the prefab: "Number" (always present) and
        // "Notes" (optional — pencil-mark 3x3 mini grid, hidden by default).
        TextMeshProUGUI numberText = cell.transform.Find("Number")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI notesText = cell.transform.Find("Notes")?.GetComponent<TextMeshProUGUI>();

        if (numberText == null)
            Debug.LogError($"SudokuGridLayout: cell prefab is missing a child named \"Number\" with a TextMeshProUGUI.", cell);

        if (numberText != null)
        {
            RectTransform numRect = numberText.rectTransform;
            numRect.anchorMin = Vector2.zero;
            numRect.anchorMax = Vector2.one;
            numRect.pivot = new Vector2(0.5f, 0.5f);
            numRect.anchoredPosition = Vector2.zero;
            numRect.sizeDelta = Vector2.zero;
            numRect.localScale = Vector3.one;

            numberText.raycastTarget = false;
            numberText.enableAutoSizing = false;
            numberText.fontSize = calculatedFontSize;
            numberText.text = "";
            numberText.alignment = TextAlignmentOptions.Center;
            numberText.color = cellTextColor;
            if (cellFontAsset != null) numberText.font = cellFontAsset;
        }

        if (notesText != null)
        {
            notesText.text = "";
            notesText.gameObject.SetActive(false);
            if (cellFontAsset != null) notesText.font = cellFontAsset;
        }

        cell.Initialize(globalRow, globalCol, button, bgImage, numberText, defaultBg, cellSelectedBgColor, cellTextColor, notesText, cellHighlightBgColor);
        cell.OnCellClicked += HandleCellClicked;

        return cell;
    }

    public void PopulateBoard(int[,] puzzle)
    {
        if (Cells == null) BuildBoard();

        for (int r = 0; r < BoardDimension; r++)
        {
            for (int c = 0; c < BoardDimension; c++)
            {
                int val = puzzle[r, c];
                if (val != 0)
                {
                    Cells[r, c].SetFixedNumber(val);
                }
                else
                {
                    Cells[r, c].ResetForNewPuzzle();
                }
            }
        }
    }

    public void SelectCell(int row, int col)
    {
        if (Cells == null) return;
        if (row >= 0 && row < BoardDimension && col >= 0 && col < BoardDimension && Cells[row, col] != null)
        {
            HandleCellClicked(Cells[row, col]);
        }
    }

    public void ClearSelection()
    {
        if (SelectedCell != null)
        {
            SelectedCell.SetSelected(false);
            SelectedCell = null;
        }
        ClearHighlights();
        ClearEnlarged();
    }

    private void HandleCellClicked(SudokuCell cell)
    {
        if (SelectedCell != null)
            SelectedCell.SetSelected(false);

        SelectedCell = cell;
        SelectedCell.SetSelected(true);

        RefreshHighlights(cell);
    }

    // Greys out the selected cell's entire row, entire column, and its
    // containing 3x3 box (the selected cell itself is left at its
    // "selected" color, not the grey highlight). Also enlarges every cell
    // on the board (including the selected cell itself) that shares the
    // selected cell's number, for as long as it stays selected.
    private void RefreshHighlights(SudokuCell selected)
    {
        if (Cells == null) return;

        int boxRowStart = (selected.Row / BoxSize) * BoxSize;
        int boxColStart = (selected.Col / BoxSize) * BoxSize;
        int selectedNumber = selected.GetNumber();

        for (int r = 0; r < BoardDimension; r++)
        {
            for (int c = 0; c < BoardDimension; c++)
            {
                SudokuCell cell = Cells[r, c];
                if (cell == null) continue;

                bool sameNumber = selectedNumber != 0 && cell.GetNumber() == selectedNumber;
                cell.SetEnlarged(sameNumber, sameNumberEnlargeScale, sameNumberEnlargeAnimDuration);

                if (cell == selected)
                {
                    cell.SetHighlighted(false);
                    continue;
                }

                bool sameRow = r == selected.Row;
                bool sameCol = c == selected.Col;
                bool sameBox = r >= boxRowStart && r < boxRowStart + BoxSize &&
                               c >= boxColStart && c < boxColStart + BoxSize;

                cell.SetHighlighted(sameRow || sameCol || sameBox);
            }
        }
    }

    private void ClearHighlights()
    {
        if (Cells == null) return;

        for (int r = 0; r < Cells.GetLength(0); r++)
        {
            for (int c = 0; c < Cells.GetLength(1); c++)
            {
                Cells[r, c]?.SetHighlighted(false);
            }
        }
    }

    private void ClearEnlarged()
    {
        if (Cells == null) return;

        for (int r = 0; r < Cells.GetLength(0); r++)
        {
            for (int c = 0; c < Cells.GetLength(1); c++)
            {
                Cells[r, c]?.SetEnlarged(false, sameNumberEnlargeScale, sameNumberEnlargeAnimDuration);
            }
        }
    }
}