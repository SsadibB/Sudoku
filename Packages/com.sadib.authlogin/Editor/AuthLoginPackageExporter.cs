using System.IO;
using UnityEditor;
using UnityEngine;

namespace SadibTools.AuthLogin.Editor
{
    /// <summary>
    /// Exports com.sadib.authlogin as a .unitypackage file ready to import into any Unity project.
    /// Run via:  Auth Login -> Export .unitypackage
    /// Output:   [project root]/build/AuthLogin_v{version}.unitypackage
    /// </summary>
    public static class AuthLoginPackageExporter
    {
        private const string PackagePath    = "Packages/com.sadib.authlogin";
        private const string PackageVersion = "1.0.0";
        private const string OutputFileName = "AuthLogin_v" + PackageVersion + ".unitypackage";

        [MenuItem("Auth Login/Export .unitypackage")]
        public static void ExportPackage()
        {
            // 1. Resolve output folder
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildFolder = Path.Combine(projectRoot, "build");
            Directory.CreateDirectory(buildFolder);

            string outputPath = Path.Combine(buildFolder, OutputFileName);

            // 2. Export
            // ExportPackageOptions.Recurse includes all files in sub-folders.
            // ExportPackageOptions.IncludeDependencies is intentionally omitted so
            // the consuming project installs its own PlayFab SDK and Facebook SDK.
            AssetDatabase.ExportPackage(
                PackagePath,
                outputPath,
                ExportPackageOptions.Recurse
            );

            // 3. Confirm and open folder
            Debug.Log("[AuthLogin] Exported to: " + outputPath);
            bool openFolder = EditorUtility.DisplayDialog(
                "AuthLogin Package Exported",
                "Saved to:\n" + outputPath + "\n\n" +
                "Import this file into any Unity project via:\n" +
                "Assets -> Import Package -> Custom Package",
                "Open Folder",
                "Close"
            );

            if (openFolder)
                EditorUtility.RevealInFinder(outputPath);
        }
    }
}
