using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    private Color fixedTextColor;
    private Color correctTextColor = new Color(0.18f, 0.55f, 0.18f, 1f); // Vibrant Green
    private Color wrongTextColor = new Color(0.85f, 0.15f, 0.15f, 1f);  // Vibrant Red

    // Index 1-9 used, index 0 unused. Tracks which pencil-mark notes are on.
    private readonly bool[] noteFlags = new bool[10];

    public void Initialize(int row, int col, Button btn, Image bg, TextMeshProUGUI text, Color defaultBg, Color selectedBg, Color defaultTextColor, TextMeshProUGUI notes = null)
    {
        Row = row;
        Col = col;
        button = btn;
        background = bg;
        numberText = text;
        notesText = notes;
        defaultBgColor = defaultBg;
        selectedBgColor = selectedBg;
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
        button.interactable = false;
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
        button.interactable = false;
    }

    public void ClearCell()
    {
        if (IsFixed) return;

        ClearNotes();
        IsCorrect = false;
        numberText.text = "";
        numberText.color = fixedTextColor;
    }

    public int GetNumber()
    {
        return string.IsNullOrEmpty(numberText.text) ? 0 : int.Parse(numberText.text);
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedBgColor : defaultBgColor;
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
        // aligned no matter which numbers are on.
        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int n = row * 3 + col + 1;
                sb.Append(noteFlags[n] ? n.ToString() : "\u00A0");
                if (col < 2) sb.Append(' ');
            }
            if (row < 2) sb.Append('\n');
        }
        notesText.text = sb.ToString();
    }
}