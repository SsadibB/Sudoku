using System;
using PlayFab;
using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Google Account -> PlayFab via LoginWithGoogleAccount (server auth code from
    /// the Google Identity Authorization API on Android).
    /// </summary>
    public class GoogleAuthProvider : IAuthProvider, IAccountProfile
    {
        public const string Id = "google";

        public string ProviderId => Id;
        public bool IsSignedIn { get; private set; }
        public string AccountDisplayName { get; private set; }
        public string AccountPhotoUrl { get; private set; }

        private readonly AuthSettings _settings;
        private readonly bool _createPlayFabAccountIfMissing;
        private readonly bool _fetchPlayerProfileOnLogin;
        private readonly IGoogleSignInClient _google;
        private int _attempt;

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

            int attempt = ++_attempt;
            AccountDisplayName = null;
            AccountPhotoUrl = null;
            _google.RequestServerAuthCode(
                _settings.GoogleWebClientId,
                silent,
                authCode =>
                {
                    if (attempt != _attempt)
                        return;

                    AccountDisplayName = PendingDisplayName;
                    AccountPhotoUrl = PendingPhotoUrl;
                    LoginToPlayFab(authCode, attempt, onSuccess, onFailure);
                },
                error =>
                {
                    if (attempt != _attempt)
                        return;

                    IsSignedIn = false;
                    onFailure?.Invoke(error);
                });
        }

        public void SignOut()
        {
            _attempt++;
            _google.SignOut();
            PlayFabClientAPI.ForgetAllCredentials();
            IsSignedIn = false;
            AccountDisplayName = null;
            AccountPhotoUrl = null;
            PendingDisplayName = null;
            PendingPhotoUrl = null;
        }

        internal static string PendingDisplayName { get; private set; }
        internal static string PendingPhotoUrl { get; private set; }

        internal static void TakeProfilePayload(string payload, out string serverAuthCode)
        {
            serverAuthCode = payload ?? "";
            PendingDisplayName = null;
            PendingPhotoUrl = null;
            if (string.IsNullOrEmpty(payload))
                return;

            int first = payload.IndexOf('\n');
            if (first < 0)
                return;

            serverAuthCode = payload.Substring(0, first);
            int second = payload.IndexOf('\n', first + 1);
            if (second < 0)
            {
                PendingDisplayName = Clean(payload.Substring(first + 1));
                return;
            }

            PendingDisplayName = Clean(payload.Substring(first + 1, second - first - 1));
            PendingPhotoUrl = Clean(payload.Substring(second + 1));
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Trim();
        }

        private void LoginToPlayFab(string serverAuthCode, int attempt, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
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
                        if (attempt != _attempt)
                            return;

                        IsSignedIn = true;
                        onSuccess?.Invoke(result);
                    });
                },
                error =>
                {
                    AuthMainThread.Post(() =>
                    {
                        if (attempt != _attempt)
                            return;

                        IsSignedIn = false;
                        onFailure?.Invoke(AuthError.FromPlayFab(ProviderId, error));
                    });
                });
        }
    }
}
