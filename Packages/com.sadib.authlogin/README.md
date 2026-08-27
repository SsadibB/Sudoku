# Auth Login (PlayFab) — `com.sadib.authlogin`

A reusable, self-contained Unity package for authenticating players via **Google Account**, **Facebook**, and **Instagram**, linked seamlessly to **Microsoft PlayFab**.

---

## 🚀 Key Features

- **Single unified entry point**: `AuthManager.Instance.SignInWithGoogle()`, `SignInWithFacebook()`, `SignInWithInstagram()`.
- **Decoupled credentials**: PlayFab Title ID, Google OAuth Web Client ID, Facebook App ID & Client Token all reside in a single ScriptableObject (`AuthSettings.asset`) per project.
- **Zero code changes between projects**: Drop into any game (e.g. Sudoku, endless runner, RPG), plug in credentials, and wire up UI buttons.
- **Clean error handling**: Detailed typed error codes (`Cancelled`, `Configuration`, `PlayFab`, `Network`, `Native`, `UnsupportedPlatform`).
- **Android ready**: Bundles lightweight native Android bridge with EDM (External Dependency Manager) auto-resolution.

---

## 📦 Prerequisites (Per Consuming Game Project)

Before importing `AuthLogin`, ensure the target project has:

1. **PlayFab Unity SDK** installed (`PlayFabSDK`).
2. **External Dependency Manager for Unity (EDM4U)** (for resolving Google Play Services dependencies on Android).
3. **Facebook Unity SDK** installed (if using Facebook / Instagram login) + define `FACEBOOK_SDK` (or `SADIB_AUTH_FACEBOOK_SDK`) in **Project Settings → Player → Scripting Define Symbols**.

---

## 📥 Installation

### Option 1: Via `.unitypackage` (Recommended)
1. In the **AuthKit** project, click **`Auth Login → Export .unitypackage`** from the top Unity menu bar.
2. The export file `AuthLogin_v1.0.0.unitypackage` will be generated in `build/` and revealed in Explorer.
3. In your target project (e.g., Sudoku), select **`Assets → Import Package → Custom Package...`**, select the file, and click **Import**.

### Option 2: Via Unity Package Manager (Local Folder / Git)
- **Local Disk**: Open Unity Package Manager (`Window → Package Manager`), click `+` → `Add package from disk...`, and select `package.json` inside `Packages/com.sadib.authlogin`.
- **Git URL**: Click `+` → `Add package from git URL...` and paste your repository URL.

---

## ⚙️ Project Configuration (`AuthSettings`)

1. In your project's `Assets/Resources/` folder, right-click and choose:
   **`Create → Auth Login → Auth Settings`** (Name the asset `AuthSettings`).
2. Select the asset in the Inspector and populate your project-specific credentials:

| Section | Field | Description |
|---|---|---|
| **PlayFab** | `PlayFab Title ID` | *(Optional)* PlayFab Title ID. If blank, uses PlayFab SDK settings. |
| **Google** | `Google Web Client ID` | OAuth 2.0 **Web application** Client ID from Google Cloud Console. |
| **Facebook / Instagram** | `Facebook App ID` | Meta App ID from `developers.facebook.com`. |
| **Facebook / Instagram** | `Facebook Client Token` | Meta Client Token from App Settings → Advanced. |

---

## 🛠️ One-Time Platform Setup Per Game

### 1. PlayFab Game Manager
1. Log in to [PlayFab Game Manager](https://developer.playfab.com/).
2. Select your Title → **Add-ons**:
   - **Google**: Enable and paste your Google OAuth **Web application Client ID** and **Client Secret**.
   - **Facebook**: Enable and paste your **Facebook App ID** and **App Secret**.

---

### 2. Google Cloud Console (Google Sign-In)
1. Go to [Google Cloud Console](https://console.cloud.google.com/) → **APIs & Services → Credentials**.
2. **Web Application Client** (Backend credentials):
   - Create an OAuth Client ID of type **Web application**.
   - Copy this Client ID into `AuthSettings` (Google Web Client ID) and PlayFab's Google Add-on.
3. **Android Client** (Client authorization):
   - Create an OAuth Client ID of type **Android**.
   - Set **Package name** = your game's Android Package Name (e.g., `com.yourcompany.game`).
   - Set **SHA-1 certificate fingerprint** from your Keystore (Debug & Release keystores).

---

### 3. Meta for Developers (Facebook & Instagram)
1. Go to [Meta for Developers](https://developers.facebook.com/) → **My Apps**.
2. Create an App with **Facebook Login** product enabled.
3. Under **App Settings → Basic → Android**:
   - Package Name = your game's Android Package Name.
   - Add your **Key Hashes** (base64 SHA-1 hash of your debug and release keystores).
4. Under **App Settings → Advanced**:
   - Copy the **Client Token** into `AuthSettings`.
5. *(For Instagram)*: Meta uses Facebook Login for Instagram integration. The package requests `public_profile` scope to securely link the Meta account to PlayFab.

---

## 💻 Code Usage

### 1. Initialize & Listen for Events

```csharp
using UnityEngine;
using SadibTools.AuthLogin;

public class LoginController : MonoBehaviour
{
    private void Start()
    {
        // Subscribe to authentication events
        AuthManager.Instance.OnLoginStarted += OnLoginStarted;
        AuthManager.Instance.OnLoginSuccess += OnLoginSuccess;
        AuthManager.Instance.OnLoginFailure += OnLoginFailure;
    }

    private void OnDestroy()
    {
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.OnLoginStarted -= OnLoginStarted;
            AuthManager.Instance.OnLoginSuccess -= OnLoginSuccess;
            AuthManager.Instance.OnLoginFailure -= OnLoginFailure;
        }
    }

    private void OnLoginStarted(string providerId)
    {
        Debug.Log($"Connecting via {providerId}...");
    }

    private void OnLoginSuccess(AuthSession session)
    {
        Debug.Log($"Logged in via {session.ProviderId}!");
        Debug.Log($"PlayFab ID: {session.PlayFabId}");
        Debug.Log($"Display Name: {session.DisplayName}");
        Debug.Log($"Email: {session.Email}");
        Debug.Log($"Newly created account: {session.NewlyCreated}");
    }

    private void OnLoginFailure(AuthError error)
    {
        Debug.LogError($"Login failed: [{error.Code}] {error.Message}");
    }
}
```

### 2. Ready-To-Use UI (`AuthLoginUI`)

The package includes a ready-to-use **[AuthLoginUI](file:///c:/Users/Sadib/Documents/GitHub/Sudoku/Packages/com.sadib.authlogin/Runtime/AuthLoginUI.cs)** component:
1. Attach `AuthLoginUI` to your Login/Account panel GameObject.
2. Drag and drop your Google, Facebook, and Instagram **Login Buttons** and **Logout Buttons** into the Inspector fields.
3. (Optional) Assign a `TMP_Text` for **Status Text** and **Account Info Text**.
4. `AuthLoginUI` automatically:
   - Toggles between Login and Logout buttons when you connect or disconnect each provider.
   - Shows "Successfully logged in!", user display name/email, and PlayFab ID.
   - Handles individual logout when clicking a provider's Logout button.

### 3. Triggering Sign In & Sign Out via Script / Inspector

You can wire these directly in the Unity Inspector to UI Button `OnClick()` events on `AuthManager` or `AuthLoginUI`:

```csharp
// Google
AuthManager.Instance.SignInWithGoogle();
AuthManager.Instance.SignOutGoogle();

// Facebook
AuthManager.Instance.SignInWithFacebook();
AuthManager.Instance.SignOutFacebook();

// Instagram
AuthManager.Instance.SignInWithInstagram();
AuthManager.Instance.SignOutInstagram();

// Sign out from all providers
AuthManager.Instance.SignOut();
```

---

## 🔍 Troubleshooting

| Issue | Cause & Fix |
|---|---|
| **Google returns `10: Developer Error` or `12500`** | Mismatch between the Keystore SHA-1 fingerprint and the Android OAuth Client in Google Cloud Console, or the Android Package Name does not match. |
| **Facebook returns "Invalid Key Hash"** | The debug/release keystore key hash added to Meta Developer Portal does not match the APK signature. Generate the base64 hash with `keytool -exportcert -alias <alias> -keystore <keystore> \| openssl sha1 -binary \| openssl base64`. |
| **Facebook / Instagram strings missing on Android build** | The package automatically writes `res/values/strings.xml` on Android pre-build. You can also trigger it manually from **`Auth Login → Write Facebook Android Strings`**. |
| **Google Sign-In fails in Unity Editor** | Native Google Identity Authorization API runs exclusively on Android devices (`#if UNITY_ANDROID && !UNITY_EDITOR`). Test using an Android APK / App Bundle on a real device. |

