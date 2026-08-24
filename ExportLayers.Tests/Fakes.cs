using System;
using System.Runtime.InteropServices;
using ExportLayersPlugin;

namespace ExportLayersTests
{
    /// <summary>A BGRA32 buffer with a stride you can pad, so the copy path gets a real workout.</summary>
    public sealed class FakePixels
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public readonly byte[] Data;

        public FakePixels(int width, int height, int stridePadding)
        {
            Width = width;
            Height = height;
            Stride = width * 4 + stridePadding;
            Data = new byte[Stride * height];
        }

        public void SetPixel(int x, int y, byte b, byte g, byte r, byte a)
        {
            int i = y * Stride + x * 4;
            Data[i] = b;
            Data[i + 1] = g;
            Data[i + 2] = r;
            Data[i + 3] = a;
        }

        // Pins the array for as long as the LockedPixels lives. Fine for a test; don't copy
        // this pattern anywhere that matters.
        public SourceLayer ToLayer(string name, bool visible)
        {
            return new SourceLayer
            {
                Name = name,
                Visible = visible,
                OpenPixels = () =>
                {
                    GCHandle handle = GCHandle.Alloc(Data, GCHandleType.Pinned);
                    return new LockedPixels(handle.AddrOfPinnedObject(), Stride, () => handle.Free());
                },
            };
        }
    }
}
