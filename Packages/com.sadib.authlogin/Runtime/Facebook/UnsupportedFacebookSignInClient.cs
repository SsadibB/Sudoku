using System;

namespace SadibTools.AuthLogin
{
    internal sealed class UnsupportedFacebookSignInClient : IFacebookSignInClient
    {
        private readonly string _providerId;

        public UnsupportedFacebookSignInClient(string providerId)
        {
            _providerId = providerId;
        }

        public bool IsSupported => false;

        public void RequestAccessToken(
            string appId,
            string clientToken,
            string[] permissions,
            Action<FacebookNativeAccount> onSuccess,
            Action<AuthError> onFailure)
        {
#if UNITY_EDITOR
            string message = "Facebook / Instagram login requires a device build. It is not available in the Unity Editor.";
#elif !FACEBOOK_SDK && !SADIB_AUTH_FACEBOOK_SDK
            string message = "Facebook SDK is not installed or enabled in this project. To use Facebook/Instagram login, import the Facebook Unity SDK and add 'FACEBOOK_SDK' (or 'SADIB_AUTH_FACEBOOK_SDK') to Player Settings -> Scripting Define Symbols.";
#else
            string message = "Facebook / Instagram login is not supported on this platform.";
#endif
            onFailure?.Invoke(AuthError.Unsupported(_providerId, message));
        }

        public void SignOut()
        {
        }
    }
}
