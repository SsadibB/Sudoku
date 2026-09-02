using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    public sealed class AuthSession
    {
        public string PlayFabId { get; }
        public string ProviderId { get; }
        public string Email { get; }
        public string DisplayName { get; }
        public string AvatarUrl { get; }
        public bool NewlyCreated { get; }
        public LoginResult LoginResult { get; }

        public AuthSession(
            string playFabId,
            string providerId,
            string email,
            string displayName,
            string avatarUrl,
            bool newlyCreated,
            LoginResult loginResult)
        {
            PlayFabId = playFabId;
            ProviderId = providerId;
            Email = email;
            DisplayName = displayName;
            AvatarUrl = avatarUrl;
            NewlyCreated = newlyCreated;
            LoginResult = loginResult;
        }

        public static AuthSession FromLogin(string providerId, LoginResult result, string fallbackPhotoUrl = null, string fallbackDisplayName = null)
        {
            string email = null;
            string displayName = null;
            string avatarUrl = fallbackPhotoUrl;

            var account = result?.InfoResultPayload?.AccountInfo;
            if (account != null)
            {
                email = account.PrivateInfo?.Email ?? account.GoogleInfo?.GoogleEmail;
                displayName = account.TitleInfo?.DisplayName
                              ?? account.GoogleInfo?.GoogleName
                              ?? account.FacebookInfo?.FullName;
            }

            var profile = result?.InfoResultPayload?.PlayerProfile;
            if (profile != null)
            {
                if (string.IsNullOrEmpty(displayName))
                    displayName = profile.DisplayName;
                if (!string.IsNullOrEmpty(profile.AvatarUrl))
                    avatarUrl = profile.AvatarUrl;
            }

            if (string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(fallbackDisplayName))
                displayName = fallbackDisplayName;

            if (string.IsNullOrEmpty(avatarUrl) && account?.FacebookInfo != null && !string.IsNullOrEmpty(account.FacebookInfo.FacebookId))
            {
                avatarUrl = $"https://graph.facebook.com/{account.FacebookInfo.FacebookId}/picture?type=large";
            }

            return new AuthSession(
                result?.PlayFabId,
                providerId,
                email,
                displayName,
                avatarUrl,
                result != null && result.NewlyCreated,
                result);
        }
    }
}
