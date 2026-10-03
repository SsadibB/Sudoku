using System;
using System.Collections;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.Networking;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Facebook or Instagram (via Facebook Login) -> PlayFab LoginWithFacebook.
    /// Meta does not offer a separate consumer Instagram login token for PlayFab;
    /// Instagram uses Facebook Login with Instagram permissions.
    /// </summary>
    public class FacebookAuthProvider : IAuthProvider, IAccountProfile
    {
        public const string FacebookId = "facebook";
        public const string InstagramId = "instagram";

        // Request only permissions enabled under Meta Use Cases. Extra scopes (email, instagram_basic)
        // make Facebook show "Sorry, something went wrong" until those permissions are added.
        public static readonly string[] FacebookPermissions = { "public_profile" };
        public static readonly string[] InstagramPermissions = { "public_profile" };

        public string ProviderId { get; }
        public bool IsSignedIn { get; private set; }
        public string AccountDisplayName { get; private set; }
        public string AccountPhotoUrl { get; private set; }

        private readonly AuthSettings _settings;
        private readonly bool _createPlayFabAccountIfMissing;
        private readonly bool _fetchPlayerProfileOnLogin;
        private readonly string[] _permissions;
        private readonly IFacebookSignInClient _facebook;
        private int _attempt;

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

            int attempt = ++_attempt;
            AccountDisplayName = null;
            AccountPhotoUrl = null;
            _facebook.RequestAccessToken(
                _settings.FacebookAppId,
                _settings.FacebookClientToken,
                _permissions,
                accessToken =>
                {
                    if (attempt != _attempt)
                        return;

                    Coroutine routine = AuthMainThread.Run(FetchProfileThenLogin(accessToken, attempt, onSuccess, onFailure));
                    if (routine == null)
                        LoginToPlayFab(accessToken, attempt, onSuccess, onFailure);
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
            _facebook.SignOut();
            PlayFabClientAPI.ForgetAllCredentials();
            IsSignedIn = false;
            AccountDisplayName = null;
            AccountPhotoUrl = null;
        }

        private IEnumerator FetchProfileThenLogin(
            string accessToken,
            int attempt,
            Action<LoginResult> onSuccess,
            Action<AuthError> onFailure)
        {
            string url = "https://graph.facebook.com/me?fields=name,picture.type(large)&access_token="
                         + UnityWebRequest.EscapeURL(accessToken);
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (attempt != _attempt)
                    yield break;

                if (request.result == UnityWebRequest.Result.Success)
                    ReadGraphProfile(request.downloadHandler.text);
            }

            if (attempt != _attempt)
                yield break;

            LoginToPlayFab(accessToken, attempt, onSuccess, onFailure);
        }

        private void ReadGraphProfile(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            GraphProfile profile = JsonUtility.FromJson<GraphProfile>(json);
            if (profile == null)
                return;

            if (!string.IsNullOrWhiteSpace(profile.name))
                AccountDisplayName = profile.name.Trim();
            if (!string.IsNullOrWhiteSpace(profile.picture?.data?.url))
                AccountPhotoUrl = profile.picture.data.url.Trim();
        }

        [Serializable]
        private class GraphProfile
        {
            public string name;
            public GraphPicture picture;
        }

        [Serializable]
        private class GraphPicture
        {
            public GraphPictureData data;
        }

        [Serializable]
        private class GraphPictureData
        {
            public string url;
        }

        private void LoginToPlayFab(string accessToken, int attempt, Action<LoginResult> onSuccess, Action<AuthError> onFailure)
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
