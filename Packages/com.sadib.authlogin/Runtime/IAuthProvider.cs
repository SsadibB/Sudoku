using System;
using PlayFab.ClientModels;

namespace SadibTools.AuthLogin
{
    /// <summary>
    /// Contract every login provider implements (Google today, Apple/Facebook/etc later).
    /// AuthManager talks to providers only through this interface, so adding a new
    /// provider never changes the API your game code calls.
    /// </summary>
    public interface IAuthProvider
    {
        /// <summary>Short id used for logging / provider selection, e.g. "google".</summary>
        string ProviderId { get; }

        bool IsSignedIn { get; }

        /// <param name="silent">If true, never show provider UI — fail quietly if it can't sign in without prompting.</param>
        void SignIn(bool silent, Action<LoginResult> onSuccess, Action<AuthError> onFailure);

        void SignOut();
    }
}
