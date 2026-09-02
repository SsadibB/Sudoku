using System;
using System.Collections.Generic;
using UnityEngine;

namespace SadibTools.AuthLogin
{
#if (FACEBOOK_SDK || SADIB_AUTH_FACEBOOK_SDK) && !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
    using Facebook.Unity;

    /// <summary>
    /// Facebook / Instagram login via the official Facebook SDK for Unity.
    /// Captures access token and user ID to construct authenticated profile picture URL.
    /// </summary>
    internal sealed class UnityFacebookSignInClient : IFacebookSignInClient
    {
        private readonly string _providerId;

        public UnityFacebookSignInClient(string providerId)
        {
            _providerId = providerId;
        }

        public bool IsSupported => true;

        public void RequestAccessToken(
            string appId,
            string clientToken,
            string[] permissions,
            Action<FacebookNativeAccount> onSuccess,
            Action<AuthError> onFailure)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(clientToken))
            {
                onFailure?.Invoke(AuthError.Configuration(
                    _providerId,
                    "AuthSettings is missing Facebook App ID or Client Token."));
                return;
            }

            EnsureInitialized(appId, clientToken, () =>
            {
                try
                {
                    IEnumerable<string> scopes = permissions != null && permissions.Length > 0
                        ? permissions
                        : FacebookAuthProvider.FacebookPermissions;

                    FB.LogInWithReadPermissions(scopes, result => HandleLoginResult(result, onSuccess, onFailure));
                }
                catch (Exception ex)
                {
                    onFailure?.Invoke(AuthError.Native(_providerId, ex.Message));
                }
            }, onFailure);
        }

        public void SignOut()
        {
            try
            {
                if (FB.IsInitialized)
                    FB.LogOut();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityFacebookSignInClient] Facebook LogOut failed: " + ex.Message);
            }
        }

        private void EnsureInitialized(
            string appId,
            string clientToken,
            Action onReady,
            Action<AuthError> onFailure)
        {
            if (FB.IsInitialized)
            {
                onReady();
                return;
            }

            FB.Init(
                appId,
                clientToken,
                true,
                true,
                true,
                false,
                true,
                null,
                "en_US",
                null,
                () =>
                {
                    AuthMainThread.Post(() =>
                    {
                        if (!FB.IsInitialized)
                        {
                            onFailure?.Invoke(AuthError.Native(_providerId, "Facebook SDK failed to initialize."));
                            return;
                        }

                        FB.ActivateApp();
                        onReady();
                    });
                });
        }

        private void HandleLoginResult(
            ILoginResult result,
            Action<FacebookNativeAccount> onSuccess,
            Action<AuthError> onFailure)
        {
            AuthMainThread.Post(() =>
            {
                if (result == null)
                {
                    onFailure?.Invoke(AuthError.Native(_providerId, "Facebook login returned no result."));
                    return;
                }

                if (result.Cancelled)
                {
                    onFailure?.Invoke(AuthError.Cancelled(_providerId, "User cancelled Facebook sign-in."));
                    return;
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    onFailure?.Invoke(AuthError.Native(_providerId, result.Error));
                    return;
                }

                AccessToken token = result.AccessToken ?? AccessToken.CurrentAccessToken;
                if (token == null || string.IsNullOrEmpty(token.TokenString))
                {
                    onFailure?.Invoke(AuthError.Native(_providerId, "Facebook returned an empty access token."));
                    return;
                }

                var account = new FacebookNativeAccount(token.TokenString, token.UserId);
                onSuccess?.Invoke(account);
            });
        }
    }
#endif
}
