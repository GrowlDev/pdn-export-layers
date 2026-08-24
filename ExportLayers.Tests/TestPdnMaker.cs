using System;
using System.IO;
using PaintDotNet;

namespace ExportLayersTests
{
    /// <summary>
    /// Writes a small layered .pdn using Paint.NET's own Document/BitmapLayer classes,
    /// for interactive testing of the plugin. Covers: distinct names, spaces, illegal
    /// filename characters, duplicate names, a hidden layer, partial-canvas content,
    /// and semi-transparency.
    /// </summary>
    public static class TestPdnMaker
    {
        public static void Make(string path)
        {
            const int width = 96;
            const int height = 64;

            Document document = new Document(width, height);

            document.Layers.Add(MakeLayer("wall", true, width, height, (x, y) =>
                ColorBgra.FromBgra(40, 60, 180, 255))); // full opaque backdrop

            document.Layers.Add(MakeLayer("studs", true, width, height, (x, y) =>
                (x % 16 < 4) ? ColorBgra.FromBgra(20, 140, 200, 255) : ColorBgra.FromBgra(0, 0, 0, 0)));

            document.Layers.Add(MakeLayer("plaster & lath: <old>", true, width, height, (x, y) =>
                (y < 20) ? ColorBgra.FromBgra(200, 200, 220, 128) : ColorBgra.FromBgra(0, 0, 0, 0)));

            document.Layers.Add(MakeLayer("wall", true, width, height, (x, y) =>
                (x > 60 && y > 40) ? ColorBgra.FromBgra(10, 200, 10, 255) : ColorBgra.FromBgra(0, 0, 0, 0)));

            document.Layers.Add(MakeLayer("debris (hidden)", false, width, height, (x, y) =>
                ((x + y) % 9 == 0) ? ColorBgra.FromBgra(30, 30, 30, 255) : ColorBgra.FromBgra(0, 0, 0, 0)));

            using (FileStream stream = File.Create(path))
            {
                document.SaveToStream(stream);
            }

            Console.WriteLine("Wrote " + path);
        }

        public static bool Verify(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                Document document = Document.FromStream(stream);
                Console.WriteLine($"Loaded: {document.Width}x{document.Height}, {document.Layers.Count} layers");
                foreach (Layer layer in document.Layers)
                {
                    Console.WriteLine($"  '{layer.Name}' visible={layer.Visible}");
                }
                return document.Layers.Count == 5;
            }
        }

        private static BitmapLayer MakeLayer(string name, bool visible, int width, int height, Func<int, int, ColorBgra> pixel)
        {
            BitmapLayer layer = new BitmapLayer(width, height);
            layer.Name = name;
            layer.Visible = visible;
            Surface surface = layer.Surface;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    surface[x, y] = pixel(x, y);
                }
            }
            return layer;
        }
    }
}
