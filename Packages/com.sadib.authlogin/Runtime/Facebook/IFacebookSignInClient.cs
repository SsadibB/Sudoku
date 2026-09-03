using System;

namespace SadibTools.AuthLogin
{
    internal interface IFacebookSignInClient
    {
        bool IsSupported { get; }

        void RequestAccessToken(
            string appId,
            string clientToken,
            string[] permissions,
            Action<string> onSuccess,
            Action<AuthError> onFailure);

        void SignOut();
    }
}
