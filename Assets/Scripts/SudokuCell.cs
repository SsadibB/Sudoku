using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SudokuCell : MonoBehaviour
{
    public int Row { get; private set; }
    public int Col { get; private set; }
    public bool IsFixed { get; private set; }

    public System.Action<SudokuCell> OnCellClicked;

    private Button button;
    private Image background;
    private TextMeshProUGUI numberText;

    private Color defaultBgColor;
    private Color selectedBgColor;

    public void Initialize(int row, int col, Button btn, Image bg, TextMeshProUGUI text, Color defaultBg, Color selectedBg)
    {
        Row = row;
        Col = col;
        button = btn;
        background = bg;
        numberText = text;
        defaultBgColor = defaultBg;
        selectedBgColor = selectedBg;

        background.color = defaultBgColor;
        button.onClick.AddListener(() => OnCellClicked?.Invoke(this));
    }

    public void SetNumber(int number, bool fixedNumber = false)
    {
        numberText.text = number == 0 ? "" : number.ToString();
        IsFixed = fixedNumber;

        // Fixed starting numbers aren't editable by the player
        button.interactable = !fixedNumber;
    }

    public int GetNumber()
    {
        return string.IsNullOrEmpty(numberText.text) ? 0 : int.Parse(numberText.text);
    }

    public void SetSelected(bool selected)
    {
        background.color = selected ? selectedBgColor : defaultBgColor;
    }
}