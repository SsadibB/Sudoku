using System;
using UnityEngine;

namespace SadibTools.AuthLogin
{
#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidGoogleSignInClient : IGoogleSignInClient
    {
        private const string BridgeClass = "com.sadib.authlogin.GoogleSignInBridge";
        private const string ProviderId = "google";

        private ListenerProxy _listener;

        public bool IsSupported => true;

        public void RequestServerAuthCode(
            string webClientId,
            bool silent,
            Action<string> onSuccess,
            Action<AuthError> onFailure)
        {
            if (string.IsNullOrEmpty(webClientId))
            {
                onFailure?.Invoke(AuthError.Configuration(
                    ProviderId,
                    "Google Web Client ID is missing. Set it on AuthSettings (OAuth Web application client, not the Android client)."));
                return;
            }

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(BridgeClass))
                {
                    _listener = new ListenerProxy(this, onSuccess, onFailure);
                    bridge.CallStatic("requestServerAuthCode", activity, webClientId, silent, _listener);
                }
            }
            catch (Exception ex)
            {
                _listener = null;
                onFailure?.Invoke(AuthError.Native(ProviderId, ex.Message));
            }
        }

        public void SignOut()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(BridgeClass))
                {
                    bridge.CallStatic("signOut", activity);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AndroidGoogleSignInClient] Native sign-out failed: " + ex.Message);
            }
        }

        private void ClearListener()
        {
            _listener = null;
        }

        private sealed class ListenerProxy : AndroidJavaProxy
        {
            private readonly AndroidGoogleSignInClient _owner;
            private readonly Action<string> _onSuccess;
            private readonly Action<AuthError> _onFailure;

            public ListenerProxy(
                AndroidGoogleSignInClient owner,
                Action<string> onSuccess,
                Action<AuthError> onFailure)
                : base(BridgeClass + "$Listener")
            {
                _owner = owner;
                _onSuccess = onSuccess;
                _onFailure = onFailure;
            }

            public void onSuccess(string serverAuthCode)
            {
                AuthMainThread.Post(() =>
                {
                    _owner.ClearListener();
                    if (string.IsNullOrEmpty(serverAuthCode))
                    {
                        _onFailure?.Invoke(AuthError.Native(ProviderId, "Google returned an empty server auth code."));
                        return;
                    }

                    _onSuccess?.Invoke(serverAuthCode);
                });
            }

            public void onError(string errorCode, string message)
            {
                AuthMainThread.Post(() =>
                {
                    _owner.ClearListener();
                    _onFailure?.Invoke(AuthError.FromNativeCode(ProviderId, errorCode, message));
                });
            }
        }
    }
#endif
}
