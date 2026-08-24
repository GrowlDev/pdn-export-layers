using System;
using System.Reflection;
using System.Windows.Forms;

namespace ExportLayersPlugin
{
    // Gets the open document's .pdn path, which the plugin API flatly refuses to tell you.
    // I couldn't find a supported route to it, so this walks the open forms looking for
    // AppWorkspace and then reflects its way down to ActiveDocumentWorkspace.FilePath.
    //
    // It's grubby, and it's the only reason "export into a folder next to the .pdn" works.
    // Checked against 5.1.12. If a later version renames any of that we return null, the
    // dialog asks for a folder the same way it already does for an unsaved document, and the
    // plugin carries on being less convenient rather than broken.
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
                        // Not a plain Invoke(): if the UI thread isn't pumping we'd sit here
                        // forever. Five seconds is made up, but it is a very long time next to
                        // how long this takes when it's going to work at all.
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
                // We are rummaging around in someone else's internals, so assume anything in
                // here can throw, and that none of it is worth killing the export over.
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
