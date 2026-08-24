using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using PaintDotNet.Rendering;

namespace ExportLayersPlugin
{
    /// <summary>Problems the user should read as a sentence in a message box, not a stack trace.</summary>
    public sealed class ExportException : Exception
    {
        public ExportException(string message) : base(message) { }
        public ExportException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ExportResult
    {
        public string Folder;
        public List<string> Files = new List<string>();
        public int SkippedHidden;
    }

    /// <summary>A BGRA32 view of one layer. Only valid until you dispose it, so don't hang onto it.</summary>
    public sealed class LockedPixels : IDisposable
    {
        private Action disposeAction;

        public LockedPixels(IntPtr buffer, int stride, Action disposeAction)
        {
            Buffer = buffer;
            Stride = stride;
            this.disposeAction = disposeAction;
        }

        public IntPtr Buffer { get; }
        public int Stride { get; }

        public void Dispose()
        {
            Action action = disposeAction;
            disposeAction = null;
            action?.Invoke();
        }
    }

    // Deliberately knows nothing about Paint.NET. That is what lets the test harness push fake
    // layers through exactly the same code the real thing runs.
    public sealed class SourceLayer
    {
        public string Name;
        public bool Visible;
        public Func<LockedPixels> OpenPixels;
    }

    public static class LayerExporter
    {
        /// <summary>Pulls the layers out of the open document and hands them to ExportCore.</summary>
        public static ExportResult Export(IEffectEnvironment environment, ExportLayersConfigToken token)
        {
            if (environment == null)
            {
                throw new ExportException("The effect environment is not available; this plugin requires Paint.NET 5.0 or newer.");
            }

            IEffectDocumentInfo document = environment.Document;
            SizeInt32 size = document.Size;

            var layers = new List<SourceLayer>();
            foreach (IEffectLayerInfo layerInfo in document.Layers)
            {
                IEffectLayerInfo capturedLayer = layerInfo;
                layers.Add(new SourceLayer
                {
                    Name = capturedLayer.Name,
                    Visible = capturedLayer.Visible,
                    OpenPixels = () =>
                    {
                        IEffectInputBitmap bitmap = capturedLayer.GetBitmap(PixelFormats.Bgra32);
                        IBitmapLock bitmapLock = bitmap.Lock(new RectInt32(0, 0, size.Width, size.Height));
                        unsafe
                        {
                            return new LockedPixels(
                                (IntPtr)bitmapLock.Buffer,
                                bitmapLock.BufferStride,
                                () =>
                                {
                                    bitmapLock.Dispose();
                                    bitmap.Dispose();
                                });
                        }
                    },
                });
            }

            return ExportCore(size.Width, size.Height, layers, token);
        }

        // The actual work: where the files go, what they end up called, writing them out.
        // No Paint.NET types anywhere in here, which is on purpose. See SourceLayer.
        public static ExportResult ExportCore(int width, int height, IReadOnlyList<SourceLayer> layers, ExportLayersConfigToken token)
        {
            string folder = ResolveDestinationFolder(token);
            Directory.CreateDirectory(folder);

            ExportResult result = new ExportResult { Folder = folder };
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SourceLayer layer in layers)
            {
                if (token.VisibleLayersOnly && !layer.Visible)
                {
                    result.SkippedHidden++;
                    continue;
                }

                string baseName = SanitizeFileName(layer.Name);
                string fileName = MakeUniqueName(baseName, usedNames, folder, token.OverwriteExisting);
                string path = Path.Combine(folder, fileName + ".png");

                WriteLayerPng(layer, width, height, path);
                result.Files.Add(path);
            }

            if (result.Files.Count == 0)
            {
                throw new ExportException(token.VisibleLayersOnly
                    ? "Nothing was exported: the document has no visible layers."
                    : "Nothing was exported: the document has no layers.");
            }

            return result;
        }

        // An explicit folder wins; failing that it's a folder named after the .pdn, sitting
        // next to it. Worked out fresh on every export rather than cached, so that saving the
        // document somewhere else quietly takes the exports with it.
        public static string ResolveDestinationFolder(ExportLayersConfigToken token)
        {
            if (!string.IsNullOrWhiteSpace(token.CustomFolder))
            {
                string custom = token.CustomFolder.Trim();
                if (!Path.IsPathRooted(custom))
                {
                    throw new ExportException($"The destination folder must be a full path, but was \"{custom}\".");
                }
                return custom;
            }

            string autoFolder = TryGetAutoFolder();
            if (autoFolder == null)
            {
                throw new ExportException(
                    "No destination folder. Save the document first (the layers are then exported to a folder " +
                    "named after it), or choose a folder in the Export Layers dialog.");
            }
            return autoFolder;
        }

        /// <summary>Folder worked out from the document's path, or null if we couldn't get one.</summary>
        public static string TryGetAutoFolder()
        {
            string docPath = DocumentPathFinder.TryGetActiveDocumentPath();
            if (string.IsNullOrEmpty(docPath))
            {
                return null;
            }

            try
            {
                string dir = Path.GetDirectoryName(docPath);
                string name = Path.GetFileNameWithoutExtension(docPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
                {
                    return null;
                }
                return Path.Combine(dir, name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static void WriteLayerPng(SourceLayer layer, int width, int height, string path)
        {
            using (LockedPixels src = layer.OpenPixels())
            using (Bitmap gdiBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                BitmapData dstData = gdiBitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        // A row at a time, because the two strides don't have to match and for
                        // odd widths they don't.
                        byte* srcBase = (byte*)src.Buffer;
                        byte* dstBase = (byte*)dstData.Scan0;
                        long rowBytes = (long)width * 4;
                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                srcBase + (long)y * src.Stride,
                                dstBase + (long)y * dstData.Stride,
                                rowBytes,
                                rowBytes);
                        }
                    }
                }
                finally
                {
                    gdiBitmap.UnlockBits(dstData);
                }

                gdiBitmap.Save(path, ImageFormat.Png);
            }
        }

        // ---- naming ----

        // Calling a layer "CON" is a perfectly reasonable thing to do, and Windows will still
        // refuse to give you CON.png. Somehow all of this is still true.
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        public static string SanitizeFileName(string layerName)
        {
            string name = (layerName ?? string.Empty).Trim();

            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 32 || Array.IndexOf(InvalidChars, c) >= 0)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Trailing dots and spaces are out too, whatever Explorer lets you type.
            string cleaned = sb.ToString().TrimEnd('.', ' ');

            if (cleaned.Length == 0)
            {
                cleaned = "layer";
            }
            else if (ReservedNames.Contains(cleaned))
            {
                cleaned += "_";
            }

            // 120 is a guess. It leaves room for a long folder path and a "_12" on the end
            // without getting near MAX_PATH. Not properly correct, just comfortably under.
            if (cleaned.Length > 120)
            {
                cleaned = cleaned.Substring(0, 120).TrimEnd('.', ' ');
            }

            return cleaned;
        }

        private static string MakeUniqueName(string baseName, HashSet<string> usedNames, string folder, bool overwriteExisting)
        {
            string candidate = baseName;
            int counter = 2;
            while (usedNames.Contains(candidate)
                || (!overwriteExisting && File.Exists(Path.Combine(folder, candidate + ".png"))))
            {
                candidate = baseName + "_" + counter;
                counter++;
            }
            usedNames.Add(candidate);
            return candidate;
        }
    }
}
