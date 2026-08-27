namespace SadibTools.AuthLogin
{
    internal static class FacebookSignInClientFactory
    {
        public static IFacebookSignInClient Create(string providerId)
        {
#if (FACEBOOK_SDK || SADIB_AUTH_FACEBOOK_SDK) && !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            return new UnityFacebookSignInClient(providerId);
#else
            return new UnsupportedFacebookSignInClient(providerId);
#endif
        }
    }
}
