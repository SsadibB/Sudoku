using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

#if UNITY_STANDALONE || UNITY_EDITOR
using SFB; // Standalone File Browser — https://github.com/gkngkc/UnityStandaloneFileBrowser
#endif

// Mobile gallery picking requires the NativeGallery plugin (free):
// Package Manager > + > Add package from git URL:
// https://github.com/yasirkula/UnityNativeGallory.git
//
// PC file picking requires Standalone File Browser (free):
// Import the .unitypackage / Assets folder from:
// https://github.com/gkngkc/UnityStandaloneFileBrowser
//
// Single consolidated Profile manager — replaces the four separate
// scripts that used to make up the Profile feature (ProfileLevelManager,
// ProfilePictureManager, ProfileStatsManager, ProfileStatsDisplay). All
// four are now regions in this one file:
//   1. Panel / Avatar   — open/close panel, pick + persist profile picture
//   2. Profile Level/XP — Profile Level & XP bar, separate from Sudoku
//                          level progress
//   3. Game Stats data  — high score per difficulty, puzzles solved,
//                          win streak (persisted)
//   4. Game Stats UI    — pushes stat values into the panel's TMP texts
//
// SETUP: this is the ONE persistent Profile object. Put the whole Profile
// panel hierarchy (icon, panel, avatar images, buttons, stat texts) under
// this same GameObject so it all survives scene loads together — it marks
// itself DontDestroyOnLoad on first Awake. Other scripts (e.g.
// SudokuGameManager) call ProfileManager.Instance.AddXP(...),
// .RecordVictory(...), .RecordLoss() from anywhere/any scene.
public class ProfileManager : MonoBehaviour
{
    public static ProfileManager Instance { get; private set; }

    // ==================== Panel / Avatar ====================

    [Header("Panel")]
    [SerializeField] private GameObject profilePanel;
    [SerializeField] private Button profileIconButton;   // opens the panel
    [SerializeField] private Button closeButton;

    [Header("Display")]
    [SerializeField] private Image profileIconImage;     // small icon shown outside the panel
    [SerializeField] private Image profilePreviewImage;  // larger preview inside the panel
    [SerializeField] private TMP_Text playerNameText;    // player name text in profile panel

    [Header("Actions")]
    [SerializeField] private Button uploadButton;

    [Header("Random Avatars")]
    [SerializeField] private Sprite[] presetAvatars;

    [Header("Upload Settings")]
    [SerializeField] private int maxImageSize = 512; // downscale target, keeps memory/texture size sane

    private const string PREF_MODE = "ProfilePic_Mode";       // "preset" or "custom"
    private const string PREF_PRESET_INDEX = "ProfilePic_PresetIndex";
    private const string CUSTOM_FILE_NAME = "profile_picture.png";

    private string CustomImagePath => Path.Combine(Application.persistentDataPath, CUSTOM_FILE_NAME);

    // ==================== Profile Level / XP ====================

    [Header("XP Curve")]
    [Tooltip("XP required to go from Profile Level 1 to Level 2.")]
    [SerializeField] private int baseXPRequired = 100;
    [Tooltip("Extra XP required per subsequent level, added on top of the base each time (e.g. Lvl2->3 needs baseXPRequired + this, Lvl3->4 needs baseXPRequired + 2x this, etc.)")]
    [SerializeField] private int xpIncreasePerLevel = 50;

    private const string ProfileLevelKey = "ProfileLevel";
    private const string ProfileXPKey = "ProfileXP";

    public int ProfileLevel { get; private set; }
    public int CurrentXP { get; private set; }
    public int XPRequiredForCurrentLevel => GetXPRequiredForLevel(ProfileLevel);

    // Fired whenever XP changes (including as part of a level-up),
    // args: (currentXP, xpRequiredForCurrentLevel, profileLevel).
    public event System.Action<int, int, int> OnXPChanged;

    // Fired specifically when Profile Level increases, args: newLevel.
    // Handy for a one-off "Level Up!" popup/animation.
    public event System.Action<int> OnLevelUp;

    // ==================== Game Stats (data) ====================

    private const string PuzzlesSolvedKey = "TotalPuzzlesSolved";
    private const string CurrentStreakKey = "CurrentWinStreak";
    private const string BestStreakKey = "BestWinStreak";

    private static string HighScoreKey(UIManager.Difficulty difficulty) => $"HighScore_{difficulty}";

    public int TotalPuzzlesSolved { get; private set; }

    // Consecutive victories with no loss in between. Resets to 0 on a loss.
    public int CurrentWinStreak { get; private set; }

    // The highest CurrentWinStreak has ever reached — this is the "streak"
    // number the Profile panel shows.
    public int BestWinStreak { get; private set; }

    // ==================== Game Stats (UI) ====================

    [Header("Game Stats — Highest Score (per difficulty)")]
    [SerializeField] private TMP_Text easyHighScoreText;
    [SerializeField] private TMP_Text mediumHighScoreText;
    [SerializeField] private TMP_Text hardHighScoreText;

    [Header("Game Stats — Highest Level Beaten")]
    [SerializeField] private TMP_Text highestLevelText;

    [Header("Game Stats — Win Streak")]
    [SerializeField] private TMP_Text winStreakText;

    [Header("Game Stats — Puzzles Solved")]
    [SerializeField] private TMP_Text puzzlesSolvedText;

    private const string HighScoreLabel = "Highest Score";
    private const string HighestLevelLabel = "Highest Level Beaten";
    private const string WinStreakLabel = "Win Streak";
    private const string PuzzlesSolvedLabel = "Puzzles Solved";

    // ==================== Lifecycle ====================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A fresh scene reload always creates a brand-new ProfileManager
            // whose serialized fields correctly point at this load's actual
            // panel/buttons/text — the persistent Instance's own fields still
            // point at whatever got destroyed with the previous scene. Hand
            // those fresh references over before this duplicate self-destructs,
            // instead of throwing them away, so the persistent Instance keeps
            // control of whichever Profile UI is actually alive right now.
            Instance.RebindFrom(this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        // -- Load persisted state --
        ProfileLevel = Mathf.Max(1, PlayerPrefs.GetInt(ProfileLevelKey, 1));
        CurrentXP = Mathf.Max(0, PlayerPrefs.GetInt(ProfileXPKey, 0));

        TotalPuzzlesSolved = Mathf.Max(0, PlayerPrefs.GetInt(PuzzlesSolvedKey, 0));
        CurrentWinStreak = Mathf.Max(0, PlayerPrefs.GetInt(CurrentStreakKey, 0));
        BestWinStreak = Mathf.Max(0, PlayerPrefs.GetInt(BestStreakKey, 0));

        // -- Panel wiring --
        AutoDiscoverAndBindUI();
        if (profileIconButton != null) profileIconButton.onClick.AddListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (uploadButton != null) uploadButton.onClick.AddListener(OnUploadClicked);

        if (profilePanel != null) profilePanel.SetActive(false);

        // Belt-and-suspenders: catches any scene-loaded case not already
        // covered by RebindFrom (e.g. additive loads that don't spawn a
        // duplicate ProfileManager). RebindFrom is the one that matters for
        // the normal reload-back-to-MainMenu flow.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        AutoDiscoverAndBindUI();
        LoadSavedProfilePicture();
        RefreshPlayerName();
    }

    public void SetPlayerName(string newName)
    {
        if (string.IsNullOrEmpty(newName)) return;
        PlayerPrefs.SetString("PlayerProfileName", newName);
        PlayerPrefs.Save();
        RefreshPlayerName();
    }

    public void RefreshPlayerName()
    {
        if (playerNameText == null)
            FindPlayerNameText();

        string savedName = PlayerPrefs.GetString("PlayerProfileName", "Luffy");
        if (playerNameText != null && !string.IsNullOrEmpty(savedName))
        {
            playerNameText.text = savedName;
        }
    }

    private void FindPlayerNameText()
    {
        var allTMP = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        foreach (var t in allTMP)
        {
            if (t.transform.parent != null && t.transform.parent.gameObject.name.ToLowerInvariant().Contains("profilename"))
            {
                playerNameText = t;
                return;
            }
        }
    }

    public void ApplyCustomAvatarTexture(Texture2D texture)
    {
        if (texture == null) return;
        Texture2D resized = LoadAndResizeTexture(texture.EncodeToPNG(), maxImageSize) ?? texture;
        ApplySprite(SpriteFromTexture(resized));
        SaveCustomImage(resized);
    }

    private void OnEnable()
    {
        if (SadibTools.AuthLogin.AuthManager.Instance != null)
        {
            SadibTools.AuthLogin.AuthManager.Instance.OnLoginSuccess += HandleAuthLoginSuccess;
            SadibTools.AuthLogin.AuthManager.Instance.OnSignedOut += HandleAuthSignedOut;
        }
    }

    private void OnDisable()
    {
        if (SadibTools.AuthLogin.AuthManager.Instance != null)
        {
            SadibTools.AuthLogin.AuthManager.Instance.OnLoginSuccess -= HandleAuthLoginSuccess;
            SadibTools.AuthLogin.AuthManager.Instance.OnSignedOut -= HandleAuthSignedOut;
        }
    }

    private void HandleAuthLoginSuccess(SadibTools.AuthLogin.AuthSession session)
    {
        if (session == null) return;

        if (!string.IsNullOrEmpty(session.DisplayName))
        {
            SetPlayerName(session.DisplayName);
        }

        if (!string.IsNullOrEmpty(session.AvatarUrl))
        {
            StartCoroutine(DownloadAndApplyAvatar(session.AvatarUrl));
        }
    }

    private void HandleAuthSignedOut(string providerId)
    {
        if (SadibTools.AuthLogin.AuthManager.Instance == null || !SadibTools.AuthLogin.AuthManager.Instance.IsSignedIn)
        {
            RestoreDefaultProfile();
        }
    }

    private IEnumerator DownloadAndApplyAvatar(string url)
    {
        if (string.IsNullOrEmpty(url)) yield break;

        Debug.Log("[ProfileManager] Downloading avatar from: " + url);

        using (var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);
                if (texture != null)
                {
                    Debug.Log($"[ProfileManager] Avatar downloaded successfully ({texture.width}x{texture.height}). Applying...");
                    ApplyCustomAvatarTexture(texture);
                }
            }
            else
            {
                Debug.LogWarning("[ProfileManager] Failed to download avatar from: " + url + " - " + request.error);
            }
        }
    }

    public void RestoreDefaultProfile()
    {
        PlayerPrefs.DeleteKey("PlayerProfileName");
        RefreshPlayerName();
        LoadSavedProfilePicture();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ForceClosePanel();
    }

    // Called on the persistent Instance by a freshly-loaded duplicate
    // ProfileManager, right before that duplicate destroys itself. `fresh`
    // is the duplicate — its serialized fields are the correct, currently-
    // alive references for this scene load (ours are stale/destroyed).
    private void RebindFrom(ProfileManager fresh)
    {
        // Old listeners are on now-destroyed buttons anyway, but detach
        // cleanly in case anything here is still valid.
        if (profileIconButton != null) profileIconButton.onClick.RemoveListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.RemoveListener(ClosePanel);
        if (uploadButton != null) uploadButton.onClick.RemoveListener(OnUploadClicked);

        profilePanel = fresh.profilePanel;
        profileIconButton = fresh.profileIconButton;
        closeButton = fresh.closeButton;
        profileIconImage = fresh.profileIconImage;
        profilePreviewImage = fresh.profilePreviewImage;
        playerNameText = fresh.playerNameText;
        uploadButton = fresh.uploadButton;

        easyHighScoreText = fresh.easyHighScoreText;
        mediumHighScoreText = fresh.mediumHighScoreText;
        hardHighScoreText = fresh.hardHighScoreText;
        highestLevelText = fresh.highestLevelText;
        winStreakText = fresh.winStreakText;
        puzzlesSolvedText = fresh.puzzlesSolvedText;

        if (profileIconButton != null) profileIconButton.onClick.AddListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (uploadButton != null) uploadButton.onClick.AddListener(OnUploadClicked);

        // Push the avatar that's already loaded onto the fresh Image refs,
        // and the current stats onto the fresh Text refs — both existed
        // before this reload and otherwise wouldn't show until they changed.
        if (currentAvatarSprite != null) ApplySprite(currentAvatarSprite);
        RefreshStatsDisplay();
        RefreshPlayerName();

        ForceClosePanel();
        StartCoroutine(SuppressIconClickBriefly());
    }

    private IEnumerator SuppressIconClickBriefly()
    {
        if (profileIconButton == null) yield break;

        profileIconButton.interactable = false;
        // Real-time, not scaled — covers the stray click even if something
        // has Time.timeScale paused right after the load.
        yield return new WaitForSecondsRealtime(0.2f);
        profileIconButton.interactable = true;
    }

    // ==================== Panel open/close ====================

    public void AutoDiscoverAndBindUI()
    {
        // 1. Discover ProfilePanel if null
        if (profilePanel == null)
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            foreach (var t in allTransforms)
            {
                if (t.gameObject.name.Equals("ProfilePanel", StringComparison.OrdinalIgnoreCase))
                {
                    profilePanel = t.gameObject;
                    break;
                }
            }
        }

        // 2. Discover Profile Button (whether on Profile, Avatar, etc.)
        var allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (var b in allButtons)
        {
            string n = b.gameObject.name.ToLowerInvariant();
            if (n == "profile" || n == "profileicon" || n == "avatar" || n.Contains("profilebutton"))
            {
                // Ensure this is not inside the panel (e.g. close/upload)
                if (profilePanel == null || !b.transform.IsChildOf(profilePanel.transform))
                {
                    profileIconButton = b;
                    EnsureButtonRaycastable(b);
                    b.onClick.RemoveListener(OpenPanel);
                    b.onClick.AddListener(OpenPanel);
                }
            }
        }

        // 3. Discover Close Button
        if (closeButton == null && profilePanel != null)
        {
            var panelButtons = profilePanel.GetComponentsInChildren<Button>(includeInactive: true);
            foreach (var b in panelButtons)
            {
                if (b.gameObject.name.ToLowerInvariant().Contains("close"))
                {
                    closeButton = b;
                    EnsureButtonRaycastable(b);
                    b.onClick.RemoveListener(ClosePanel);
                    b.onClick.AddListener(ClosePanel);
                    break;
                }
            }
        }

        // 4. Discover Images
        EnsureAvatarImagesFound();
    }

    private void EnsureButtonRaycastable(Button btn)
    {
        if (btn == null) return;
        var graphic = btn.targetGraphic;
        if (graphic != null)
        {
            graphic.raycastTarget = true;
        }
        else
        {
            var img = btn.GetComponent<Image>() ?? btn.GetComponentInChildren<Image>(includeInactive: true);
            if (img != null)
            {
                btn.targetGraphic = img;
                img.raycastTarget = true;
            }
        }
    }

    public void OpenPanel()
    {
        Debug.Log($"[ProfileManager] OpenPanel() called. Frame: {Time.frameCount}");
        if (profilePanel == null)
            AutoDiscoverAndBindUI();

        if (profilePanel != null)
        {
            profilePanel.SetActive(true);
            RefreshStatsDisplay();
            RefreshPlayerName();
            if (currentAvatarSprite != null)
                ApplySprite(currentAvatarSprite);
        }
        else
        {
            Debug.LogWarning("[ProfileManager] OpenPanel() failed — profilePanel could not be found!");
        }
    }

    public void ClosePanel()
    {
        if (profilePanel != null)
        {
            profilePanel.SetActive(false);
            return;
        }

        // If this fires after the very first scene load, the Profile Panel
        // hierarchy isn't parented under this GameObject in the scene, so
        // it didn't survive the last scene reload with ProfileManager (see
        // the SETUP note at the top of this file) — this reference is now
        // pointing at a destroyed object and can't be recovered at runtime.
        Debug.LogWarning("[ProfileManager] ClosePanel() couldn't run — profilePanel " +
            "reference is null, likely because the Profile Panel hierarchy isn't " +
            "parented under the ProfileManager GameObject and didn't survive a scene reload.");
    }

    // Public wrapper so other scripts (e.g. UIManager, on returning to
    // MainMenu) can force the panel closed regardless of whatever state it
    // was left in — this object is DontDestroyOnLoad, so its open/closed
    // state otherwise survives scene reloads untouched.
    public void ForceClosePanel() => ClosePanel();

    // ==================== Gallery upload ====================

    private void OnUploadClicked()
    {
        // Checked in this order on purpose: in the Editor, UNITY_EDITOR is
        // defined alongside whatever platform (e.g. UNITY_ANDROID) is
        // currently active in Build Settings — so without this ordering,
        // testing in Play Mode with an Android build target selected would
        // try to call NativeGallery, which doesn't work in-Editor. Routing
        // UNITY_EDITOR through the standalone picker lets you test the whole
        // upload flow without a device.
#if UNITY_EDITOR || UNITY_STANDALONE
        OnUploadClicked_Standalone();
#elif UNITY_ANDROID || UNITY_IOS
        OnUploadClicked_Mobile();
#else
        Debug.LogWarning("ProfileManager: gallery/file picking isn't set up for this platform.");
#endif
    }

#if UNITY_EDITOR || UNITY_STANDALONE
    private void OnUploadClicked_Standalone()
    {
        var extensions = new[] { new ExtensionFilter("Image Files", "png", "jpg", "jpeg") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select a profile picture", "", extensions, false);

        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
            return; // user cancelled

        StartCoroutine(LoadPickedImageStandalone(paths[0]));
    }

    private IEnumerator LoadPickedImageStandalone(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Texture2D texture = LoadAndResizeTexture(bytes, maxImageSize);

        if (texture == null)
        {
            Debug.LogWarning("ProfileManager: failed to load image from " + path);
            yield break;
        }

        ApplySprite(SpriteFromTexture(texture));
        SaveCustomImage(texture);

        yield return null;
    }
#endif

#if UNITY_ANDROID || UNITY_IOS
    private void OnUploadClicked_Mobile()
    {
        if (NativeGallery.IsMediaPickerBusy()) return;

        // Note: GetImageFromGallery returns void in this version of NativeGallery
        // (older versions returned a Permission enum synchronously). Permission
        // status, if you need to react to a denial, is available via
        // NativeGallery.CheckPermission() separately if your installed version
        // exposes it — check NativeGallery.cs for the exact API you have.
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path))
                return; // user cancelled

            StartCoroutine(LoadPickedImageMobile(path));
        }, "Select a profile picture", "image/*");
    }

    private IEnumerator LoadPickedImageMobile(string path)
    {
        // NativeGallery can downscale while loading, so no separate resize step needed here.
        Texture2D texture = NativeGallery.LoadImageAtPath(path, maxImageSize, false);

        if (texture == null)
        {
            Debug.LogWarning("ProfileManager: failed to load image from " + path);
            yield break;
        }

        ApplySprite(SpriteFromTexture(texture));
        SaveCustomImage(texture);

        yield return null;
    }
#endif

    // Loads raw image bytes and, if larger than maxSize on its longest side,
    // downsamples (GPU blit) to fit — used by the standalone/PC path, since
    // File dialogs don't give you a resize-on-load option like NativeGallery does.
    private Texture2D LoadAndResizeTexture(byte[] bytes, int maxSize)
    {
        Texture2D raw = new Texture2D(2, 2);
        if (!raw.LoadImage(bytes))
        {
            Destroy(raw);
            return null;
        }

        int width = raw.width;
        int height = raw.height;

        if (Mathf.Max(width, height) <= maxSize)
            return raw;

        float scale = (float)maxSize / Mathf.Max(width, height);
        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));

        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        RenderTexture previousActive = RenderTexture.active;

        Graphics.Blit(raw, rt);
        RenderTexture.active = rt;

        Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        resized.Apply();

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(raw);

        return resized;
    }

    // ==================== Random preset avatar (auto-assigned on first run only) ====================

    private void ApplyPresetAvatar(int index)
    {
        if (presetAvatars == null || index < 0 || index >= presetAvatars.Length) return;

        ApplySprite(presetAvatars[index]);

        PlayerPrefs.SetString(PREF_MODE, "preset");
        PlayerPrefs.SetInt(PREF_PRESET_INDEX, index);
        PlayerPrefs.Save();

        // If a previously saved custom image exists on disk it's fine to leave it;
        // it'll just be unused until upload is picked again. Delete it here instead
        // if you'd rather free the space immediately:
        // if (File.Exists(CustomImagePath)) File.Delete(CustomImagePath);
    }

    // ==================== Shared apply / persistence (avatar) ====================

    // Cached so RebindFrom can re-apply the already-chosen avatar to fresh
    // Image references after a scene reload, without re-reading disk/prefs.
    private Sprite currentAvatarSprite;

    private void EnsureAvatarImagesFound()
    {
        if (profileIconImage == null || (profileIconImage == profilePreviewImage && profileIconButton != null))
        {
            if (profileIconButton != null)
            {
                var img = profileIconButton.GetComponent<Image>() ?? profileIconButton.GetComponentInChildren<Image>(includeInactive: true);
                if (img != null) profileIconImage = img;
            }
        }

        if (profilePreviewImage == null || profilePreviewImage == profileIconImage)
        {
            var allImages = FindObjectsByType<Image>(FindObjectsInactive.Include);
            foreach (var img in allImages)
            {
                if (img.gameObject.name.ToLowerInvariant().Contains("profileimage"))
                {
                    profilePreviewImage = img;
                    break;
                }
            }
        }
    }

    public void EnsureAvatarMasking()
    {
        EnsureAvatarImagesFound();

        // 1. Profile Panel Preview Image Masking
        if (profilePreviewImage != null)
        {
            Transform avatarParent = profilePreviewImage.transform.parent;
            if (avatarParent != null)
            {
                Transform bgTransform = avatarParent.Find("BG");
                if (bgTransform != null)
                {
                    var bgImg = bgTransform.GetComponent<Image>();
                    if (bgImg != null)
                    {
                        var mask = bgTransform.GetComponent<Mask>();
                        if (mask == null)
                        {
                            mask = bgTransform.gameObject.AddComponent<Mask>();
                            mask.showMaskGraphic = true;
                        }

                        if (profilePreviewImage.transform.parent != bgTransform)
                        {
                            profilePreviewImage.transform.SetParent(bgTransform, worldPositionStays: true);
                        }
                    }
                }
                else
                {
                    var mask = avatarParent.GetComponent<Mask>();
                    if (mask == null)
                    {
                        var parentImg = avatarParent.GetComponent<Image>();
                        if (parentImg != null)
                        {
                            mask = avatarParent.gameObject.AddComponent<Mask>();
                            mask.showMaskGraphic = true;
                        }
                        else
                        {
                            var rectMask = avatarParent.GetComponent<RectMask2D>();
                            if (rectMask == null)
                                avatarParent.gameObject.AddComponent<RectMask2D>();
                        }
                    }
                }
            }
        }

        // 2. Top-Left Profile Icon Masking
        if (profileIconImage != null)
        {
            Transform iconParent = profileIconImage.transform.parent;
            if (iconParent != null && iconParent != profileIconImage.transform)
            {
                Transform bgTransform = iconParent.Find("BG");
                if (bgTransform != null)
                {
                    var bgImg = bgTransform.GetComponent<Image>();
                    if (bgImg != null)
                    {
                        var mask = bgTransform.GetComponent<Mask>();
                        if (mask == null)
                        {
                            mask = bgTransform.gameObject.AddComponent<Mask>();
                            mask.showMaskGraphic = true;
                        }

                        if (profileIconImage.transform.parent != bgTransform)
                        {
                            profileIconImage.transform.SetParent(bgTransform, worldPositionStays: true);
                        }
                    }
                }
                else
                {
                    var mask = iconParent.GetComponent<Mask>();
                    if (mask == null)
                    {
                        var parentImg = iconParent.GetComponent<Image>();
                        if (parentImg != null)
                        {
                            mask = iconParent.gameObject.AddComponent<Mask>();
                            mask.showMaskGraphic = true;
                        }
                        else
                        {
                            var rectMask = iconParent.GetComponent<RectMask2D>();
                            if (rectMask == null)
                                iconParent.gameObject.AddComponent<RectMask2D>();
                        }
                    }
                }
            }
        }
    }

    private void ApplySprite(Sprite sprite)
    {
        currentAvatarSprite = sprite;
        EnsureAvatarImagesFound();
        EnsureAvatarMasking();
        if (profileIconImage != null) profileIconImage.sprite = sprite;
        if (profilePreviewImage != null) profilePreviewImage.sprite = sprite;
    }

    private void SaveCustomImage(Texture2D texture)
    {
        try
        {
            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(CustomImagePath, pngBytes);

            PlayerPrefs.SetString(PREF_MODE, "custom");
            PlayerPrefs.Save();
        }
        catch (IOException e)
        {
            Debug.LogWarning("ProfileManager: failed to save custom image — " + e.Message);
        }
    }

    private void LoadSavedProfilePicture()
    {
        string mode = PlayerPrefs.GetString(PREF_MODE, "");

        if (mode == "custom" && File.Exists(CustomImagePath))
        {
            byte[] bytes = File.ReadAllBytes(CustomImagePath);
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(bytes))
            {
                ApplySprite(SpriteFromTexture(texture));
                return;
            }
        }
        else if (mode == "preset")
        {
            int index = PlayerPrefs.GetInt(PREF_PRESET_INDEX, 0);
            if (presetAvatars != null && index >= 0 && index < presetAvatars.Length)
            {
                ApplySprite(presetAvatars[index]);
                return;
            }
        }

        // No saved choice yet (first run) — pick a random preset so the icon
        // isn't blank, and persist that choice.
        if (presetAvatars != null && presetAvatars.Length > 0)
        {
            ApplyPresetAvatar(Random.Range(0, presetAvatars.Length));
        }
    }

    private static Sprite SpriteFromTexture(Texture2D texture)
    {
        return Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f)
        );
    }

    // ==================== Profile Level / XP ====================

    private int GetXPRequiredForLevel(int level)
    {
        return baseXPRequired + Mathf.Max(0, level - 1) * xpIncreasePerLevel;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        CurrentXP += amount;

        int required = GetXPRequiredForLevel(ProfileLevel);
        while (CurrentXP >= required)
        {
            CurrentXP -= required;
            ProfileLevel++;
            OnLevelUp?.Invoke(ProfileLevel);
            required = GetXPRequiredForLevel(ProfileLevel);
        }

        SaveProfileLevel();
        OnXPChanged?.Invoke(CurrentXP, required, ProfileLevel);
    }

    private void SaveProfileLevel()
    {
        PlayerPrefs.SetInt(ProfileLevelKey, ProfileLevel);
        PlayerPrefs.SetInt(ProfileXPKey, CurrentXP);
        PlayerPrefs.Save();
    }

    // ==================== Game Stats (data) ====================

    // Highest sessionScore ever recorded for a completed puzzle on this
    // difficulty. 0 if none completed yet.
    public int GetHighScore(UIManager.Difficulty difficulty)
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(HighScoreKey(difficulty), 0));
    }

    // Call once per board completion, from
    // SudokuGameManager.CheckWinCondition() — after scorePerBoardComplete
    // has already been added to sessionScore, so `score` is the final
    // score for that puzzle.
    public void RecordVictory(UIManager.Difficulty difficulty, int score)
    {
        TotalPuzzlesSolved++;
        PlayerPrefs.SetInt(PuzzlesSolvedKey, TotalPuzzlesSolved);

        if (score > GetHighScore(difficulty))
        {
            PlayerPrefs.SetInt(HighScoreKey(difficulty), score);
        }

        CurrentWinStreak++;
        PlayerPrefs.SetInt(CurrentStreakKey, CurrentWinStreak);

        if (CurrentWinStreak > BestWinStreak)
        {
            BestWinStreak = CurrentWinStreak;
            PlayerPrefs.SetInt(BestStreakKey, BestWinStreak);
        }

        PlayerPrefs.Save();
        RefreshStatsDisplay();
    }

    // Call on a loss (out of hearts / game over), from
    // SudokuGameManager.HandleGameOver(). Only breaks the current streak —
    // puzzles-solved and high scores are untouched by a loss.
    public void RecordLoss()
    {
        if (CurrentWinStreak == 0) return; // nothing to reset, skip the write

        CurrentWinStreak = 0;
        PlayerPrefs.SetInt(CurrentStreakKey, 0);
        PlayerPrefs.Save();
        RefreshStatsDisplay();
    }

    // ==================== Cloud sync (PlayFab) ====================

    // Called by SudokuPlayFabManager after login, with whatever values
    // came back from the cloud. Every field is merged by taking the max
    // against the current local value — same reasoning as
    // CoinManager.ApplyCloudValue / LevelManager.ApplyCloudProgress, so a
    // device that's behind can never roll back progress.
    //
    // Profile Level/XP is a pair (XP only means anything relative to its
    // own level), so it's merged as a pair: whichever side has the higher
    // ProfileLevel wins outright; if levels are tied, the higher XP wins.
    //
    // easyHigh/mediumHigh/hardHigh let the caller pass all three
    // difficulties' high scores in one call.
    public void ApplyCloudStats(int cloudProfileLevel, int cloudXP, int cloudPuzzlesSolved,
        int cloudBestWinStreak, int easyHigh, int mediumHigh, int hardHigh)
    {
        bool changed = false;

        if (cloudProfileLevel > ProfileLevel ||
            (cloudProfileLevel == ProfileLevel && cloudXP > CurrentXP))
        {
            ProfileLevel = Mathf.Max(1, cloudProfileLevel);
            CurrentXP = Mathf.Max(0, cloudXP);
            changed = true;
        }

        if (cloudPuzzlesSolved > TotalPuzzlesSolved)
        {
            TotalPuzzlesSolved = cloudPuzzlesSolved;
            PlayerPrefs.SetInt(PuzzlesSolvedKey, TotalPuzzlesSolved);
            changed = true;
        }

        if (cloudBestWinStreak > BestWinStreak)
        {
            BestWinStreak = cloudBestWinStreak;
            PlayerPrefs.SetInt(BestStreakKey, BestWinStreak);
            changed = true;
        }

        ApplyCloudHighScore(UIManager.Difficulty.Easy, easyHigh);
        ApplyCloudHighScore(UIManager.Difficulty.Medium, mediumHigh);
        ApplyCloudHighScore(UIManager.Difficulty.Hard, hardHigh);

        if (changed)
        {
            SaveProfileLevel();
            PlayerPrefs.Save();
            OnXPChanged?.Invoke(CurrentXP, GetXPRequiredForLevel(ProfileLevel), ProfileLevel);
            RefreshStatsDisplay();
        }
    }

    private void ApplyCloudHighScore(UIManager.Difficulty difficulty, int cloudScore)
    {
        if (cloudScore > GetHighScore(difficulty))
        {
            PlayerPrefs.SetInt(HighScoreKey(difficulty), cloudScore);
            PlayerPrefs.Save();
        }
    }

    // ==================== Game Stats (UI) ====================

    // Pushes the current stat values into the panel's TMP texts. Called
    // whenever the panel opens, and immediately after any stat changes
    // (RecordVictory/RecordLoss) so the numbers stay correct even if the
    // panel happens to already be open when a puzzle finishes.
    private void RefreshStatsDisplay()
    {
        SetHighScoreText(easyHighScoreText, UIManager.Difficulty.Easy);
        SetHighScoreText(mediumHighScoreText, UIManager.Difficulty.Medium);
        SetHighScoreText(hardHighScoreText, UIManager.Difficulty.Hard);

        if (highestLevelText != null)
        {
            int highestLevel = LevelManager.Instance != null
                ? LevelManager.Instance.GetHighestLevelBeaten()
                : 0;
            highestLevelText.text = $"{Translate(HighestLevelLabel)}: {highestLevel}";
        }

        if (winStreakText != null)
        {
            winStreakText.text = $"{Translate(WinStreakLabel)}: {BestWinStreak}";
        }

        if (puzzlesSolvedText != null)
        {
            puzzlesSolvedText.text = $"{Translate(PuzzlesSolvedLabel)}: {TotalPuzzlesSolved}";
        }
    }

    private void SetHighScoreText(TMP_Text label, UIManager.Difficulty difficulty)
    {
        if (label == null) return;
        label.text = $"{Translate(HighScoreLabel)} ({difficulty}): {GetHighScore(difficulty)}";
    }

    private static string Translate(string source)
    {
        return LocalizationManager.Instance != null
            ? LocalizationManager.Instance.Translate(source)
            : source;
    }
}