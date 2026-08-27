using PlayFab;

namespace SadibTools.AuthLogin
{
    public enum AuthErrorCode
    {
        Cancelled,
        Configuration,
        Native,
        PlayFab,
        Network,
        UnsupportedPlatform,
        InProgress
    }

    public sealed class AuthError
    {
        public AuthErrorCode Code { get; }
        public string Message { get; }
        public string ProviderId { get; }
        public PlayFabError PlayFabError { get; }

        private AuthError(AuthErrorCode code, string providerId, string message, PlayFabError playFabError = null)
        {
            Code = code;
            ProviderId = providerId;
            Message = message ?? string.Empty;
            PlayFabError = playFabError;
        }

        public static AuthError Cancelled(string providerId, string message = null)
        {
            return new AuthError(AuthErrorCode.Cancelled, providerId, message ?? "Sign-in was cancelled.");
        }

        public static AuthError Configuration(string providerId, string message)
        {
            return new AuthError(AuthErrorCode.Configuration, providerId, message);
        }

        public static AuthError Native(string providerId, string message)
        {
            return new AuthError(AuthErrorCode.Native, providerId, message);
        }

        public static AuthError Unsupported(string providerId, string message)
        {
            return new AuthError(AuthErrorCode.UnsupportedPlatform, providerId, message);
        }

        public static AuthError InProgress(string providerId)
        {
            return new AuthError(AuthErrorCode.InProgress, providerId, "A sign-in is already in progress.");
        }

        public static AuthError FromPlayFab(string providerId, PlayFabError error)
        {
            if (error == null)
                return new AuthError(AuthErrorCode.PlayFab, providerId, "PlayFab login failed.");

            var isNetwork = error.Error == PlayFabErrorCode.ConnectionError
                            || error.HttpCode == 0
                            || error.Error == PlayFabErrorCode.Unknown;

            return new AuthError(
                isNetwork ? AuthErrorCode.Network : AuthErrorCode.PlayFab,
                providerId,
                error.GenerateErrorReport(),
                error);
        }

        public static AuthError FromNativeCode(string providerId, string errorCode, string message)
        {
            switch (errorCode)
            {
                case "cancelled":
                    return Cancelled(providerId, message);
                case "config":
                    return Configuration(providerId, message);
                default:
                    return Native(providerId, message);
            }
        }

        public override string ToString()
        {
            return $"[{Code}] {ProviderId}: {Message}";
        }
    }
}
