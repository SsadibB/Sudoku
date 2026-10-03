package com.sadib.authlogin;

import android.app.Activity;
import android.content.Intent;
import android.content.IntentSender;
import android.os.Bundle;

import com.google.android.gms.auth.api.identity.AuthorizationRequest;
import com.google.android.gms.auth.api.identity.AuthorizationResult;
import com.google.android.gms.auth.api.identity.Identity;
import com.google.android.gms.auth.api.signin.GoogleSignInAccount;
import com.google.android.gms.common.api.ApiException;
import com.google.android.gms.common.api.CommonStatusCodes;
import com.google.android.gms.common.api.Scope;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.Arrays;

public final class GoogleSignInActivity extends Activity {
    public static final String EXTRA_WEB_CLIENT_ID = "webClientId";
    public static final String EXTRA_SILENT = "silent";
    public static final String EXTRA_REQUEST_ID = "requestId";

    private static final int REQUEST_AUTHORIZE = 9101;

    private int requestId;
    private boolean completed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        if (savedInstanceState != null) {
            requestId = savedInstanceState.getInt(EXTRA_REQUEST_ID, 0);
            return;
        }

        requestId = getIntent().getIntExtra(EXTRA_REQUEST_ID, 0);
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

        succeedAndFinish(result);
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
            succeedAndFinish(result);
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

    private void succeedAndFinish(final AuthorizationResult result) {
        final String serverAuthCode = result != null ? result.getServerAuthCode() : null;
        if (serverAuthCode == null || serverAuthCode.length() == 0) {
            failAndFinish("native", "Google returned an empty server auth code.");
            return;
        }

        String accountName = "";
        String accountPhoto = "";
        try {
            GoogleSignInAccount account = result.toGoogleSignInAccount();
            if (account != null) {
                if (account.getDisplayName() != null)
                    accountName = account.getDisplayName();
                if (account.getPhotoUrl() != null)
                    accountPhoto = account.getPhotoUrl().toString();
            }
        } catch (Exception ignored) {
        }

        final String knownName = accountName;
        final String knownPhoto = accountPhoto;
        final String accessToken = result.getAccessToken();
        new Thread(() -> {
            String name = knownName;
            String photo = knownPhoto;
            try {
                if ((name.length() == 0 || photo.length() == 0)
                        && accessToken != null && accessToken.length() > 0) {
                    String json = httpGet("https://www.googleapis.com/oauth2/v3/userinfo", accessToken);
                    JSONObject profile = new JSONObject(json);
                    if (name.length() == 0)
                        name = profile.optString("name", "");
                    if (photo.length() == 0)
                        photo = profile.optString("picture", "");
                }
            } catch (Exception ignored) {
            }

            final String payload = serverAuthCode
                    + "\n" + sanitize(name)
                    + "\n" + sanitize(photo);
            runOnUiThread(() -> {
                if (!markCompleted()) {
                    return;
                }
                GoogleSignInBridge.deliverSuccess(requestId, payload);
                finish();
            });
        }).start();
    }

    private static String httpGet(String url, String accessToken) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
        connection.setRequestMethod("GET");
        connection.setConnectTimeout(8000);
        connection.setReadTimeout(8000);
        connection.setRequestProperty("Authorization", "Bearer " + accessToken);
        int status = connection.getResponseCode();
        InputStream stream = status >= 200 && status < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
        String body = readAll(stream);
        connection.disconnect();
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("userinfo " + status);
        }
        return body;
    }

    private static String readAll(InputStream stream) throws Exception {
        if (stream == null) {
            return "";
        }
        BufferedReader reader = new BufferedReader(new InputStreamReader(stream));
        StringBuilder builder = new StringBuilder();
        String line;
        while ((line = reader.readLine()) != null) {
            builder.append(line);
        }
        reader.close();
        return builder.toString();
    }

    private static String sanitize(String value) {
        if (value == null) {
            return "";
        }
        return value.replace("\n", " ").replace("\r", " ").trim();
    }

    private void failAndFinish(String errorCode, String message) {
        if (!markCompleted()) {
            return;
        }
        GoogleSignInBridge.deliverError(requestId, errorCode, message);
        finish();
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        outState.putInt(EXTRA_REQUEST_ID, requestId);
    }

    @Override
    protected void onDestroy() {
        if (!completed && !isChangingConfigurations()) {
            completed = true;
            GoogleSignInBridge.deliverError(requestId, "cancelled", "Google sign-in was interrupted.");
        }
        super.onDestroy();
    }

    private boolean markCompleted() {
        if (completed) {
            return false;
        }
        completed = true;
        return true;
    }

    private static String safeMessage(Exception e) {
        if (e == null || e.getMessage() == null) {
            return "Google sign-in failed.";
        }
        return e.getMessage();
    }
}
