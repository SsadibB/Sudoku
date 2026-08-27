using System;

namespace SadibTools.AuthLogin
{
    internal interface IGoogleSignInClient
    {
        bool IsSupported { get; }

        void RequestServerAuthCode(
            string webClientId,
            bool silent,
            Action<string> onSuccess,
            Action<AuthError> onFailure);

        void SignOut();
    }
}
