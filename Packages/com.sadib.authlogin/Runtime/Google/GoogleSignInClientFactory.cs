namespace SadibTools.AuthLogin
{
    internal static class GoogleSignInClientFactory
    {
        public static IGoogleSignInClient Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidGoogleSignInClient();
#else
            return new UnsupportedGoogleSignInClient();
#endif
        }
    }
}
