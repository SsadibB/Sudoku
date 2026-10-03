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
        /// <summary>True while a silent startup sign-in is the attempt currently in flight.</summary>
        public bool IsSilentSignIn => IsBusy && _silentInProgress;
        public bool LastSignInWasExplicit { get; private set; }
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
        private IAuthProvider _activeProvider;
        private int _signInGeneration;
        private bool _silentInProgress;

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
            AuthLoginUI.EnsureInstance();
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
            // Do not start a silent Google sign-in here. It uses the same authorization
            // activity as the account button, and Play Services then cancels that tap.
            if (!autoSignInSilentlyOnStart)
                return;
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
            SignOutProvider(_google, GoogleAuthProvider.Id, notify: true);
        }

        private void SignOutOthers(string keepProviderId)
        {
            if (!string.Equals(keepProviderId, GoogleAuthProvider.Id, StringComparison.OrdinalIgnoreCase))
                SignOutProvider(_google, GoogleAuthProvider.Id, notify: true);
            if (!string.Equals(keepProviderId, FacebookAuthProvider.FacebookId, StringComparison.OrdinalIgnoreCase))
                SignOutProvider(_facebook, FacebookAuthProvider.FacebookId, notify: true);
            if (!string.Equals(keepProviderId, FacebookAuthProvider.InstagramId, StringComparison.OrdinalIgnoreCase))
                SignOutProvider(_instagram, FacebookAuthProvider.InstagramId, notify: true);
        }

        private void SignOutProvider(IAuthProvider provider, string providerId, bool notify)
        {
            if (provider == null || !provider.IsSignedIn)
                return;

            provider.SignOut();
            if (CurrentSession != null && string.Equals(CurrentSession.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                CurrentSession = null;

            Debug.Log("[AuthManager] Signed out from " + providerId + ".");
            if (notify)
                OnSignedOut?.Invoke(providerId);
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
            CancelInFlightSignIn();
            _google?.SignOut();
            _facebook?.SignOut();
            _instagram?.SignOut();
            CurrentSession = null;
            Debug.Log("[AuthManager] Signed out from all providers.");
            OnSignedOut?.Invoke("all");
        }

        /// <summary>
        /// Drops an in-flight attempt so a late callback cannot finish it
        /// or block the next login. A connected account is left for the caller to sign out.
        /// </summary>
        private void CancelInFlightSignIn()
        {
            IAuthProvider active = _activeProvider;
            _signInGeneration++;
            _silentInProgress = false;
            IsBusy = false;
            _activeProvider = null;
            if (active != null && !active.IsSignedIn)
                active.SignOut();
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
                // A silent startup attempt must not block the account button.
                // Any other in-flight attempt (double click or a second button listener)
                // is ignored until it completes, fails, or is cancelled.
                if (silent || !_silentInProgress)
                    return;

                CancelInFlightSignIn();
            }

            if (provider.IsSignedIn && CurrentSession != null
                && string.Equals(CurrentSession.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                LastSignInWasExplicit = false;
                OnLoginSuccess?.Invoke(CurrentSession);
                return;
            }

            // A player can connect only one account. An explicit sign-in replaces the others.
            int generation = ++_signInGeneration;
            _silentInProgress = silent;
            _activeProvider = provider;
            LastSignInWasExplicit = !silent;
            if (!silent)
                SignOutOthers(provider.ProviderId);

            IsBusy = true;
            if (!silent)
                OnLoginStarted?.Invoke(provider.ProviderId);
            provider.SignIn(
                silent,
                onSuccess: result =>
                {
                    if (generation != _signInGeneration)
                        return;

                    IsBusy = false;
                    _silentInProgress = false;
                    _activeProvider = null;
                    string displayName = null;
                    string photoUrl = null;
                    if (provider is IAccountProfile account)
                    {
                        displayName = account.AccountDisplayName;
                        photoUrl = account.AccountPhotoUrl;
                    }

                    CurrentSession = AuthSession.FromLogin(provider.ProviderId, result, displayName, photoUrl);
                    Debug.Log($"[AuthManager] Login OK via {provider.ProviderId}. PlayFabId={result.PlayFabId} NewAccount={result.NewlyCreated}");
                    OnLoginSuccess?.Invoke(CurrentSession);
                },
                onFailure: error =>
                {
                    if (generation != _signInGeneration)
                        return;

                    IsBusy = false;
                    _silentInProgress = false;
                    _activeProvider = null;
                    bool hideSilentCancel = silent && error != null && error.Code == AuthErrorCode.Cancelled;
                    if (!hideSilentCancel)
                    {
                        if (error != null && error.Code != AuthErrorCode.Cancelled && error.Code != AuthErrorCode.UnsupportedPlatform)
                            Debug.LogError($"[AuthManager] {error}");
                        OnLoginFailure?.Invoke(error);
                    }
                });
        }
    }
}
