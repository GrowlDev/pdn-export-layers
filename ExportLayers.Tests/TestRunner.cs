using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ExportLayersPlugin;

namespace ExportLayersTests
{
    public static class TestRunner
    {
        private static int failures;
        private static int checks;

        private static void Check(bool condition, string what)
        {
            checks++;
            if (!condition)
            {
                failures++;
                Console.WriteLine("  FAIL: " + what);
            }
        }

        public static int RunAll()
        {
            string root = Path.Combine(Path.GetTempPath(), "ExportLayersTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestSanitize();
                TestResolveFolderErrors();
                TestBasicExport(Path.Combine(root, "basic"));
                TestVisibleOnly(Path.Combine(root, "visible"));
                TestNoOverwrite(Path.Combine(root, "noverwrite"));
                TestOverwriteReplaces(Path.Combine(root, "overwrite"));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine($"{checks} checks, {failures} failures");
            return failures == 0 ? 0 : 1;
        }

        // ---- helpers ----

        private const int W = 64;
        private const int H = 40;

        private static FakePixels MakePixels(Action<FakePixels> paint, int stridePadding = 12)
        {
            var p = new FakePixels(W, H, stridePadding);
            paint?.Invoke(p);
            return p;
        }

        private static ExportLayersConfigToken Token(string folder, bool visibleOnly = false, bool overwrite = true)
        {
            return new ExportLayersConfigToken
            {
                CustomFolder = folder,
                VisibleLayersOnly = visibleOnly,
                OverwriteExisting = overwrite,
                ExportRequested = true,
            };
        }

        private static ExportResult Export(string folder, ExportLayersConfigToken token, params SourceLayer[] layers)
        {
            return LayerExporter.ExportCore(W, H, new List<SourceLayer>(layers), token);
        }

        private static Color GetPngPixel(string path, int x, int y, out Size size, out System.Drawing.Imaging.PixelFormat format)
        {
            using (var bmp = new Bitmap(path))
            {
                size = bmp.Size;
                format = bmp.PixelFormat;
                return bmp.GetPixel(x, y);
            }
        }

        // ---- tests ----

        private static void TestSanitize()
        {
            Console.WriteLine("TestSanitize");
            Check(LayerExporter.SanitizeFileName("wall") == "wall", "plain name unchanged");
            Check(LayerExporter.SanitizeFileName("  spaced name  ") == "spaced name", "spaces kept, ends trimmed");
            Check(LayerExporter.SanitizeFileName("plaster & lath: <old>") == "plaster & lath_ _old_", "illegal chars replaced: got '" + LayerExporter.SanitizeFileName("plaster & lath: <old>") + "'");
            Check(LayerExporter.SanitizeFileName("a/b\\c|d?e*f") == "a_b_c_d_e_f", "slashes/pipes/wildcards replaced");
            Check(LayerExporter.SanitizeFileName("") == "layer", "empty becomes 'layer'");
            Check(LayerExporter.SanitizeFileName("   ") == "layer", "whitespace becomes 'layer'");
            Check(LayerExporter.SanitizeFileName("con") == "con_", "reserved device name suffixed");
            Check(LayerExporter.SanitizeFileName("Layer 1.") == "Layer 1", "trailing dot trimmed");
            Check(LayerExporter.SanitizeFileName("tab\there") == "tab_here", "control char replaced");
            Check(LayerExporter.SanitizeFileName(null) == "layer", "null becomes 'layer'");
        }

        private static void TestResolveFolderErrors()
        {
            Console.WriteLine("TestResolveFolderErrors");
            bool threwRelative = false;
            try { LayerExporter.ResolveDestinationFolder(Token("not\\rooted")); }
            catch (ExportException) { threwRelative = true; }
            Check(threwRelative, "relative custom folder rejected");

            // Outside Paint.NET there is no document path, so auto mode must fail cleanly.
            bool threwNoFolder = false;
            try { LayerExporter.ResolveDestinationFolder(Token("")); }
            catch (ExportException) { threwNoFolder = true; }
            Check(threwNoFolder, "no destination -> friendly error");
        }

        private static void TestBasicExport(string folder)
        {
            Console.WriteLine("TestBasicExport");

            var wall = MakePixels(p =>
            {
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 16; x++)
                        p.SetPixel(x, y, 10, 20, 200, 255); // opaque reddish block, partial canvas
            });
            var studs = MakePixels(p => p.SetPixel(5, 5, 0, 255, 0, 128)); // single semi-transparent pixel
            var messy = MakePixels(p => p.SetPixel(0, 0, 1, 2, 3, 4));
            var wall2 = MakePixels(p => p.SetPixel(1, 1, 9, 9, 9, 9));
            var hidden = MakePixels(p => p.SetPixel(2, 2, 7, 7, 7, 7));

            var result = Export(folder, Token(folder),
                wall.ToLayer("wall", true),
                studs.ToLayer("studs", true),
                messy.ToLayer("plaster & lath: <old>", true),
                wall2.ToLayer("wall", true),
                hidden.ToLayer("hidden layer", false));

            Check(result.Files.Count == 5, $"5 files exported, got {result.Files.Count}");
            Check(File.Exists(Path.Combine(folder, "wall.png")), "wall.png exists");
            Check(File.Exists(Path.Combine(folder, "studs.png")), "studs.png exists");
            Check(File.Exists(Path.Combine(folder, "plaster & lath_ _old_.png")), "sanitized name exists");
            Check(File.Exists(Path.Combine(folder, "wall_2.png")), "duplicate name got _2 suffix");
            Check(File.Exists(Path.Combine(folder, "hidden layer.png")), "hidden layer exported by default");

            var c = GetPngPixel(Path.Combine(folder, "wall.png"), 3, 3, out Size size, out var format);
            Check(size.Width == W && size.Height == H, $"canvas-sized PNG ({size.Width}x{size.Height})");
            Check(format == System.Drawing.Imaging.PixelFormat.Format32bppArgb, $"32-bit ARGB PNG, got {format}");
            Check(c.R == 200 && c.G == 20 && c.B == 10 && c.A == 255, $"opaque pixel roundtrip, got {c}");

            var t = GetPngPixel(Path.Combine(folder, "wall.png"), 40, 30, out _, out _);
            Check(t.A == 0, $"untouched area transparent, alpha={t.A}");

            var s = GetPngPixel(Path.Combine(folder, "studs.png"), 5, 5, out _, out _);
            Check(s.A == 128 && s.G == 255 && s.R == 0 && s.B == 0, $"semi-transparent pixel roundtrip, got {s}");

            var w2 = GetPngPixel(Path.Combine(folder, "wall_2.png"), 1, 1, out _, out _);
            Check(w2.A == 9, $"second 'wall' layer has its own content, alpha={w2.A}");
        }

        private static void TestVisibleOnly(string folder)
        {
            Console.WriteLine("TestVisibleOnly");
            var result = Export(folder, Token(folder, visibleOnly: true),
                MakePixels(null).ToLayer("shown", true),
                MakePixels(null).ToLayer("hidden", false));

            Check(result.Files.Count == 1, $"1 file exported, got {result.Files.Count}");
            Check(result.SkippedHidden == 1, $"1 hidden skipped, got {result.SkippedHidden}");
            Check(!File.Exists(Path.Combine(folder, "hidden.png")), "hidden layer not written");

            bool threw = false;
            try
            {
                Export(folder, Token(folder, visibleOnly: true), MakePixels(null).ToLayer("h", false));
            }
            catch (ExportException) { threw = true; }
            Check(threw, "all-hidden document -> friendly error");
        }

        private static void TestNoOverwrite(string folder)
        {
            Console.WriteLine("TestNoOverwrite");
            SourceLayer[] layers =
            {
                MakePixels(null).ToLayer("wall", true),
                MakePixels(null).ToLayer("wall", true),
            };

            Export(folder, Token(folder, overwrite: false), layers);
            Export(folder, Token(folder, overwrite: false), layers);

            string[] files = Directory.GetFiles(folder, "*.png").Select(Path.GetFileName).OrderBy(f => f).ToArray();
            Check(files.Length == 4, $"4 distinct files after two no-overwrite runs, got {files.Length}: {string.Join(", ", files)}");
            Check(files.Contains("wall.png") && files.Contains("wall_2.png")
                && files.Contains("wall_3.png") && files.Contains("wall_4.png"),
                "numbered names fill in: " + string.Join(", ", files));
        }

        private static void TestOverwriteReplaces(string folder)
        {
            Console.WriteLine("TestOverwriteReplaces");
            var v1 = MakePixels(p => p.SetPixel(0, 0, 0, 0, 0, 255));
            Export(folder, Token(folder), v1.ToLayer("wall", true));

            var v2 = MakePixels(p => p.SetPixel(0, 0, 0, 0, 255, 255));
            Export(folder, Token(folder), v2.ToLayer("wall", true));

            string[] files = Directory.GetFiles(folder, "*.png");
            Check(files.Length == 1, $"still exactly 1 file, got {files.Length}");
            var c = GetPngPixel(Path.Combine(folder, "wall.png"), 0, 0, out _, out _);
            Check(c.R == 255, $"file content replaced on re-export, R={c.R}");
        }
    }
}
