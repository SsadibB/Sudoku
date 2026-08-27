using UnityEngine;
using PlayFab;

namespace SadibTools.AuthLogin
{
    [CreateAssetMenu(fileName = "AuthSettings", menuName = "Auth Login/Auth Settings")]
    public class AuthSettings : ScriptableObject
    {
        private const string ResourcesName = "AuthSettings";

        [Header("PlayFab")]
        [SerializeField]
        [Tooltip("Your PlayFab Title ID (e.g. AB12C). Found in Game Manager → Settings → Title Settings. " +
                 "If left blank, the Title ID set in the PlayFab SDK settings asset is used instead.")]
        private string playfabTitleId = string.Empty;

        [Header("Google")]
        [SerializeField]
        [Tooltip("OAuth 2.0 Web application client ID from Google Cloud Console. Must match the client " +
                 "configured in PlayFab Game Manager → Add-ons → Google. Never put the client secret here.")]
        private string googleWebClientId = string.Empty;

        [Header("Facebook / Instagram")]
        [SerializeField]
        [Tooltip("Meta / Facebook App ID from developers.facebook.com. Also required for Instagram " +
                 "(Meta uses Facebook Login). Enable the PlayFab Facebook add-on with the same app.")]
        private string facebookAppId = string.Empty;

        [SerializeField]
        [Tooltip("Facebook Client Token from Meta App Settings → Advanced. This is not the App Secret.")]
        private string facebookClientToken = string.Empty;

        // ── PlayFab ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the PlayFab Title ID for this project.
        /// Priority: AuthSettings field → PlayFabSettings.TitleId (SDK asset).
        /// </summary>
        public string PlayFabTitleId
        {
            get
            {
                string fromSettings = playfabTitleId == null ? string.Empty : playfabTitleId.Trim();
                if (!string.IsNullOrEmpty(fromSettings))
                    return fromSettings;

                // Fall back to the PlayFab SDK's own settings asset so existing projects
                // that already have a Title ID set there continue to work without changes.
                return PlayFabSettings.TitleId;
            }
        }

        // ── Google ───────────────────────────────────────────────────────────────

        public string GoogleWebClientId => googleWebClientId == null ? string.Empty : googleWebClientId.Trim();

        public bool HasGoogleWebClientId => !string.IsNullOrEmpty(GoogleWebClientId);

        // ── Facebook / Instagram ─────────────────────────────────────────────────

        public string FacebookAppId => facebookAppId == null ? string.Empty : facebookAppId.Trim();

        public string FacebookClientToken => facebookClientToken == null ? string.Empty : facebookClientToken.Trim();

        public bool HasFacebookAppId => !string.IsNullOrEmpty(FacebookAppId) && !string.IsNullOrEmpty(FacebookClientToken);

        // ── Helpers ──────────────────────────────────────────────────────────────

        public static AuthSettings LoadFromResources()
        {
            return Resources.Load<AuthSettings>(ResourcesName);
        }
    }
}
