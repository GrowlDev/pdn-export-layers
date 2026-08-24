using System;
using System.IO;

namespace ExportLayersPlugin
{
    /// <summary>
    /// Remembers the last explicitly chosen destination folder across sessions, used only as a
    /// dialog prefill when the document has no file path. Stored under %APPDATA%\PdnExportLayers.
    /// </summary>
    internal static class PluginSettings
    {
        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PdnExportLayers",
            "lastfolder.txt");

        public static string TryGetLastCustomFolder()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string folder = File.ReadAllText(path).Trim();
                    if (folder.Length > 0 && Path.IsPathRooted(folder))
                    {
                        return folder;
                    }
                }
            }
            catch
            {
                // Prefill convenience only; never let it break the dialog.
            }
            return null;
        }

        public static void SaveLastCustomFolder(string folder)
        {
            try
            {
                string path = SettingsFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, folder ?? string.Empty);
            }
            catch
            {
            }
        }
    }
}
