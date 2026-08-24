using System;
using System.Reflection;
using System.Windows.Forms;

namespace ExportLayersPlugin
{
    /// <summary>
    /// Finds the active document's .pdn file path by reflecting over Paint.NET's UI
    /// (AppWorkspace.ActiveDocumentWorkspace.FilePath). The plugin API does not expose the
    /// path, so this is best-effort: every failure returns null and callers fall back to
    /// asking the user for a folder. Verified against Paint.NET 5.1.12.
    /// </summary>
    public static class DocumentPathFinder
    {
        public static string TryGetActiveDocumentPath()
        {
            try
            {
                foreach (Form form in Application.OpenForms)
                {
                    string path;
                    if (form.InvokeRequired)
                    {
                        // Marshal to the UI thread, but never block indefinitely: if the UI
                        // thread is not pumping, give up and let the caller fall back.
                        IAsyncResult asyncResult = form.BeginInvoke(new Func<Form, string>(TryGetPathFromForm), form);
                        if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                        {
                            continue;
                        }
                        path = (string)form.EndInvoke(asyncResult);
                    }
                    else
                    {
                        path = TryGetPathFromForm(form);
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Fall through: reflection into app internals must never break the export.
            }
            return null;
        }

        private static string TryGetPathFromForm(Form form)
        {
            try
            {
                Control appWorkspace = FindControlByTypeName(form, "PaintDotNet.Controls.AppWorkspace");
                if (appWorkspace == null)
                {
                    return null;
                }

                PropertyInfo activeProp = appWorkspace.GetType().GetProperty(
                    "ActiveDocumentWorkspace",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object documentWorkspace = activeProp?.GetValue(appWorkspace);
                if (documentWorkspace == null)
                {
                    return null;
                }

                PropertyInfo filePathProp = documentWorkspace.GetType().GetProperty(
                    "FilePath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return filePathProp?.GetValue(documentWorkspace) as string;
            }
            catch
            {
                return null;
            }
        }

        private static Control FindControlByTypeName(Control root, string fullTypeName)
        {
            if (root.GetType().FullName == fullTypeName)
            {
                return root;
            }
            foreach (Control child in root.Controls)
            {
                Control found = FindControlByTypeName(child, fullTypeName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
