using System;

namespace SadibTools.AuthLogin
{
    public sealed class FacebookNativeAccount
    {
        public string AccessToken { get; }
        public string UserId { get; }
        public string PhotoUrl { get; }

        public FacebookNativeAccount(string accessToken, string userId)
        {
            AccessToken = accessToken;
            UserId = userId;
            PhotoUrl = !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(accessToken)
                ? $"https://graph.facebook.com/{userId}/picture?type=large&access_token={accessToken}"
                : null;
        }
    }

    internal interface IFacebookSignInClient
    {
        bool IsSupported { get; }

        void RequestAccessToken(
            string appId,
            string clientToken,
            string[] permissions,
            Action<FacebookNativeAccount> onSuccess,
            Action<AuthError> onFailure);

        void SignOut();
    }
}
