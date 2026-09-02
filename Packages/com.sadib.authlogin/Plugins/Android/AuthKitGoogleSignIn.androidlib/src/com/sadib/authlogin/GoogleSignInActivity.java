package com.sadib.authlogin;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;

import com.google.android.gms.auth.api.signin.GoogleSignIn;
import com.google.android.gms.auth.api.signin.GoogleSignInAccount;
import com.google.android.gms.auth.api.signin.GoogleSignInClient;
import com.google.android.gms.auth.api.signin.GoogleSignInOptions;
import com.google.android.gms.common.api.ApiException;
import com.google.android.gms.common.api.CommonStatusCodes;
import com.google.android.gms.tasks.Task;

public final class GoogleSignInActivity extends Activity {
    public static final String EXTRA_WEB_CLIENT_ID = "webClientId";
    public static final String EXTRA_SILENT = "silent";

    private static final int REQUEST_SIGN_IN = 9101;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        String webClientId = getIntent().getStringExtra(EXTRA_WEB_CLIENT_ID);
        boolean silent = getIntent().getBooleanExtra(EXTRA_SILENT, false);

        if (webClientId == null || webClientId.length() == 0) {
            failAndFinish("config", "Google Web Client ID is empty.");
            return;
        }

        GoogleSignInOptions gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DEFAULT_SIGN_IN)
                .requestServerAuthCode(webClientId)
                .requestEmail()
                .requestProfile()
                .build();

        GoogleSignInClient client = GoogleSignIn.getClient(this, gso);

        if (silent) {
            client.silentSignIn()
                    .addOnCompleteListener(this, this::handleSignInResult);
        } else {
            // Sign out first so the account picker shows cleanly
            client.signOut().addOnCompleteListener(this, task -> {
                Intent signInIntent = client.getSignInIntent();
                startActivityForResult(signInIntent, REQUEST_SIGN_IN);
            });
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_SIGN_IN) {
            return;
        }

        if (resultCode != RESULT_OK || data == null) {
            failAndFinish("cancelled", "User cancelled Google sign-in.");
            return;
        }

        Task<GoogleSignInAccount> task = GoogleSignIn.getSignedInAccountFromIntent(data);
        handleSignInResult(task);
    }

    private void handleSignInResult(Task<GoogleSignInAccount> task) {
        try {
            GoogleSignInAccount account = task.getResult(ApiException.class);
            if (account == null) {
                failAndFinish("native", "Google sign-in account is null.");
                return;
            }

            String serverAuthCode = account.getServerAuthCode();
            if (serverAuthCode == null || serverAuthCode.length() == 0) {
                failAndFinish("native", "Google returned an empty server auth code.");
                return;
            }

            String displayName = account.getDisplayName() != null ? account.getDisplayName() : "";
            Uri photoUri = account.getPhotoUrl();
            String photoUrl = photoUri != null ? photoUri.toString() : "";
            String email = account.getEmail() != null ? account.getEmail() : "";

            GoogleSignInBridge.deliverSuccess(serverAuthCode, displayName, photoUrl, email);
            finish();
        } catch (ApiException e) {
            if (e.getStatusCode() == CommonStatusCodes.CANCELED || e.getStatusCode() == 12501) {
                failAndFinish("cancelled", "User cancelled Google sign-in.");
            } else {
                failAndFinish("native", e.getStatusCode() + ": " + safeMessage(e));
            }
        } catch (Exception e) {
            failAndFinish("native", safeMessage(e));
        }
    }

    private void failAndFinish(String errorCode, String message) {
        GoogleSignInBridge.deliverError(errorCode, message);
        finish();
    }

    private static String safeMessage(Exception e) {
        if (e == null || e.getMessage() == null) {
            return "Google sign-in failed.";
        }
        return e.getMessage();
    }
}
