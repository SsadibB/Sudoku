using System;

namespace SadibTools.AuthLogin
{
    public sealed class GoogleNativeAccount
    {
        public string ServerAuthCode { get; }
        public string DisplayName { get; }
        public string PhotoUrl { get; }
        public string Email { get; }

        public GoogleNativeAccount(string serverAuthCode, string displayName, string photoUrl, string email)
        {
            ServerAuthCode = serverAuthCode;
            DisplayName = displayName;
            PhotoUrl = photoUrl;
            Email = email;
        }
    }

    internal interface IGoogleSignInClient
    {
        bool IsSupported { get; }

        void RequestServerAuthCode(
            string webClientId,
            bool silent,
            Action<GoogleNativeAccount> onSuccess,
            Action<AuthError> onFailure);

        void SignOut();
    }
}
