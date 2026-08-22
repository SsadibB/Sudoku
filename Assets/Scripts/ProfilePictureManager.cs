using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_STANDALONE || UNITY_EDITOR
using SFB; // Standalone File Browser — https://github.com/gkngkc/UnityStandaloneFileBrowser
#endif

// Mobile gallery picking requires the NativeGallery plugin (free):
// Package Manager > + > Add package from git URL:
// https://github.com/yasirkula/UnityNativeGallery.git
//
// PC file picking requires Standalone File Browser (free):
// Import the .unitypackage / Assets folder from:
// https://github.com/gkngkc/UnityStandaloneFileBrowser
//
// Handles:
//  - Opening/closing the profile panel
//  - Picking an image from the device gallery and cropping it into a sprite
//  - Picking a random avatar from a preset list
//  - Persisting the choice locally (custom image saved to disk, or the
//    index of the chosen preset avatar) so it survives app restarts
public class ProfilePictureManager : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject profilePanel;
    [SerializeField] private Button profileIconButton;   // opens the panel
    [SerializeField] private Button closeButton;

    [Header("Display")]
    [SerializeField] private Image profileIconImage;     // small icon shown outside the panel
    [SerializeField] private Image profilePreviewImage;  // larger preview inside the panel

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

    private void Awake()
    {
        if (profileIconButton != null) profileIconButton.onClick.AddListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (uploadButton != null) uploadButton.onClick.AddListener(OnUploadClicked);

        if (profilePanel != null) profilePanel.SetActive(false);
    }

    private void Start()
    {
        LoadSavedProfilePicture();
    }

    // ---------- Panel open/close ----------

    private void OpenPanel()
    {
        if (profilePanel != null) profilePanel.SetActive(true);
    }

    private void ClosePanel()
    {
        if (profilePanel != null) profilePanel.SetActive(false);
    }

    // ---------- Gallery upload ----------

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
        Debug.LogWarning("ProfilePictureManager: gallery/file picking isn't set up for this platform.");
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
            Debug.LogWarning("ProfilePictureManager: failed to load image from " + path);
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
            Debug.LogWarning("ProfilePictureManager: failed to load image from " + path);
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

    // ---------- Random preset avatar (auto-assigned on first run only) ----------

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

    // ---------- Shared apply / persistence ----------

    private void ApplySprite(Sprite sprite)
    {
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
            Debug.LogWarning("ProfilePictureManager: failed to save custom image — " + e.Message);
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
}