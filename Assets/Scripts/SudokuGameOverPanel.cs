using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class SudokuGameOverPanel : MonoBehaviour
{
    [Header("Panel UI Elements")]
    [SerializeField] private GameObject panelCanvas;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button restartButton;

    [Header("Title Text Strings")]
    [SerializeField] private string gameOverTitle = "GAME OVER";
    [SerializeField] private string winTitle = "VICTORY!";

    [Header("Animation Settings")]
    [SerializeField] private float animDuration = 0.4f;

    public System.Action OnRestartClicked;

    private Sequence showSequence;

    private void Awake()
    {
        if (panelCanvas != null)
            panelCanvas.SetActive(false);

        if (restartButton != null)
            restartButton.onClick.AddListener(HandleRestartClicked);
    }

    private void OnDestroy()
    {
        if (restartButton != null)
            restartButton.onClick.RemoveListener(HandleRestartClicked);

        showSequence?.Kill();
    }

    public void ShowGameOver()
    {
        ShowPanel(gameOverTitle, new Color(0.9f, 0.2f, 0.15f, 1f));
    }

    public void ShowWin()
    {
        ShowPanel(winTitle, new Color(1f, 0.84f, 0f, 1f));
    }

    private void ShowPanel(string title, Color titleColor)
    {
        if (panelCanvas == null) return;

        showSequence?.Kill();

        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = titleColor;
        }

        panelCanvas.SetActive(true);

        if (canvasGroup == null)
            canvasGroup = panelCanvas.GetComponent<CanvasGroup>();

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        panelCanvas.transform.localScale = Vector3.one * 0.7f;

        showSequence = DOTween.Sequence()
            .Append(panelCanvas.transform.DOScale(1f, animDuration).SetEase(Ease.OutBack))
            .Join(canvasGroup != null ? canvasGroup.DOFade(1f, animDuration) : null)
            .SetLink(panelCanvas);
    }

    public void HidePanel()
    {
        if (panelCanvas == null) return;

        showSequence?.Kill();

        if (canvasGroup != null)
        {
            showSequence = DOTween.Sequence()
                .Append(canvasGroup.DOFade(0f, animDuration))
                .Join(panelCanvas.transform.DOScale(0.8f, animDuration))
                .OnComplete(() => panelCanvas.SetActive(false))
                .SetLink(panelCanvas);
        }
        else
        {
            panelCanvas.SetActive(false);
        }
    }

    private void HandleRestartClicked()
    {
        HidePanel();
        OnRestartClicked?.Invoke();
    }
}
