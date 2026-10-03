using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    public sealed class AuthSession
    {
        public string PlayFabId { get; }
        public string ProviderId { get; }
        public string Email { get; }
        public string DisplayName { get; }
        public string PhotoUrl { get; }
        public bool NewlyCreated { get; }
        public LoginResult LoginResult { get; }

        public AuthSession(
            string playFabId,
            string providerId,
            string email,
            string displayName,
            string photoUrl,
            bool newlyCreated,
            LoginResult loginResult)
        {
            PlayFabId = playFabId;
            ProviderId = providerId;
            Email = email;
            DisplayName = displayName;
            PhotoUrl = photoUrl;
            NewlyCreated = newlyCreated;
            LoginResult = loginResult;
        }

        public static AuthSession FromLogin(
            string providerId,
            LoginResult result,
            string displayNameOverride = null,
            string photoUrl = null)
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

            if (!string.IsNullOrWhiteSpace(displayNameOverride))
                displayName = displayNameOverride.Trim();

            return new AuthSession(
                result?.PlayFabId,
                providerId,
                email,
                displayName,
                string.IsNullOrWhiteSpace(photoUrl) ? null : photoUrl.Trim(),
                result != null && result.NewlyCreated,
                result);
        }
    }

    /// <summary>
    /// Name and photo captured from the provider during sign-in.
    /// PlayFab's account payload often has neither picture.
    /// </summary>
    internal interface IAccountProfile
    {
        string AccountDisplayName { get; }
        string AccountPhotoUrl { get; }
    }
}
