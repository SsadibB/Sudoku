using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Cross-platform clipboard helper that handles Android native clipboard via JNI,
/// GUIUtility.systemCopyBuffer, and in-memory session caching for maximum reliability on mobile.
/// </summary>
public static class ClipboardHelper
{
    private static string lastCopiedRoomCode = "";

    /// <summary>
    /// Gets or sets the cached in-memory room code.
    /// </summary>
    public static string LastCopiedRoomCode
    {
        get => lastCopiedRoomCode;
        set => lastCopiedRoomCode = value;
    }

    /// <summary>
    /// Copies text to Android native clipboard, Unity systemCopyBuffer, and in-memory cache.
    /// </summary>
    public static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        lastCopiedRoomCode = text.Trim();
        GUIUtility.systemCopyBuffer = lastCopiedRoomCode;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity != null)
                {
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            using (var clipboardService = activity.Call<AndroidJavaObject>("getSystemService", "clipboard"))
                            using (var clipDataClass = new AndroidJavaClass("android.content.ClipData"))
                            using (var clipData = clipDataClass.CallStatic<AndroidJavaObject>("newPlainText", "SudokuRoomCode", text))
                            {
                                if (clipboardService != null && clipData != null)
                                {
                                    clipboardService.Call("setPrimaryClip", clipData);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ClipboardHelper] Android setPrimaryClip error: {ex.Message}");
                        }
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ClipboardHelper] Android runOnUiThread error: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// Reads text from the clipboard (Android native JNI, Unity systemCopyBuffer, or session fallback).
    /// </summary>
    public static string GetFromClipboard()
    {
        string text = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity != null)
                {
                    var waitHandle = new System.Threading.ManualResetEvent(false);
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            using (var clipboardService = activity.Call<AndroidJavaObject>("getSystemService", "clipboard"))
                            {
                                if (clipboardService != null && clipboardService.Call<bool>("hasPrimaryClip"))
                                {
                                    using (var clipData = clipboardService.Call<AndroidJavaObject>("getPrimaryClip"))
                                    {
                                        if (clipData != null && clipData.Call<int>("getItemCount") > 0)
                                        {
                                            using (var item = clipData.Call<AndroidJavaObject>("getItemAt", 0))
                                            using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                                            {
                                                var charSeq = item.Call<AndroidJavaObject>("coerceToText", context);
                                                if (charSeq != null)
                                                {
                                                    text = charSeq.Call<string>("toString");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ClipboardHelper] Android getPrimaryClip error: {ex.Message}");
                        }
                        finally
                        {
                            waitHandle.Set();
                        }
                    }));
                    // Wait up to 150ms so Unity main thread is not blocked
                    waitHandle.WaitOne(150);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ClipboardHelper] Android clipboard read error: {ex.Message}");
        }
#endif

        if (string.IsNullOrEmpty(text))
        {
            text = GUIUtility.systemCopyBuffer;
        }

        if (string.IsNullOrEmpty(text))
        {
            text = lastCopiedRoomCode;
        }

        return text;
    }

    /// <summary>
    /// Sanitizes and extracts a 6-character room code from arbitrary clipboard text.
    /// If text contains an explicit 6-char alphanumeric code (e.g. from a shared message), extracts it.
    /// Otherwise trims and returns uppercase.
    /// </summary>
    public static string ExtractRoomCode(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string trimmed = input.Trim().ToUpper();

        // Exact match of 6 alphanumeric characters
        if (trimmed.Length == 6 && Regex.IsMatch(trimmed, "^[A-Z0-9]{6}$"))
        {
            return trimmed;
        }

        // If clipboard contains a longer string (e.g. "Join code: AB12CD"), look for 6-char word
        var match = Regex.Match(trimmed, @"\b[A-Z0-9]{6}\b");
        if (match.Success)
        {
            return match.Value;
        }

        // Fallback: take up to 6 alphanumeric characters
        string filtered = Regex.Replace(trimmed, @"[^A-Z0-9]", "");
        if (filtered.Length > 6)
        {
            filtered = filtered.Substring(0, 6);
        }

        return filtered;
    }
}
