using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Drop-in UI component to connect Google, Facebook, and Instagram Login/Logout buttons
    /// and display real-time connection status, success messages, and account details.
    /// Supports both TextMeshPro (TMP_Text) and Standard/Legacy UI Text.
    /// </summary>
    [AddComponentMenu("Auth Login/Auth Login UI")]
    public class AuthLoginUI : MonoBehaviour
    {
        [Header("Google Buttons")]
        [Tooltip("Button or Container shown when NOT connected with Google.")]
        [SerializeField] private GameObject googleLoginButton;
        [Tooltip("Button or Container shown when connected with Google.")]
        [SerializeField] private GameObject googleLogoutButton;

        [Header("Facebook Buttons")]
        [Tooltip("Button or Container shown when NOT connected with Facebook.")]
        [SerializeField] private GameObject facebookLoginButton;
        [Tooltip("Button or Container shown when connected with Facebook.")]
        [SerializeField] private GameObject facebookLogoutButton;

        [Header("Instagram Buttons")]
        [Tooltip("Button or Container shown when NOT connected with Instagram.")]
        [SerializeField] private GameObject instagramLoginButton;
        [Tooltip("Button or Container shown when connected with Instagram.")]
        [SerializeField] private GameObject instagramLogoutButton;

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

        [Header("Auto Discovery & Raycasting")]
        [Tooltip("Automatically bind buttons and find 'Connect_Text' if unassigned.")]
        [SerializeField] private bool autoDiscoverElements = true;

        private void Awake()
        {
            if (autoDiscoverElements)
            {
                AutoDiscoverStatusText();
                AutoDiscoverButtons();
            }

            SetupButton(googleLoginButton, OnClickSignInGoogle);
            SetupButton(googleLogoutButton, OnClickSignOutGoogle);

            SetupButton(facebookLoginButton, OnClickSignInFacebook);
            SetupButton(facebookLogoutButton, OnClickSignOutFacebook);

            SetupButton(instagramLoginButton, OnClickSignInInstagram);
            SetupButton(instagramLogoutButton, OnClickSignOutInstagram);
        }

        private void OnEnable()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginStarted += HandleLoginStarted;
                AuthManager.Instance.OnLoginSuccess += HandleLoginSuccess;
                AuthManager.Instance.OnLoginFailure += HandleLoginFailure;
                AuthManager.Instance.OnSignedOut += HandleSignedOut;
            }

            RefreshUI();
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
            RefreshUI();
        }

        // ==================== Actions (Callable from UI Buttons) ====================

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
            string providerName = GetProviderDisplayName(session.ProviderId);
            string userDisplay = !string.IsNullOrEmpty(session.DisplayName)
                ? session.DisplayName
                : (!string.IsNullOrEmpty(session.Email) ? session.Email : session.PlayFabId);

            SetStatus($"<color=#4CAF50>Connected ({providerName}): {userDisplay}</color>");

            if (accountInfoText != null)
            {
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
            string providerName = providerId == "all" ? "All providers" : GetProviderDisplayName(providerId);
            SetStatus($"Signed out from {providerName}.");

            if (accountInfoText != null && (AuthManager.Instance == null || !AuthManager.Instance.IsSignedIn))
            {
                accountInfoText.text = string.Empty;
                accountInfoText.gameObject.SetActive(false);
            }

            RefreshUI();
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

            // Google Buttons
            SetPairState(googleLoginButton, googleLogoutButton, isGoogle);

            // Facebook Buttons
            SetPairState(facebookLoginButton, facebookLogoutButton, isFacebook);

            // Instagram Buttons
            SetPairState(instagramLoginButton, instagramLogoutButton, isInstagram);

            // Account info if already logged in
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

        private void SetPairState(GameObject loginObj, GameObject logoutObj, bool isSignedIn)
        {
            if (loginObj != null) loginObj.SetActive(!isSignedIn);
            if (logoutObj != null) logoutObj.SetActive(isSignedIn);
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

        private void SetupButton(GameObject target, UnityEngine.Events.UnityAction action)
        {
            if (target == null) return;

            // Ensure Button is raycastable on mobile touchscreens
            var btn = target.GetComponent<Button>();
            if (btn != null)
            {
                EnsureButtonRaycastable(btn);
                btn.onClick.RemoveListener(action);
                btn.onClick.AddListener(action);
            }
        }

        /// <summary>
        /// Ensures a UI button has a valid Graphic with raycastTarget=true
        /// so touch clicks register reliably across its full rect.
        /// </summary>
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
                    // Add transparent Image so the entire RectTransform bounds are clickable
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

            // Search for Connect_Text or StatusText in hierarchy
            var allTMP = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in allTMP)
            {
                string n = t.gameObject.name.ToLowerInvariant();
                if (n.Contains("connect_text") || n.Contains("connecttext") || n.Contains("statustext") || n.Contains("authstatus"))
                {
                    statusTMPText = t;
                    statusTextObject = t.gameObject;
                    return;
                }
            }

            var allLegacy = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in allLegacy)
            {
                string n = t.gameObject.name.ToLowerInvariant();
                if (n.Contains("connect_text") || n.Contains("connecttext") || n.Contains("statustext") || n.Contains("authstatus"))
                {
                    statusLegacyText = t;
                    statusTextObject = t.gameObject;
                    return;
                }
            }
        }

        private void AutoDiscoverButtons()
        {
            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in buttons)
            {
                string n = b.gameObject.name.ToLowerInvariant();
                if (googleLoginButton == null && (n == "google" || n == "goggle" || n.Contains("googlelogin") || n.Contains("googlesignin")))
                    googleLoginButton = b.gameObject;
                else if (googleLogoutButton == null && (n.Contains("googlelogout") || n.Contains("googlesignout")))
                    googleLogoutButton = b.gameObject;
                else if (facebookLoginButton == null && (n == "facebook" || n.Contains("facebooklogin") || n.Contains("facebooksignin")))
                    facebookLoginButton = b.gameObject;
                else if (facebookLogoutButton == null && (n.Contains("facebooklogout") || n.Contains("facebooksignout")))
                    facebookLogoutButton = b.gameObject;
                else if (instagramLoginButton == null && (n == "instagram" || n.Contains("instagramlogin") || n.Contains("instagramsignin")))
                    instagramLoginButton = b.gameObject;
                else if (instagramLogoutButton == null && (n.Contains("instagramlogout") || n.Contains("instagramsignout")))
                    instagramLogoutButton = b.gameObject;
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
