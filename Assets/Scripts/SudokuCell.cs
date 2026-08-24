using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class SudokuCell : MonoBehaviour
{
    public int Row { get; private set; }
    public int Col { get; private set; }
    public bool IsFixed { get; private set; }
    public bool IsCorrect { get; private set; }

    public System.Action<SudokuCell> OnCellClicked;

    private Button button;
    private Image background;
    private TextMeshProUGUI numberText;
    private TextMeshProUGUI notesText;

    private Color defaultBgColor;
    private Color selectedBgColor;
    private Color highlightBgColor;
    private Color fixedTextColor;
    private Color correctTextColor = new Color(0.18f, 0.55f, 0.18f, 1f); // Vibrant Green
    private Color wrongTextColor = new Color(0.85f, 0.15f, 0.15f, 1f);  // Vibrant Red

    // Index 1-9 used, index 0 unused. Tracks which pencil-mark notes are on.
    private readonly bool[] noteFlags = new bool[10];

    // Selected wins over highlighted, which wins over default.
    private bool isSelected;
    private bool isHighlighted;

    public void Initialize(int row, int col, Button btn, Image bg, TextMeshProUGUI text, Color defaultBg, Color selectedBg, Color defaultTextColor, TextMeshProUGUI notes = null, Color? highlightBg = null)
    {
        Row = row;
        Col = col;
        button = btn;
        background = bg;
        numberText = text;
        notesText = notes;
        defaultBgColor = defaultBg;
        selectedBgColor = selectedBg;
        highlightBgColor = highlightBg ?? new Color(0.5f, 0.5f, 0.5f, 0.35f);
        fixedTextColor = defaultTextColor;

        background.color = defaultBgColor;
        button.onClick.AddListener(() => OnCellClicked?.Invoke(this));

        if (notesText != null)
        {
            notesText.text = "";
            notesText.gameObject.SetActive(false);
        }
    }

    public void SetFixedNumber(int number)
    {
        ClearNotes();
        IsFixed = true;
        IsCorrect = true;
        numberText.text = number == 0 ? "" : number.ToString();
        numberText.color = fixedTextColor;
        // Stays interactable: fixed cells must remain selectable so their
        // row/column/box can still be highlighted. Editing is blocked
        // separately (IsFixed checks in SudokuGameManager), not via the
        // button's interactable flag.
    }

    public void SetUserNumber(int number, bool isCorrect)
    {
        if (IsFixed) return;

        ClearNotes();
        IsCorrect = isCorrect;
        numberText.text = number.ToString();
        numberText.color = isCorrect ? correctTextColor : wrongTextColor;
    }

    // A number placed in the right cell locks permanently — same as a
    // fixed/given number, it can no longer be erased or undone, but it
    // keeps the "correct" green color instead of the given-number color.
    public void LockAsCorrect(int number)
    {
        ClearNotes();
        IsFixed = true;
        IsCorrect = true;
        numberText.text = number.ToString();
        numberText.color = correctTextColor;
        // See note in SetFixedNumber() — stays interactable/selectable.
    }

    public void ClearCell()
    {
        if (IsFixed) return;

        ClearNotes();
        IsCorrect = false;
        numberText.text = "";
        numberText.color = fixedTextColor;
    }

    // Full reset for loading a brand new puzzle. Unlike ClearCell() (which
    // deliberately refuses to touch a permanently-locked cell during normal
    // play, e.g. via the Erase button), this always resets back to empty/
    // unfixed regardless of prior state. Needed because SudokuGridLayout
    // builds its 81 SudokuCell objects once and reuses them for every
    // level - after a full Victory every cell is IsFixed (locked correct),
    // so without this, PopulateBoard's ClearCell() calls for the new
    // puzzle's empty cells would silently no-op, leaving the finished
    // board's numbers stuck in place.
    public void ResetForNewPuzzle()
    {
        IsFixed = false;
        IsCorrect = false;
        ClearNotes();
        numberText.text = "";
        numberText.color = fixedTextColor;
    }

    public int GetNumber()
    {
        return string.IsNullOrEmpty(numberText.text) ? 0 : int.Parse(numberText.text);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        RefreshBackground();
    }

    // Greys out this cell as part of the selected cell's row, column, or
    // 3x3 box. Ignored while this cell is itself the selected one.
    public void SetHighlighted(bool highlighted)
    {
        isHighlighted = highlighted;
        RefreshBackground();
    }

    private void RefreshBackground()
    {
        if (isSelected) background.color = selectedBgColor;
        else if (isHighlighted) background.color = highlightBgColor;
        else background.color = defaultBgColor;
    }

    // Persistent scale-up on this cell's number, used while a cell sharing
    // the same number is selected elsewhere on the board (or this cell is
    // itself the selected one). Reverts back to normal size once the flag
    // clears — unlike PulseNumber below, this holds rather than bouncing.
    public void SetEnlarged(bool enlarged, float scale = 1.2f, float duration = 0.2f)
    {
        if (numberText == null) return;

        Transform t = numberText.transform;
        t.DOKill();
        t.DOScale(enlarged ? Vector3.one * scale : Vector3.one, duration).SetEase(Ease.OutQuad).SetLink(numberText.gameObject);
    }

    // Brief scale-punch on this cell's number, used to flag "same number as
    // the currently selected cell" elsewhere on the board.
    public void PulseNumber()
    {
        if (numberText == null || string.IsNullOrEmpty(numberText.text)) return;

        Transform t = numberText.transform;
        t.DOKill();
        t.localScale = Vector3.one;
        t.DOPunchScale(Vector3.one * 0.25f, 0.35f, 6, 0.8f).SetLink(numberText.gameObject);
    }

    // Brief glow on this cell's number: brightens its current color (green
    // for correct, red for wrong) and fades back. Used right after a
    // number is placed to give instant feedback either way.
    public void GlowNumber()
    {
        if (numberText == null || string.IsNullOrEmpty(numberText.text)) return;

        Color baseColor = numberText.color;
        Color glowColor = Color.Lerp(baseColor, Color.white, 0.6f);

        numberText.DOKill();
        numberText.color = baseColor;
        DOTween.Sequence()
            .Append(numberText.DOColor(glowColor, 0.12f))
            .Append(numberText.DOColor(baseColor, 0.25f))
            .SetLink(numberText.gameObject);
    }

    // ---------------- Pencil-mark Notes ----------------

    public void ToggleNote(int number)
    {
        if (IsFixed) return;          // locked/given cells can't take notes
        if (GetNumber() != 0) return; // only empty cells show notes
        if (number < 1 || number > 9) return;

        noteFlags[number] = !noteFlags[number];
        RefreshNotesDisplay();
    }

    public void ClearNotes()
    {
        for (int n = 1; n <= 9; n++) noteFlags[n] = false;

        if (notesText != null)
        {
            notesText.text = "";
            notesText.gameObject.SetActive(false);
        }
    }

    private void RefreshNotesDisplay()
    {
        if (notesText == null) return;

        bool anyNotes = false;
        for (int n = 1; n <= 9; n++)
        {
            if (noteFlags[n]) { anyNotes = true; break; }
        }

        if (!anyNotes)
        {
            notesText.text = "";
            notesText.gameObject.SetActive(false);
            return;
        }

        notesText.gameObject.SetActive(true);

        // 3x3 layout: row1=[1 2 3] row2=[4 5 6] row3=[7 8 9].
        // Un-toggled slots use a non-breaking space so the grid stays
        // aligned no matter which numbers are on. A thin space (not a full
        // space) separates columns, so the auto-sizer can grow the digits
        // as large as possible while still fitting neatly in the cell.
        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int n = row * 3 + col + 1;
                sb.Append(noteFlags[n] ? n.ToString() : "\u00A0");
                if (col < 2) sb.Append('\u2009');
            }
            if (row < 2) sb.Append('\n');
        }
        notesText.text = sb.ToString();
    }
}