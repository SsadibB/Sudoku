using System;
using PlayFab;
using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Facebook or Instagram (via Facebook Login) -> PlayFab LoginWithFacebook.
    /// Captures Facebook Profile Picture URL with authenticated access token.
    /// </summary>
    public class FacebookAuthProvider : IAuthProvider
    {
        public const string FacebookId = "facebook";
        public const string InstagramId = "instagram";

        public static readonly string[] FacebookPermissions = { "public_profile" };
        public static readonly string[] InstagramPermissions = { "public_profile" };

        public string ProviderId { get; }
        public bool IsSignedIn { get; private set; }
        public string LastPhotoUrl { get; private set; }

        private readonly AuthSettings _settings;
        private readonly bool _createPlayFabAccountIfMissing;
        private readonly bool _fetchPlayerProfileOnLogin;
        private readonly string[] _permissions;
        private readonly IFacebookSignInClient _facebook;

        public FacebookAuthProvider(
            AuthSettings settings,
            string providerId,
            string[] permissions,
            bool createPlayFabAccountIfMissing = true,
            bool fetchPlayerProfileOnLogin = true)
        {
            _settings = settings;
            ProviderId = string.IsNullOrEmpty(providerId) ? FacebookId : providerId;
            _permissions = permissions ?? FacebookPermissions;
            _createPlayFabAccountIfMissing = createPlayFabAccountIfMissing;
            _fetchPlayerProfileOnLogin = fetchPlayerProfileOnLogin;
            _facebook = FacebookSignInClientFactory.Create(ProviderId);
        }

        public void SignIn(bool silent, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
        {
            if (_settings == null || !_settings.HasFacebookAppId)
            {
                onFailure?.Invoke(AuthError.Configuration(
                    ProviderId,
                    "AuthSettings is missing Facebook App ID or Client Token. Create a Meta app, enable Facebook Login, and paste both values on Auth Settings."));
                return;
            }

            if (silent)
            {
                onFailure?.Invoke(AuthError.Cancelled(ProviderId, "Silent Facebook / Instagram sign-in is not supported."));
                return;
            }

            _facebook.RequestAccessToken(
                _settings.FacebookAppId,
                _settings.FacebookClientToken,
                _permissions,
                nativeAccount =>
                {
                    LastPhotoUrl = nativeAccount?.PhotoUrl;
                    LoginToPlayFab(nativeAccount?.AccessToken, onSuccess, onFailure);
                },
                error =>
                {
                    IsSignedIn = false;
                    onFailure?.Invoke(error);
                });
        }

        public void SignOut()
        {
            _facebook.SignOut();
            PlayFabClientAPI.ForgetAllCredentials();
            IsSignedIn = false;
            LastPhotoUrl = null;
        }

        private void LoginToPlayFab(string accessToken, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
        {
            var request = new LoginWithFacebookRequest
            {
                AccessToken = accessToken,
                CreateAccount = _createPlayFabAccountIfMissing,
                TitleId = _settings.PlayFabTitleId,
                InfoRequestParameters = _fetchPlayerProfileOnLogin
                    ? new GetPlayerCombinedInfoRequestParams
                    {
                        GetPlayerProfile = true,
                        GetUserAccountInfo = true
                    }
                    : null
            };

            PlayFabClientAPI.LoginWithFacebook(
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
