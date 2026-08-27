using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SadibTools.AuthLogin.Editor
{
    /// <summary>
    /// Writes Facebook App ID / Client Token from AuthSettings into the Android library strings.xml
    /// so the Facebook SDK can register the fb{appId} custom-tab scheme.
    /// Works whether the package is installed via UPM (Packages/) or imported as a .unitypackage (Assets/).
    /// </summary>
    internal sealed class AuthLoginFacebookAndroidConfig : IPreprocessBuildWithReport
    {
        private const string AndroidLibName = "AuthKitGoogleSignIn.androidlib";
        private const string StringsRelativePath = "res/values/strings.xml";

        // Fallback used only if AssetDatabase lookup fails (should not happen in practice).
        private const string FallbackStringsPath =
            "Packages/com.sadib.authlogin/Plugins/Android/AuthKitGoogleSignIn.androidlib/res/values/strings.xml";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            WriteStrings(AuthSettings.LoadFromResources());
        }

        [MenuItem("Auth Login/Write Facebook Android Strings")]
        private static void WriteFromMenu()
        {
            WriteStrings(AuthSettings.LoadFromResources());
        }

        internal static void WriteStrings(AuthSettings settings)
        {
            string appId = settings != null ? settings.FacebookAppId : string.Empty;
            string clientToken = settings != null ? settings.FacebookClientToken : string.Empty;
            if (string.IsNullOrEmpty(appId))
                appId = "0";
            if (string.IsNullOrEmpty(clientToken))
                clientToken = "placeholder";

            string scheme = "fb" + appId;
            var xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.AppendLine("<resources>");
            xml.AppendLine("    <string name=\"facebook_app_id\">" + EscapeXml(appId) + "</string>");
            xml.AppendLine("    <string name=\"facebook_client_token\">" + EscapeXml(clientToken) + "</string>");
            xml.AppendLine("    <string name=\"fb_login_protocol_scheme\">" + EscapeXml(scheme) + "</string>");
            xml.AppendLine("</resources>");

            string stringsPath = ResolveStringsPath();
            string fullPath = Path.GetFullPath(stringsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, xml.ToString());
            Debug.Log("[AuthLogin] Wrote Facebook Android strings to: " + fullPath + " (App ID: " + appId + ")");
        }

        /// <summary>
        /// Resolves the strings.xml path dynamically so this works for both:
        ///   - UPM installs:          Packages/com.sadib.authlogin/Plugins/Android/...
        ///   - .unitypackage imports: Assets/[AnyFolder]/Plugins/Android/...
        /// </summary>
        private static string ResolveStringsPath()
        {
            // 1. Check direct standard UPM path
            string upmFullPath = Path.GetFullPath(FallbackStringsPath);
            if (File.Exists(upmFullPath) || Directory.Exists(Path.GetDirectoryName(upmFullPath)))
            {
                return FallbackStringsPath;
            }

            // 2. Search filesystem in Assets and Packages
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                foreach (string searchDir in new[] { Path.Combine(projectRoot, "Assets"), Path.Combine(projectRoot, "Packages") })
                {
                    if (Directory.Exists(searchDir))
                    {
                        string[] matchingDirs = Directory.GetDirectories(searchDir, AndroidLibName, SearchOption.AllDirectories);
                        if (matchingDirs.Length > 0)
                        {
                            return Path.Combine(matchingDirs[0], StringsRelativePath);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[AuthLogin] Disk search for " + AndroidLibName + ": " + ex.Message);
            }

            // 3. AssetDatabase fallback
            string[] guids = AssetDatabase.FindAssets("project.properties");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.Contains(AndroidLibName))
                {
                    int lastSlash = assetPath.LastIndexOf('/');
                    string libRoot = assetPath.Substring(0, lastSlash);
                    return libRoot + "/" + StringsRelativePath;
                }
            }

            return FallbackStringsPath;
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
