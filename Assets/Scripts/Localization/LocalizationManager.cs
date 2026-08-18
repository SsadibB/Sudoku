using System;
using System.Collections.Generic;
using UnityEngine;

// The 7 languages the game supports.
public enum Language
{
    English = 0,
    Japanese = 1,
    Spanish = 2,
    Portuguese = 3,
    Bangla = 4,
    Korean = 5,
    Chinese = 6
}

// Lives on a GameObject in the MainMenu scene. Survives scene loads via
// DontDestroyOnLoad, same setup as SoundManager.
//
// Translations are typed in by hand in the Entries list below — one row
// per piece of text, matched against the English string already sitting on
// each LocalizedText. No network calls, works fully offline.
public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager Instance { get; private set; }

    [Serializable]
    public class LocalizedEntry
    {
        [Tooltip("Must exactly match the English text already on the LocalizedText object(s) this row is for.")]
        public string english;

        [Header("Translations")]
        [TextArea] public string japanese;
        [TextArea] public string spanish;
        [TextArea] public string portuguese;
        [TextArea] public string bangla;
        [TextArea] public string korean;
        [TextArea] public string chinese;

        public string Get(Language language)
        {
            string value;
            switch (language)
            {
                case Language.Japanese: value = japanese; break;
                case Language.Spanish: value = spanish; break;
                case Language.Portuguese: value = portuguese; break;
                case Language.Bangla: value = bangla; break;
                case Language.Korean: value = korean; break;
                case Language.Chinese: value = chinese; break;
                default: value = english; break;
            }

            return string.IsNullOrEmpty(value) ? english : value;
        }
    }

    [Header("One row per piece of text — fill in translations by hand")]
    [SerializeField] private List<LocalizedEntry> entries = new List<LocalizedEntry>();

    private const string LanguagePrefKey = "SelectedLanguage";

    public Language CurrentLanguage { get; private set; } = Language.English;

    // Fired after CurrentLanguage has already been updated. Every
    // LocalizedText subscribes to this to refresh itself.
    public event Action OnLanguageChanged;

    private Dictionary<string, LocalizedEntry> lookup;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadLanguagePref();
    }

    private void LoadLanguagePref()
    {
        int saved = PlayerPrefs.GetInt(LanguagePrefKey, (int)Language.English);
        CurrentLanguage = Enum.IsDefined(typeof(Language), saved) ? (Language)saved : Language.English;
    }

    public void SetLanguage(Language language)
    {
        if (CurrentLanguage == language) return;

        CurrentLanguage = language;
        PlayerPrefs.SetInt(LanguagePrefKey, (int)language);
        PlayerPrefs.Save();

        OnLanguageChanged?.Invoke();
    }

    // Looks up sourceText (the English string a LocalizedText was showing)
    // against the Entries list. Falls back to sourceText itself (with a
    // console warning) if no matching row was filled in yet.
    public string Translate(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText) || CurrentLanguage == Language.English)
            return sourceText;

        BuildLookupIfNeeded();

        if (lookup.TryGetValue(sourceText, out LocalizedEntry entry))
            return entry.Get(CurrentLanguage);

        Debug.LogWarning($"LocalizationManager: no entry for \"{sourceText}\" — add a row to Entries.");
        return sourceText;
    }

    private void BuildLookupIfNeeded()
    {
        if (lookup != null) return;

        lookup = new Dictionary<string, LocalizedEntry>();
        foreach (LocalizedEntry entry in entries)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.english) && !lookup.ContainsKey(entry.english))
                lookup[entry.english] = entry;
        }
    }
}