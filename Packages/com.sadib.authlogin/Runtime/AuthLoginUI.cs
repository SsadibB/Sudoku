using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Drop-in UI component to connect Google, Facebook, and Instagram Login/Logout buttons
    /// and display real-time connection status, success messages, and account details.
    /// Automatically discovers and wires UI elements in the scene hierarchy.
    /// Dynamically switches button text between "Log in" and "Log out" and highlights active logos.
    /// </summary>
    [AddComponentMenu("Auth Login/Auth Login UI")]
    public class AuthLoginUI : MonoBehaviour
    {
        public static AuthLoginUI Instance { get; private set; }

        [System.Serializable]
        public class ProviderSlot
        {
            public string providerId;
            public GameObject button;
            public GameObject logoutButton;
            public Image logoImage;
            public TMP_Text labelTMP;
            public Text labelLegacy;
            public List<Button> allButtons = new List<Button>();
        }

        [Header("Providers UI")]
        [SerializeField] private ProviderSlot google = new ProviderSlot { providerId = "google" };
        [SerializeField] private ProviderSlot facebook = new ProviderSlot { providerId = "facebook" };
        [SerializeField] private ProviderSlot instagram = new ProviderSlot { providerId = "instagram" };

        [Header("Status Display")]
        [Tooltip("TextMeshPro text for connection status messages (e.g. 'Connect_Text').")]
        [SerializeField] private TMP_Text statusTMPText;
        [Tooltip("Standard UI Text for connection status messages if not using TextMeshPro.")]
        [SerializeField] private Text statusLegacyText;
        [Tooltip("GameObject of status text (will auto-detect TMP_Text or Text component).")]
        [SerializeField] private GameObject statusTextObject;

        [Header("Account Info Display (Optional)")]
        [Tooltip("Displays current PlayFab ID / User Display Name / Email when connected.")]
        [SerializeField] private TMP_Text accountInfoText;
        [Tooltip("Spinner / loading game object shown while authentication is in progress.")]
        [SerializeField] private GameObject loadingIndicator;

        [Header("Visual Feedback")]
        [Tooltip("Color of logo when connected.")]
        [SerializeField] private Color logoConnectedColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("Color of logo when disconnected.")]
        [SerializeField] private Color logoDisconnectedColor = new Color(0.6f, 0.6f, 0.6f, 0.75f);
        [Tooltip("Text to display when not logged in.")]
        [SerializeField] private string loginText = "Log in";
        [Tooltip("Text to display when logged in.")]
        [SerializeField] private string logoutText = "Log out";

        [Header("Auto Discovery")]
        [Tooltip("Automatically find buttons, labels, and 'Connect_Text' if unassigned.")]
        [SerializeField] private bool autoDiscoverElements = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            EnsureInstance();
        }

        public static AuthLoginUI EnsureInstance()
        {
            if (Instance != null) return Instance;

            var existing = FindAnyObjectByType<AuthLoginUI>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var authMgr = AuthManager.EnsureInstance();
            if (authMgr != null)
            {
                Instance = authMgr.gameObject.AddComponent<AuthLoginUI>();
                return Instance;
            }

            var go = new GameObject("AuthLoginUI");
            Instance = go.AddComponent<AuthLoginUI>();
            DontDestroyOnLoad(go);
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            google.providerId = GoogleAuthProvider.Id;
            facebook.providerId = FacebookAuthProvider.FacebookId;
            instagram.providerId = FacebookAuthProvider.InstagramId;

            SceneManager.sceneLoaded += OnSceneLoaded;

            BindAndRefresh();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindAndRefresh();
        }

        private void OnEnable()
        {
            var auth = AuthManager.EnsureInstance();
            if (auth != null)
            {
                auth.OnLoginStarted += HandleLoginStarted;
                auth.OnLoginSuccess += HandleLoginSuccess;
                auth.OnLoginFailure += HandleLoginFailure;
                auth.OnSignedOut += HandleSignedOut;
            }

            BindAndRefresh();
        }

        private void OnDisable()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginStarted -= HandleLoginStarted;
                AuthManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
                AuthManager.Instance.OnLoginFailure -= HandleLoginFailure;
                AuthManager.Instance.OnSignedOut -= HandleSignedOut;
            }
        }

        private void Start()
        {
            BindAndRefresh();
        }

        public void BindAndRefresh()
        {
            if (autoDiscoverElements)
            {
                AutoDiscoverStatusText();
                AutoDiscoverProviderSlots();
            }

            BindSlot(google, OnClickGoogleSlot);
            BindSlot(facebook, OnClickFacebookSlot);
            BindSlot(instagram, OnClickInstagramSlot);

            RefreshUI();
        }

        // ==================== Slot Actions (Toggles Login/Logout) ====================

        public void OnClickGoogleSlot()
        {
            var auth = AuthManager.EnsureInstance();
            if (auth.IsGoogleSignedIn)
            {
                OnClickSignOutGoogle();
            }
            else
            {
                OnClickSignInGoogle();
            }
        }

        public void OnClickFacebookSlot()
        {
            var auth = AuthManager.EnsureInstance();
            if (auth.IsFacebookSignedIn)
            {
                OnClickSignOutFacebook();
            }
            else
            {
                OnClickSignInFacebook();
            }
        }

        public void OnClickInstagramSlot()
        {
            var auth = AuthManager.EnsureInstance();
            if (auth.IsInstagramSignedIn)
            {
                OnClickSignOutInstagram();
            }
            else
            {
                OnClickSignInInstagram();
            }
        }

        public void OnClickSignInGoogle()
        {
            SetStatus("Connecting with Google...");
            AuthManager.EnsureInstance().SignInWithGoogle();
        }

        public void OnClickSignOutGoogle()
        {
            SetStatus("Signing out from Google...");
            AuthManager.EnsureInstance().SignOutGoogle();
        }

        public void OnClickSignInFacebook()
        {
            SetStatus("Connecting with Facebook...");
            AuthManager.EnsureInstance().SignInWithFacebook();
        }

        public void OnClickSignOutFacebook()
        {
            SetStatus("Signing out from Facebook...");
            AuthManager.EnsureInstance().SignOutFacebook();
        }

        public void OnClickSignInInstagram()
        {
            SetStatus("Connecting with Instagram...");
            AuthManager.EnsureInstance().SignInWithInstagram();
        }

        public void OnClickSignOutInstagram()
        {
            SetStatus("Signing out from Instagram...");
            AuthManager.EnsureInstance().SignOutInstagram();
        }

        public void OnClickSignOutAll()
        {
            SetStatus("Signing out...");
            AuthManager.EnsureInstance().SignOut();
        }

        // ==================== Event Handlers ====================

        private void HandleLoginStarted(string providerId)
        {
            SetLoading(true);
            SetStatus($"Connecting with {GetProviderDisplayName(providerId)}...");
        }

        private void HandleLoginSuccess(AuthSession session)
        {
            SetLoading(false);
            
            // Connection successful message displayed in status text
            SetStatus("<color=#4CAF50>Connection Successful!</color>");

            // Update player name with Google/Facebook display name
            if (!string.IsNullOrEmpty(session.DisplayName))
            {
                UpdatePlayerName(session.DisplayName);
            }

            if (accountInfoText != null)
            {
                string userDisplay = !string.IsNullOrEmpty(session.DisplayName)
                    ? session.DisplayName
                    : (!string.IsNullOrEmpty(session.Email) ? session.Email : session.PlayFabId);

                accountInfoText.text = $"Logged in as: <b>{userDisplay}</b>\nPlayFab ID: <color=#888888>{session.PlayFabId}</color>";
                accountInfoText.gameObject.SetActive(true);
            }

            RefreshUI();
        }

        private void HandleLoginFailure(AuthError error)
        {
            SetLoading(false);
            string providerName = GetProviderDisplayName(error.ProviderId);

            if (error.Code == AuthErrorCode.Cancelled)
            {
                SetStatus($"{providerName} sign-in cancelled.");
            }
            else
            {
                SetStatus($"<color=#F44336>{providerName} Login Failed: {error.Message}</color>");
            }

            RefreshUI();
        }

        private void HandleSignedOut(string providerId)
        {
            SetLoading(false);
            string providerName = providerId == "all" ? "All accounts" : GetProviderDisplayName(providerId);
            SetStatus($"Signed out from {providerName}.");

            if (accountInfoText != null && (AuthManager.Instance == null || !AuthManager.Instance.IsSignedIn))
            {
                accountInfoText.text = string.Empty;
                accountInfoText.gameObject.SetActive(false);
            }

            RefreshUI();
        }

        private void UpdatePlayerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var allTMP = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            foreach (var t in allTMP)
            {
                if (t.transform.parent != null && t.transform.parent.gameObject.name.ToLowerInvariant().Contains("profilename"))
                {
                    t.text = name;
                }
            }
        }

        // ==================== UI State Refresh ====================

        public void RefreshUI()
        {
            var auth = AuthManager.Instance;
            bool isGoogle = auth != null && auth.IsGoogleSignedIn;
            bool isFacebook = auth != null && auth.IsFacebookSignedIn;
            bool isInstagram = auth != null && auth.IsInstagramSignedIn;
            bool isBusy = auth != null && auth.IsBusy;

            SetLoading(isBusy);

            UpdateSlotState(google, isGoogle);
            UpdateSlotState(facebook, isFacebook);
            UpdateSlotState(instagram, isInstagram);

            if (accountInfoText != null && auth != null && auth.IsSignedIn && auth.CurrentSession != null)
            {
                var session = auth.CurrentSession;
                string userDisplay = !string.IsNullOrEmpty(session.DisplayName)
                    ? session.DisplayName
                    : (!string.IsNullOrEmpty(session.Email) ? session.Email : session.PlayFabId);

                accountInfoText.text = $"Logged in as: <b>{userDisplay}</b>\nPlayFab ID: <color=#888888>{session.PlayFabId}</color>";
                accountInfoText.gameObject.SetActive(true);
            }
        }

        private void UpdateSlotState(ProviderSlot slot, bool isConnected)
        {
            if (slot == null) return;

            // 1. Logo active brightness / color
            if (slot.logoImage != null)
            {
                slot.logoImage.color = isConnected ? logoConnectedColor : logoDisconnectedColor;
            }

            // 2. Label text ("Log in" vs "Log out")
            string displayText = isConnected ? logoutText : loginText;

            if (slot.labelTMP != null)
            {
                slot.labelTMP.text = displayText;
            }

            if (slot.labelLegacy != null)
            {
                slot.labelLegacy.text = displayText;
            }

            // Also update any labels under buttons in slot
            if (slot.allButtons != null)
            {
                foreach (var b in slot.allButtons)
                {
                    if (b == null) continue;
                    var tmp = b.GetComponentInChildren<TMP_Text>(includeInactive: true);
                    if (tmp != null) tmp.text = displayText;
                    var txt = b.GetComponentInChildren<Text>(includeInactive: true);
                    if (txt != null) txt.text = displayText;
                }
            }
        }

        public void SetStatus(string message)
        {
            if (statusTMPText == null && statusLegacyText == null && statusTextObject == null)
            {
                AutoDiscoverStatusText();
            }

            if (statusTMPText != null)
            {
                statusTMPText.text = message;
                statusTMPText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }

            if (statusLegacyText != null)
            {
                statusLegacyText.text = message;
                statusLegacyText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }

            if (statusTextObject != null && statusTMPText == null && statusLegacyText == null)
            {
                var tmp = statusTextObject.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    tmp.text = message;
                    statusTextObject.SetActive(!string.IsNullOrEmpty(message));
                }
                else
                {
                    var txt = statusTextObject.GetComponent<Text>();
                    if (txt != null)
                    {
                        txt.text = message;
                        statusTextObject.SetActive(!string.IsNullOrEmpty(message));
                    }
                }
            }

            Debug.Log($"[AuthLoginUI] {message}");
        }

        private void SetLoading(bool active)
        {
            if (loadingIndicator != null)
                loadingIndicator.SetActive(active);
        }

        private void BindSlot(ProviderSlot slot, UnityEngine.Events.UnityAction toggleAction)
        {
            if (slot == null || slot.allButtons == null) return;

            foreach (var btn in slot.allButtons)
            {
                if (btn == null) continue;
                EnsureButtonRaycastable(btn);
                btn.onClick.RemoveListener(toggleAction);
                btn.onClick.AddListener(toggleAction);
            }
        }

        private void EnsureButtonRaycastable(Button btn)
        {
            if (btn == null) return;

            var graphic = btn.targetGraphic;
            if (graphic == null)
            {
                graphic = btn.GetComponentInChildren<Graphic>(includeInactive: true);
                if (graphic != null)
                {
                    btn.targetGraphic = graphic;
                    graphic.raycastTarget = true;
                }
                else
                {
                    var img = btn.gameObject.AddComponent<Image>();
                    img.color = new Color(0, 0, 0, 0);
                    img.raycastTarget = true;
                    btn.targetGraphic = img;
                }
            }
            else
            {
                graphic.raycastTarget = true;
            }
        }

        private void AutoDiscoverStatusText()
        {
            if (statusTMPText != null || statusLegacyText != null)
                return;

            if (statusTextObject != null)
            {
                statusTMPText = statusTextObject.GetComponent<TMP_Text>();
                statusLegacyText = statusTextObject.GetComponent<Text>();
                return;
            }

            var allTMP = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
            foreach (var t in allTMP)
            {
                string n = t.gameObject.name.ToLowerInvariant();
                if (n.Contains("connect_text") || n.Contains("connecttext") || n.Contains("statustext") || n.Contains("authstatus") || n.Contains("connect"))
                {
                    statusTMPText = t;
                    statusTextObject = t.gameObject;
                    return;
                }
            }

            var allLegacy = FindObjectsByType<Text>(FindObjectsInactive.Include);
            foreach (var t in allLegacy)
            {
                string n = t.gameObject.name.ToLowerInvariant();
                if (n.Contains("connect_text") || n.Contains("connecttext") || n.Contains("statustext") || n.Contains("authstatus") || n.Contains("connect"))
                {
                    statusLegacyText = t;
                    statusTextObject = t.gameObject;
                    return;
                }
            }
        }

        private void AutoDiscoverProviderSlots()
        {
            google.allButtons.Clear();
            facebook.allButtons.Clear();
            instagram.allButtons.Clear();

            var allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include);
            foreach (var b in allButtons)
            {
                string n = b.gameObject.name.ToLowerInvariant();
                string parentName = b.transform.parent != null ? b.transform.parent.gameObject.name.ToLowerInvariant() : "";

                if (n.Contains("google") || n.Contains("goggle") || parentName.Contains("google") || parentName.Contains("goggle"))
                {
                    RegisterButtonToSlot(google, b);
                }
                else if (n.Contains("facebook") || parentName.Contains("facebook"))
                {
                    RegisterButtonToSlot(facebook, b);
                }
                else if (n.Contains("instagram") || parentName.Contains("instagram"))
                {
                    RegisterButtonToSlot(instagram, b);
                }
            }
        }

        private void RegisterButtonToSlot(ProviderSlot slot, Button b)
        {
            if (!slot.allButtons.Contains(b))
            {
                slot.allButtons.Add(b);
            }

            if (slot.button == null)
            {
                slot.button = b.gameObject;
            }

            if (slot.logoImage == null)
            {
                // Try to find the logo image on this button or its parent
                var img = b.GetComponent<Image>() ?? b.transform.parent?.GetComponent<Image>() ?? b.GetComponentInChildren<Image>(includeInactive: true);
                if (img != null && img.sprite != null && img.sprite.name != "UISprite")
                {
                    slot.logoImage = img;
                }
            }

            if (slot.labelTMP == null)
            {
                slot.labelTMP = b.GetComponentInChildren<TMP_Text>(includeInactive: true);
            }

            if (slot.labelLegacy == null)
            {
                slot.labelLegacy = b.GetComponentInChildren<Text>(includeInactive: true);
            }
        }

        private static string GetProviderDisplayName(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return "Account";
            switch (providerId.ToLowerInvariant())
            {
                case "google": return "Google";
                case "facebook": return "Facebook";
                case "instagram": return "Instagram";
                default: return providerId;
            }
        }
    }
}
