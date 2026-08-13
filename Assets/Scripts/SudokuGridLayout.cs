using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(GridLayoutGroup))]
[RequireComponent(typeof(RectTransform))]
public class SudokuGridLayout : MonoBehaviour
{
    [Header("Big Grid — outer 3x3 (arranges the 9 boxes)")]
    [SerializeField] private RectOffset bigGridPadding;
    [SerializeField] private Vector2 bigGridCellSize = new Vector2(287f, 345f);
    [SerializeField] private Vector2 bigGridSpacing = new Vector2(20f, 25f);
    [SerializeField] private Color boxBorderColor = new Color(0.35f, 0.2f, 0.08f, 1f);

    [Header("Small Grid — inner 3x3 (arranges cells inside each box)")]
    [SerializeField] private RectOffset smallGridPadding;
    [SerializeField] private Vector2 smallGridCellSize = new Vector2(92.8f, 112f);
    [SerializeField] private Vector2 smallGridSpacing = new Vector2(3.6f, 4f);

    [Header("Cell Appearance")]
    [SerializeField] private TMP_FontAsset cellFontAsset;
    [SerializeField] private Color cellTextColor = new Color(0.25f, 0.15f, 0.05f, 1f);
    [SerializeField] private Color cellDefaultBgColor = new Color(1f, 1f, 1f, 0f); // transparent, board art shows through
    [SerializeField] private Color cellSelectedBgColor = new Color(1f, 0.85f, 0.4f, 0.5f);

    private const int BoxCount = 3;   // 3x3 boxes
    private const int CellCount = 3;  // 3x3 small cells inside each box

    private GridLayoutGroup outerGrid;
    private RectTransform gridRect;

    // Indexed by global row/col, 0-8, 0-8
    public SudokuCell[,] Cells { get; private set; }
    public SudokuCell SelectedCell { get; private set; }

    private void Awake()
    {
        outerGrid = GetComponent<GridLayoutGroup>();
        gridRect = GetComponent<RectTransform>();

        if (bigGridPadding == null) bigGridPadding = new RectOffset(0, 0, 0, 0);
        if (smallGridPadding == null) smallGridPadding = new RectOffset(0, 0, 0, 0);

        // Built in Awake (not Start) so the grid exists before ANY other
        // script's Start() runs. Previously this ran in Start(), which raced
        // against SudokuGameManager.Start() -> PopulateBoard(): if the
        // GameManager's Start() fired first, it would build + populate the
        // board, then this Start() would fire afterward and rebuild an empty
        // board on top of it, wiping out the fixed numbers.
        BuildBoard();
    }

    private void BuildBoard()
    {
        // Clear any leftover children (useful if regenerating on restart)
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        Cells = new SudokuCell[BoxCount * CellCount, BoxCount * CellCount];

        outerGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        outerGrid.constraintCount = BoxCount;
        outerGrid.padding = bigGridPadding;
        outerGrid.spacing = bigGridSpacing;
        outerGrid.cellSize = bigGridCellSize;

        for (int boxRow = 0; boxRow < BoxCount; boxRow++)
        {
            for (int boxCol = 0; boxCol < BoxCount; boxCol++)
            {
                CreateBox(boxRow, boxCol);
            }
        }
    }

    private void CreateBox(int boxRow, int boxCol)
    {
        GameObject boxGO = new GameObject($"Box_{boxRow}_{boxCol}", typeof(RectTransform));
        boxGO.transform.SetParent(transform, false);

        // This background shows through as the border between boxes,
        // since the inner grid is padded/spaced inward from this image's edges.
        Image boxBg = boxGO.AddComponent<Image>();
        boxBg.color = boxBorderColor;

        GridLayoutGroup innerGrid = boxGO.AddComponent<GridLayoutGroup>();
        innerGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        innerGrid.constraintCount = CellCount;
        innerGrid.padding = smallGridPadding;
        innerGrid.spacing = smallGridSpacing;
        innerGrid.cellSize = smallGridCellSize;

        for (int r = 0; r < CellCount; r++)
        {
            for (int c = 0; c < CellCount; c++)
            {
                int globalRow = boxRow * CellCount + r;
                int globalCol = boxCol * CellCount + c;
                Cells[globalRow, globalCol] = CreateCell(boxGO.transform, globalRow, globalCol);
            }
        }
    }

    private SudokuCell CreateCell(Transform parent, int globalRow, int globalCol)
    {
        GameObject cellGO = new GameObject($"Cell_{globalRow}_{globalCol}", typeof(RectTransform));
        cellGO.transform.SetParent(parent, false);

        Image bgImage = cellGO.AddComponent<Image>();
        bgImage.color = cellDefaultBgColor;

        Button button = cellGO.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        button.colors = colors;

        GameObject textGO = new GameObject("Number", typeof(RectTransform));
        textGO.transform.SetParent(cellGO.transform, false);

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI numberText = textGO.AddComponent<TextMeshProUGUI>();
        numberText.alignment = TextAlignmentOptions.Center;
        numberText.color = cellTextColor;
        numberText.enableAutoSizing = true;
        numberText.fontSizeMin = 10f;
        numberText.fontSizeMax = 72f;
        numberText.text = "";
        if (cellFontAsset != null) numberText.font = cellFontAsset;

        // Small pencil-mark notes grid (3x3: 1 2 3 / 4 5 6 / 7 8 9), sits
        // behind/alongside the main number and is only shown when the cell
        // is empty and has at least one note toggled on.
        GameObject notesGO = new GameObject("Notes", typeof(RectTransform));
        notesGO.transform.SetParent(cellGO.transform, false);

        RectTransform notesRect = notesGO.GetComponent<RectTransform>();
        notesRect.anchorMin = Vector2.zero;
        notesRect.anchorMax = Vector2.one;
        notesRect.offsetMin = Vector2.zero;
        notesRect.offsetMax = Vector2.zero;

        TextMeshProUGUI notesText = notesGO.AddComponent<TextMeshProUGUI>();
        notesText.alignment = TextAlignmentOptions.Center;
        notesText.color = new Color(cellTextColor.r, cellTextColor.g, cellTextColor.b, 0.65f);
        notesText.enableAutoSizing = false;
        notesText.fontSize = 16f;
        notesText.lineSpacing = -10f;
        notesText.text = "";
        notesText.gameObject.SetActive(false);
        if (cellFontAsset != null) notesText.font = cellFontAsset;

        SudokuCell cell = cellGO.AddComponent<SudokuCell>();
        cell.Initialize(globalRow, globalCol, button, bgImage, numberText, cellDefaultBgColor, cellSelectedBgColor, cellTextColor, notesText);
        cell.OnCellClicked += HandleCellClicked;

        return cell;
    }

    public void PopulateBoard(int[,] puzzle)
    {
        if (Cells == null) BuildBoard();

        for (int r = 0; r < BoxCount * CellCount; r++)
        {
            for (int c = 0; c < BoxCount * CellCount; c++)
            {
                int val = puzzle[r, c];
                if (val != 0)
                {
                    Cells[r, c].SetFixedNumber(val);
                }
                else
                {
                    Cells[r, c].ClearCell();
                }
            }
        }
    }

    public void ClearSelection()
    {
        if (SelectedCell != null)
        {
            SelectedCell.SetSelected(false);
            SelectedCell = null;
        }
    }

    private void HandleCellClicked(SudokuCell cell)
    {
        if (SelectedCell != null)
            SelectedCell.SetSelected(false);

        SelectedCell = cell;
        SelectedCell.SetSelected(true);
    }
}