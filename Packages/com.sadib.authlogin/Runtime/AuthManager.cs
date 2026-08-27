using System;
using UnityEngine;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// The single entry point your game code talks to. Drop on a persistent GameObject
    /// and wire UI Button On Click() in the Inspector to SignInWithGoogle / Facebook / Instagram.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private AuthSettings settings;
        [Tooltip("On Start, try a silent Google sign-in (no UI) so returning players skip the login screen.")]
        [SerializeField] private bool autoSignInSilentlyOnStart = true;
        [SerializeField] private bool createPlayFabAccountIfMissing = true;
        [SerializeField] private bool fetchPlayerProfileOnLogin = true;

        public event Action<AuthSession> OnLoginSuccess;
        public event Action<AuthError> OnLoginFailure;
        public event Action<string> OnLoginStarted;
        public event Action<string> OnSignedOut;

        public bool IsSignedIn => CurrentSession != null;
        public bool IsBusy { get; private set; }
        public AuthSession CurrentSession { get; private set; }
        public string LastPlayFabId => CurrentSession?.PlayFabId;
        public string LastProviderId => CurrentSession?.ProviderId;

        public bool IsGoogleSignedIn => _google != null && _google.IsSignedIn;
        public bool IsFacebookSignedIn => _facebook != null && _facebook.IsSignedIn;
        public bool IsInstagramSignedIn => _instagram != null && _instagram.IsSignedIn;

        public bool IsProviderSignedIn(string providerId)
        {
            if (string.Equals(providerId, GoogleAuthProvider.Id, StringComparison.OrdinalIgnoreCase))
                return IsGoogleSignedIn;
            if (string.Equals(providerId, FacebookAuthProvider.FacebookId, StringComparison.OrdinalIgnoreCase))
                return IsFacebookSignedIn;
            if (string.Equals(providerId, FacebookAuthProvider.InstagramId, StringComparison.OrdinalIgnoreCase))
                return IsInstagramSignedIn;
            return false;
        }

        public AuthSettings Settings => settings;

        private GoogleAuthProvider _google;
        private FacebookAuthProvider _facebook;
        private FacebookAuthProvider _instagram;

        public static AuthManager EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<AuthManager>();
            if (existing != null)
                return existing;

            var go = new GameObject("AuthManager");
            return go.AddComponent<AuthManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            AuthMainThread.Ensure(gameObject);

            if (settings == null)
                settings = AuthSettings.LoadFromResources();

            RebuildProvider();
        }

        /// <summary>Assign AuthSettings at runtime before the first sign-in (e.g. from a sample scene).</summary>
        public void Configure(AuthSettings authSettings, bool autoSilentOnStart)
        {
            settings = authSettings;
            autoSignInSilentlyOnStart = autoSilentOnStart;
            RebuildProvider();
        }

        private void RebuildProvider()
        {
            _google = new GoogleAuthProvider(settings, createPlayFabAccountIfMissing, fetchPlayerProfileOnLogin);
            _facebook = new FacebookAuthProvider(
                settings,
                FacebookAuthProvider.FacebookId,
                FacebookAuthProvider.FacebookPermissions,
                createPlayFabAccountIfMissing,
                fetchPlayerProfileOnLogin);
            _instagram = new FacebookAuthProvider(
                settings,
                FacebookAuthProvider.InstagramId,
                FacebookAuthProvider.InstagramPermissions,
                createPlayFabAccountIfMissing,
                fetchPlayerProfileOnLogin);
        }

        private void Start()
        {
            if (autoSignInSilentlyOnStart)
                SignIn(_google, silent: true);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Inspector On Click(): show the Google account picker.</summary>
        public void SignInWithGoogle()
        {
            SignIn(_google, silent: false);
        }

        public void SignInWithGoogle(bool silent)
        {
            SignIn(_google, silent);
        }

        /// <summary>Inspector On Click(): Facebook Login, then PlayFab LoginWithFacebook.</summary>
        public void SignInWithFacebook()
        {
            SignIn(_facebook, silent: false);
        }

        /// <summary>Inspector On Click(): Facebook Login with Instagram permissions, then PlayFab LoginWithFacebook.</summary>
        public void SignInWithInstagram()
        {
            SignIn(_instagram, silent: false);
        }

        /// <summary>Inspector On Click(): Sign out from Google.</summary>
        public void SignOutGoogle()
        {
            _google?.SignOut();
            if (CurrentSession != null && string.Equals(CurrentSession.ProviderId, GoogleAuthProvider.Id, StringComparison.OrdinalIgnoreCase))
            {
                CurrentSession = null;
            }
            Debug.Log("[AuthManager] Signed out from Google.");
            OnSignedOut?.Invoke(GoogleAuthProvider.Id);
        }

        /// <summary>Inspector On Click(): Sign out from Facebook.</summary>
        public void SignOutFacebook()
        {
            _facebook?.SignOut();
            if (CurrentSession != null && string.Equals(CurrentSession.ProviderId, FacebookAuthProvider.FacebookId, StringComparison.OrdinalIgnoreCase))
            {
                CurrentSession = null;
            }
            Debug.Log("[AuthManager] Signed out from Facebook.");
            OnSignedOut?.Invoke(FacebookAuthProvider.FacebookId);
        }

        /// <summary>Inspector On Click(): Sign out from Instagram.</summary>
        public void SignOutInstagram()
        {
            _instagram?.SignOut();
            if (CurrentSession != null && string.Equals(CurrentSession.ProviderId, FacebookAuthProvider.InstagramId, StringComparison.OrdinalIgnoreCase))
            {
                CurrentSession = null;
            }
            Debug.Log("[AuthManager] Signed out from Instagram.");
            OnSignedOut?.Invoke(FacebookAuthProvider.InstagramId);
        }

        public void SignOut(string providerId)
        {
            if (string.Equals(providerId, GoogleAuthProvider.Id, StringComparison.OrdinalIgnoreCase))
                SignOutGoogle();
            else if (string.Equals(providerId, FacebookAuthProvider.FacebookId, StringComparison.OrdinalIgnoreCase))
                SignOutFacebook();
            else if (string.Equals(providerId, FacebookAuthProvider.InstagramId, StringComparison.OrdinalIgnoreCase))
                SignOutInstagram();
            else
                SignOut();
        }

        /// <summary>Inspector On Click(): Sign out from all providers.</summary>
        public void SignOut()
        {
            _google?.SignOut();
            _facebook?.SignOut();
            _instagram?.SignOut();
            IsBusy = false;
            CurrentSession = null;
            Debug.Log("[AuthManager] Signed out from all providers.");
            OnSignedOut?.Invoke("all");
        }

        private void SignIn(IAuthProvider provider, bool silent)
        {
            if (provider == null)
            {
                OnLoginFailure?.Invoke(AuthError.Configuration("none", "Auth provider is not initialized."));
                return;
            }

            if (IsBusy)
            {
                if (!silent)
                    OnLoginFailure?.Invoke(AuthError.InProgress(provider.ProviderId));
                return;
            }

            if (provider.IsSignedIn && CurrentSession != null)
            {
                OnLoginSuccess?.Invoke(CurrentSession);
                return;
            }

            IsBusy = true;
            OnLoginStarted?.Invoke(provider.ProviderId);
            provider.SignIn(
                silent,
                onSuccess: result =>
                {
                    IsBusy = false;
                    CurrentSession = AuthSession.FromLogin(provider.ProviderId, result);
                    Debug.Log($"[AuthManager] Login OK via {provider.ProviderId}. PlayFabId={result.PlayFabId} NewAccount={result.NewlyCreated}");
                    OnLoginSuccess?.Invoke(CurrentSession);
                },
                onFailure: error =>
                {
                    IsBusy = false;
                    bool hideSilentCancel = silent && error.Code == AuthErrorCode.Cancelled;
                    if (!hideSilentCancel)
                    {
                        if (error.Code != AuthErrorCode.Cancelled && error.Code != AuthErrorCode.UnsupportedPlatform)
                            Debug.LogError($"[AuthManager] {error}");
                        OnLoginFailure?.Invoke(error);
                    }
                });
        }
    }
}
