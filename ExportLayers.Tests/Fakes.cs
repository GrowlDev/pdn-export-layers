using System;
using System.Runtime.InteropServices;
using ExportLayersPlugin;

namespace ExportLayersTests
{
    /// <summary>A BGRA32 pixel buffer with configurable row stride (to exercise stride handling).</summary>
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

        /// <summary>Builds a SourceLayer whose OpenPixels pins this buffer until disposed.</summary>
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
