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
    static int nextRequestId;
    static int activeRequestId = -1;

    private GoogleSignInBridge() {
    }

    public static void requestServerAuthCode(Activity activity, String webClientId, boolean silent, Listener callback) {
        int requestId = ++nextRequestId;
        activeRequestId = requestId;
        listener = callback;
        Intent intent = new Intent(activity, GoogleSignInActivity.class);
        intent.putExtra(GoogleSignInActivity.EXTRA_WEB_CLIENT_ID, webClientId);
        intent.putExtra(GoogleSignInActivity.EXTRA_SILENT, silent);
        intent.putExtra(GoogleSignInActivity.EXTRA_REQUEST_ID, requestId);
        activity.startActivity(intent);
    }

    public static void signOut(Activity activity) {
        activeRequestId = -1;
        listener = null;
        try {
            GoogleSignIn.getClient(activity, GoogleSignInOptions.DEFAULT_SIGN_IN).signOut();
        } catch (Exception ignored) {
        }
    }

    static void deliverSuccess(int requestId, String serverAuthCode) {
        if (requestId != activeRequestId) {
            return;
        }
        Listener callback = listener;
        listener = null;
        activeRequestId = -1;
        if (callback != null) {
            callback.onSuccess(serverAuthCode);
        }
    }

    static void deliverError(int requestId, String errorCode, String message) {
        if (requestId != activeRequestId) {
            return;
        }
        Listener callback = listener;
        listener = null;
        activeRequestId = -1;
        if (callback != null) {
            callback.onError(errorCode, message);
        }
    }
}
