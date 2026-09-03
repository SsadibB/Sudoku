package com.sadib.authlogin;

import android.app.Activity;
import android.content.Intent;

import com.google.android.gms.auth.api.signin.GoogleSignIn;
import com.google.android.gms.auth.api.signin.GoogleSignInOptions;

public final class GoogleSignInBridge {
    public interface Listener {
        void onSuccess(String serverAuthCode);

        void onError(String errorCode, String message);
    }

    static Listener listener;

    private GoogleSignInBridge() {
    }

    public static void requestServerAuthCode(Activity activity, String webClientId, boolean silent, Listener callback) {
        listener = callback;
        Intent intent = new Intent(activity, GoogleSignInActivity.class);
        intent.putExtra(GoogleSignInActivity.EXTRA_WEB_CLIENT_ID, webClientId);
        intent.putExtra(GoogleSignInActivity.EXTRA_SILENT, silent);
        activity.startActivity(intent);
    }

    public static void signOut(Activity activity) {
        try {
            GoogleSignIn.getClient(activity, GoogleSignInOptions.DEFAULT_SIGN_IN).signOut();
        } catch (Exception ignored) {
        }
        listener = null;
    }

    static void deliverSuccess(String serverAuthCode) {
        Listener callback = listener;
        listener = null;
        if (callback != null) {
            callback.onSuccess(serverAuthCode);
        }
    }

    static void deliverError(String errorCode, String message) {
        Listener callback = listener;
        listener = null;
        if (callback != null) {
            callback.onError(errorCode, message);
        }
    }
}
