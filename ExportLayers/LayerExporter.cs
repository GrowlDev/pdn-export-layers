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
    /// <summary>Thrown for expected, user-facing export problems (no destination, no layers, ...).</summary>
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

    /// <summary>A BGRA32 pixel view of one layer, valid until disposed.</summary>
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

    /// <summary>One layer to export, decoupled from Paint.NET types so the pipeline is testable.</summary>
    public sealed class SourceLayer
    {
        public string Name;
        public bool Visible;
        public Func<LockedPixels> OpenPixels;
    }

    public static class LayerExporter
    {
        /// <summary>
        /// Exports the document's layers as canvas-sized straight-alpha 32-bit PNGs.
        /// Runs at most once per call; safe on any thread.
        /// </summary>
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

        /// <summary>Paint.NET-independent export pipeline: naming, destination, PNG writing.</summary>
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

        /// <summary>
        /// Where files will go: an explicit folder from the token wins; otherwise a folder named
        /// after the .pdn file, next to it (resolved at export time so renames are followed).
        /// </summary>
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

        /// <summary>Folder derived from the open document's file path, or null if unavailable.</summary>
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

            // Windows disallows trailing dots and spaces on file names.
            string cleaned = sb.ToString().TrimEnd('.', ' ');

            if (cleaned.Length == 0)
            {
                cleaned = "layer";
            }
            else if (ReservedNames.Contains(cleaned))
            {
                cleaned += "_";
            }

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
