using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    public sealed class AuthSession
    {
        public string PlayFabId { get; }
        public string ProviderId { get; }
        public string Email { get; }
        public string DisplayName { get; }
        public bool NewlyCreated { get; }
        public LoginResult LoginResult { get; }

        public AuthSession(
            string playFabId,
            string providerId,
            string email,
            string displayName,
            bool newlyCreated,
            LoginResult loginResult)
        {
            PlayFabId = playFabId;
            ProviderId = providerId;
            Email = email;
            DisplayName = displayName;
            NewlyCreated = newlyCreated;
            LoginResult = loginResult;
        }

        public static AuthSession FromLogin(string providerId, LoginResult result)
        {
            string email = null;
            string displayName = null;

            var account = result?.InfoResultPayload?.AccountInfo;
            if (account != null)
            {
                email = account.PrivateInfo?.Email ?? account.GoogleInfo?.GoogleEmail;
                displayName = account.TitleInfo?.DisplayName
                              ?? account.GoogleInfo?.GoogleName
                              ?? account.FacebookInfo?.FullName;
            }

            if (string.IsNullOrEmpty(displayName))
                displayName = result?.InfoResultPayload?.PlayerProfile?.DisplayName;

            return new AuthSession(
                result?.PlayFabId,
                providerId,
                email,
                displayName,
                result != null && result.NewlyCreated,
                result);
        }
    }
}
