package com.sadib.authlogin;

import android.app.Activity;
import android.content.Intent;
import android.content.IntentSender;
import android.os.Bundle;

import com.google.android.gms.auth.api.identity.AuthorizationRequest;
import com.google.android.gms.auth.api.identity.AuthorizationResult;
import com.google.android.gms.auth.api.identity.Identity;
import com.google.android.gms.common.api.ApiException;
import com.google.android.gms.common.api.CommonStatusCodes;
import com.google.android.gms.common.api.Scope;

import java.util.Arrays;

public final class GoogleSignInActivity extends Activity {
    public static final String EXTRA_WEB_CLIENT_ID = "webClientId";
    public static final String EXTRA_SILENT = "silent";

    private static final int REQUEST_AUTHORIZE = 9101;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        String webClientId = getIntent().getStringExtra(EXTRA_WEB_CLIENT_ID);
        boolean silent = getIntent().getBooleanExtra(EXTRA_SILENT, false);

        if (webClientId == null || webClientId.length() == 0) {
            failAndFinish("config", "Google Web Client ID is empty.");
            return;
        }

        AuthorizationRequest request = AuthorizationRequest.builder()
                .setRequestedScopes(Arrays.asList(
                        new Scope("email"),
                        new Scope("profile"),
                        new Scope("openid")))
                .requestOfflineAccess(webClientId, !silent)
                .build();

        Identity.getAuthorizationClient(this)
                .authorize(request)
                .addOnSuccessListener(result -> handleAuthorizationResult(result, silent))
                .addOnFailureListener(e -> failAndFinish("native", safeMessage(e)));
    }

    private void handleAuthorizationResult(AuthorizationResult result, boolean silent) {
        if (result.hasResolution()) {
            if (silent) {
                failAndFinish("cancelled", "Silent Google sign-in requires prior consent.");
                return;
            }

            try {
                startIntentSenderForResult(
                        result.getPendingIntent().getIntentSender(),
                        REQUEST_AUTHORIZE,
                        null,
                        0,
                        0,
                        0);
            } catch (IntentSender.SendIntentException e) {
                failAndFinish("native", safeMessage(e));
            }
            return;
        }

        succeedAndFinish(result.getServerAuthCode());
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_AUTHORIZE) {
            return;
        }

        if (resultCode != RESULT_OK || data == null) {
            failAndFinish("cancelled", "User cancelled Google sign-in.");
            return;
        }

        try {
            AuthorizationResult result = Identity.getAuthorizationClient(this)
                    .getAuthorizationResultFromIntent(data);
            succeedAndFinish(result.getServerAuthCode());
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

    private void succeedAndFinish(String serverAuthCode) {
        if (serverAuthCode == null || serverAuthCode.length() == 0) {
            failAndFinish("native", "Google returned an empty server auth code.");
            return;
        }

        GoogleSignInBridge.deliverSuccess(serverAuthCode);
        finish();
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
