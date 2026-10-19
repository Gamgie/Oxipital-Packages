using System.IO;
using UnityEditor;
using UnityEngine;

namespace Oxipital
{
    // OrbGroup loads its emitter shapes at runtime from StreamingAssets/emitters, and Unity only reads
    // StreamingAssets from the project's Assets folder. The package ships the basic shapes in Emitters~
    // (ignored by Unity) and copies them there : automatically when the project has no emitters folder yet,
    // or from the menu. Existing files are never overwritten.
    [InitializeOnLoad]
    static class EmitterInstaller
    {
        const string MenuPath = "Oxipital/Installer les émetteurs de base";

        static EmitterInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                if (!Directory.Exists(TargetFolder)) Install();
            };
        }

        static string TargetFolder => Path.Combine(Application.streamingAssetsPath, "emitters");

        [MenuItem(MenuPath)]
        static void Install()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(EmitterInstaller).Assembly);
            if (package == null) return;

            string source = Path.Combine(package.resolvedPath, "Emitters~");
            if (!Directory.Exists(source))
            {
                Debug.LogWarning("Oxipital : dossier des émetteurs introuvable dans le package (" + source + ")");
                return;
            }

            Directory.CreateDirectory(TargetFolder);
            int copied = 0;
            foreach (string file in Directory.GetFiles(source, "*.glb"))
            {
                string target = Path.Combine(TargetFolder, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                File.Copy(file, target);
                copied++;
            }

            if (copied > 0) AssetDatabase.Refresh();
            Debug.Log($"Oxipital : {copied} émetteur(s) de base copié(s) dans Assets/StreamingAssets/emitters");
        }
    }
}
