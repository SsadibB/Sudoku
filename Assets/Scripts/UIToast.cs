using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class UIToast : MonoBehaviour
{
    private static UIToast instance;

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private RectTransform toastRect;

    private Coroutine hideCoroutine;
    private Tween fadeTween;

    public static void Show(string message, float duration = 2.5f)
    {
        if (instance == null)
        {
            CreateToastInstance();
        }

        if (instance != null)
        {
            instance.Display(message, duration);
        }
    }

    private static void CreateToastInstance()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[UIToast] No active Canvas found to show toast.");
            return;
        }

        GameObject toastGO = new GameObject("ToastNotification", typeof(RectTransform), typeof(CanvasGroup));
        toastGO.transform.SetParent(canvas.transform, false);

        RectTransform rect = toastGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.85f);
        rect.anchorMax = new Vector2(0.5f, 0.85f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(580f, 80f);
        rect.anchoredPosition = Vector2.zero;

        // Background
        Image bgImage = toastGO.AddComponent<Image>();
        bgImage.color = new Color(0.12f, 0.14f, 0.18f, 0.92f); // Sleek dark slate
        bgImage.raycastTarget = false;

        // Message text
        GameObject textGO = new GameObject("ToastText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(toastGO.transform, false);

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = new Vector2(-40f, -16f);
        textRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = 28f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;

        CanvasGroup cg = toastGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        UIToast toast = toastGO.AddComponent<UIToast>();
        toast.canvasGroup = cg;
        toast.messageText = tmp;
        toast.toastRect = rect;

        instance = toast;
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void Display(string message, float duration)
    {
        if (messageText == null || canvasGroup == null) return;

        // Bring to front
        transform.SetAsLastSibling();

        messageText.text = message;

        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }

        fadeTween?.Kill();

        gameObject.SetActive(true);
        canvasGroup.alpha = 0f;
        if (toastRect != null)
        {
            toastRect.localScale = Vector3.one * 0.85f;
            toastRect.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
        }

        fadeTween = canvasGroup.DOFade(1f, 0.25f);
        hideCoroutine = StartCoroutine(HideRoutine(duration));
    }

    private IEnumerator HideRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        fadeTween?.Kill();
        fadeTween = canvasGroup.DOFade(0f, 0.3f).OnComplete(() =>
        {
            gameObject.SetActive(false);
        });
    }
}
