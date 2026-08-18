using UnityEngine;
using TMPro;

// Drop this on any GameObject that has a TMP_Text and already shows your
// normal English text — nothing else to fill in here. Run Tools >
// Localization > Generate Translations once (and again whenever you add or
// change text) to pre-translate everything; this component just looks up
// the result, instantly and offline.
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    private TMP_Text label;
    private string originalEnglishText;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        originalEnglishText = label.text;
    }

    private void OnEnable()
    {
        Apply();

        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged += Apply;
    }

    private void OnDisable()
    {
        if (LocalizationManager.Instance != null)
            LocalizationManager.Instance.OnLanguageChanged -= Apply;
    }

    private void Apply()
    {
        if (LocalizationManager.Instance == null || label == null || string.IsNullOrEmpty(originalEnglishText))
            return;

        label.text = LocalizationManager.Instance.Translate(originalEnglishText);
    }
}