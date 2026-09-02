using System;
using PlayFab;
using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Google Account -> PlayFab via LoginWithGoogleAccount.
    /// Captures Google Account DisplayName and PhotoUrl directly from native Android sign-in.
    /// </summary>
    public class GoogleAuthProvider : IAuthProvider
    {
        public const string Id = "google";

        public string ProviderId => Id;
        public bool IsSignedIn { get; private set; }
        public string LastPhotoUrl { get; private set; }
        public string LastDisplayName { get; private set; }

        private readonly AuthSettings _settings;
        private readonly bool _createPlayFabAccountIfMissing;
        private readonly bool _fetchPlayerProfileOnLogin;
        private readonly IGoogleSignInClient _google;

        public GoogleAuthProvider(
            AuthSettings settings,
            bool createPlayFabAccountIfMissing = true,
            bool fetchPlayerProfileOnLogin = true)
        {
            _settings = settings;
            _createPlayFabAccountIfMissing = createPlayFabAccountIfMissing;
            _fetchPlayerProfileOnLogin = fetchPlayerProfileOnLogin;
            _google = GoogleSignInClientFactory.Create();
        }

        public void SignIn(bool silent, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
        {
            if (_settings == null || !_settings.HasGoogleWebClientId)
            {
                onFailure?.Invoke(AuthError.Configuration(
                    ProviderId,
                    "AuthSettings is missing a Google Web Client ID. Create Auth Login/Auth Settings and paste the OAuth Web application client ID."));
                return;
            }

            _google.RequestServerAuthCode(
                _settings.GoogleWebClientId,
                silent,
                nativeAccount =>
                {
                    LastPhotoUrl = nativeAccount?.PhotoUrl;
                    LastDisplayName = nativeAccount?.DisplayName;
                    LoginToPlayFab(nativeAccount?.ServerAuthCode, onSuccess, onFailure);
                },
                error =>
                {
                    IsSignedIn = false;
                    onFailure?.Invoke(error);
                });
        }

        public void SignOut()
        {
            _google.SignOut();
            PlayFabClientAPI.ForgetAllCredentials();
            IsSignedIn = false;
            LastPhotoUrl = null;
            LastDisplayName = null;
        }

        private void LoginToPlayFab(string serverAuthCode, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
        {
            var request = new LoginWithGoogleAccountRequest
            {
                ServerAuthCode = serverAuthCode,
                CreateAccount = _createPlayFabAccountIfMissing,
                SetEmail = true,
                TitleId = _settings.PlayFabTitleId,
                InfoRequestParameters = _fetchPlayerProfileOnLogin
                    ? new GetPlayerCombinedInfoRequestParams
                    {
                        GetPlayerProfile = true,
                        GetUserAccountInfo = true
                    }
                    : null
            };

            PlayFabClientAPI.LoginWithGoogleAccount(
                request,
                result =>
                {
                    AuthMainThread.Post(() =>
                    {
                        IsSignedIn = true;
                        onSuccess?.Invoke(result);
                    });
                },
                error =>
                {
                    AuthMainThread.Post(() =>
                    {
                        IsSignedIn = false;
                        onFailure?.Invoke(AuthError.FromPlayFab(ProviderId, error));
                    });
                });
        }
    }
}
