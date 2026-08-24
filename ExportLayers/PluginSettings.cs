using System;
using System.IO;

namespace ExportLayersPlugin
{
    // Remembers the last folder you picked by hand, purely so the dialog can prefill it for
    // documents that have never been saved. Nothing depends on this working, which is why
    // every failure below gets swallowed: a lost prefill is not worth an error message.
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
                // See above.
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
                // And here.
            }
        }
    }
}
