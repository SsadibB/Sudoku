using System;

namespace SadibTools.AuthLogin
{
    internal sealed class UnsupportedGoogleSignInClient : IGoogleSignInClient
    {
        public const string ProviderId = "google";

        public bool IsSupported => false;

        public void RequestServerAuthCode(
            string webClientId,
            bool silent,
            Action<string> onSuccess,
            Action<AuthError> onFailure)
        {
            onFailure?.Invoke(AuthError.Unsupported(
                ProviderId,
                "Google Account login requires a signed Android device build. It is not available in the Unity Editor."));
        }

        public void SignOut()
        {
        }
    }
}
