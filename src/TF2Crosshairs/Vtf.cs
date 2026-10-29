using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TF2Crosshairs
{
    /// <summary>
    /// Minimal reader for Valve Texture Format (.vtf) files.
    /// Decodes frame 0 / face 0 of the largest mip level into a 32-bit ARGB bitmap.
    /// Supports the formats crosshair packs actually use (DXT1/3/5 and the 8/16/24/32-bit uncompressed layouts).
    /// </summary>
    public static class Vtf
    {
        public static Bitmap Decode(byte[] d)
        {
            if (d.Length < 64 || d[0] != (byte)'V' || d[1] != (byte)'T' || d[2] != (byte)'F' || d[3] != 0)
                throw new InvalidDataException("Not a VTF file.");

            uint major = BitConverter.ToUInt32(d, 4);
            uint minor = BitConverter.ToUInt32(d, 8);
            uint headerSize = BitConverter.ToUInt32(d, 12);
            int width = BitConverter.ToUInt16(d, 16);
            int height = BitConverter.ToUInt16(d, 18);
            uint flags = BitConverter.ToUInt32(d, 20);
            int frames = Math.Max(1, (int)BitConverter.ToUInt16(d, 24));
            int format = BitConverter.ToInt32(d, 52);
            int mipCount = Math.Max(1, (int)d[56]);
            int lowFormat = BitConverter.ToInt32(d, 57);
            int lowW = d[61], lowH = d[62];
            bool v72 = major > 7 || (major == 7 && minor >= 2);
            bool v73 = major > 7 || (major == 7 && minor >= 3);
            int depth = v72 ? Math.Max(1, (int)BitConverter.ToUInt16(d, 63)) : 1;
            int faces = (flags & 0x4000) != 0 ? 6 : 1; // TEXTUREFLAGS_ENVMAP
            if (width <= 0 || height <= 0) throw new InvalidDataException("VTF has no pixels.");

            long offset;
            if (v73)
            {
                // 7.3+: image data lives behind a resource entry tagged 0x30 0x00 0x00.
                int numRes = BitConverter.ToInt32(d, 68);
                offset = -1;
                for (int i = 0; i < numRes; i++)
                {
                    int o = 80 + i * 8;
                    if (o + 8 > d.Length) break;
                    if (d[o] == 0x30 && d[o + 1] == 0 && d[o + 2] == 0)
                    {
                        offset = BitConverter.ToUInt32(d, o + 4);
                        break;
                    }
                }
                if (offset < 0) throw new InvalidDataException("VTF has no image data.");
            }
            else
            {
                // 7.0-7.2: header, then the low-res thumbnail, then the mip chain.
                offset = headerSize;
                if (lowFormat >= 0 && lowW > 0 && lowH > 0) offset += ImageSize(lowFormat, lowW, lowH);
            }

            // Mips are stored smallest first; skip everything but mip 0.
            for (int m = mipCount - 1; m >= 1; m--)
                offset += (long)ImageSize(format, Math.Max(1, width >> m), Math.Max(1, height >> m)) * frames * faces * depth;

            int need = ImageSize(format, width, height);
            if (offset + need > d.Length) throw new InvalidDataException("VTF image data is truncated.");

            byte[] bgra = ToBgra(d, (int)offset, format, width, height);
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(bgra, y * width * 4, bd.Scan0 + y * bd.Stride, width * 4);
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }

        static int ImageSize(int format, int w, int h) => format switch
        {
            13 or 20 => Blocks(w) * Blocks(h) * 8,   // DXT1, DXT1 one-bit alpha
            14 or 15 => Blocks(w) * Blocks(h) * 16,  // DXT3, DXT5
            0 or 1 or 11 or 12 or 16 or 23 or 26 => w * h * 4,
            2 or 3 or 9 or 10 => w * h * 3,
            4 or 6 or 17 or 18 or 19 or 21 or 22 => w * h * 2,
            5 or 7 or 8 => w * h,
            24 or 25 => w * h * 8,
            _ => throw new NotSupportedException("Unsupported VTF image format " + format + "."),
        };

        /// <summary>Number of 4x4 DXT blocks along an edge of <paramref name="pixels"/> pixels.</summary>
        static int Blocks(int pixels) => Math.Max(1, (pixels + 3) / 4);

        static byte[] ToBgra(byte[] d, int o, int format, int w, int h)
        {
            var p = new byte[w * h * 4];
            switch (format)
            {
                case 13: case 20: DecodeDxt1(d, o, w, h, p); return p;
                case 14: DecodeDxt3(d, o, w, h, p); return p;
                case 15: DecodeDxt5(d, o, w, h, p); return p;
            }

            for (int i = 0; i < w * h; i++)
            {
                byte r, g, b, a = 255;
                int v;
                switch (format)
                {
                    case 0: r = d[o]; g = d[o + 1]; b = d[o + 2]; a = d[o + 3]; o += 4; break;              // RGBA8888
                    case 1: a = d[o]; b = d[o + 1]; g = d[o + 2]; r = d[o + 3]; o += 4; break;              // ABGR8888
                    case 2: case 9: r = d[o]; g = d[o + 1]; b = d[o + 2]; o += 3; break;                    // RGB888
                    case 3: case 10: b = d[o]; g = d[o + 1]; r = d[o + 2]; o += 3; break;                   // BGR888
                    case 11: a = d[o]; r = d[o + 1]; g = d[o + 2]; b = d[o + 3]; o += 4; break;             // ARGB8888
                    case 12: b = d[o]; g = d[o + 1]; r = d[o + 2]; a = d[o + 3]; o += 4; break;             // BGRA8888
                    case 16: b = d[o]; g = d[o + 1]; r = d[o + 2]; o += 4; break;                           // BGRX8888
                    case 5: case 7: r = g = b = d[o]; o += 1; break;                                        // I8 / P8
                    case 6: r = g = b = d[o]; a = d[o + 1]; o += 2; break;                                  // IA88
                    case 8: r = g = b = 255; a = d[o]; o += 1; break;                                       // A8
                    case 4: v = d[o] | (d[o + 1] << 8); r = E5((v >> 11) & 31); g = E6((v >> 5) & 63); b = E5(v & 31); o += 2; break;   // RGB565
                    case 17: v = d[o] | (d[o + 1] << 8); b = E5((v >> 11) & 31); g = E6((v >> 5) & 63); r = E5(v & 31); o += 2; break;  // BGR565
                    case 18: case 21:                                                                       // BGRX5551 / BGRA5551
                        v = d[o] | (d[o + 1] << 8); b = E5(v & 31); g = E5((v >> 5) & 31); r = E5((v >> 10) & 31);
                        if (format == 21) a = (byte)((v & 0x8000) != 0 ? 255 : 0);
                        o += 2; break;
                    case 19: v = d[o] | (d[o + 1] << 8); b = E4(v & 15); g = E4((v >> 4) & 15); r = E4((v >> 8) & 15); a = E4((v >> 12) & 15); o += 2; break; // BGRA4444
                    default: throw new NotSupportedException("Unsupported VTF image format " + format + ".");
                }
                p[i * 4] = b; p[i * 4 + 1] = g; p[i * 4 + 2] = r; p[i * 4 + 3] = a;
            }
            return p;
        }

        static byte E4(int v) => (byte)(v * 17);
        static byte E5(int v) => (byte)((v << 3) | (v >> 2));
        static byte E6(int v) => (byte)((v << 2) | (v >> 4));

        static void DecodeDxt1(byte[] d, int o, int w, int h, byte[] p)
        {
            int bw = Blocks(w), bh = Blocks(h);
            var c = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, o += 8)
                {
                    ColorBlock(d, o, c, true);
                    PutBlock(p, w, h, bx, by, c, BitConverter.ToUInt32(d, o + 4), null);
                }
        }

        static void DecodeDxt3(byte[] d, int o, int w, int h, byte[] p)
        {
            int bw = Blocks(w), bh = Blocks(h);
            var c = new byte[16];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, o += 16)
                {
                    for (int k = 0; k < 16; k++) alpha[k] = E4((d[o + k / 2] >> ((k % 2) * 4)) & 15);
                    ColorBlock(d, o + 8, c, false);
                    PutBlock(p, w, h, bx, by, c, BitConverter.ToUInt32(d, o + 12), alpha);
                }
        }

        static void DecodeDxt5(byte[] d, int o, int w, int h, byte[] p)
        {
            int bw = Blocks(w), bh = Blocks(h);
            var c = new byte[16];
            var pal = new byte[8];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, o += 16)
                {
                    int a0 = d[o], a1 = d[o + 1];
                    pal[0] = (byte)a0; pal[1] = (byte)a1;
                    if (a0 > a1)
                    {
                        for (int i = 1; i <= 6; i++) pal[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                    }
                    else
                    {
                        for (int i = 1; i <= 4; i++) pal[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                        pal[6] = 0; pal[7] = 255;
                    }
                    ulong bits = 0;
                    for (int k = 0; k < 6; k++) bits |= (ulong)d[o + 2 + k] << (8 * k);
                    for (int k = 0; k < 16; k++) alpha[k] = pal[(int)((bits >> (3 * k)) & 7)];
                    ColorBlock(d, o + 8, c, false);
                    PutBlock(p, w, h, bx, by, c, BitConverter.ToUInt32(d, o + 12), alpha);
                }
        }

        /// <summary>Expands a 4-byte DXT colour block into four BGRA palette entries.</summary>
        static void ColorBlock(byte[] d, int o, byte[] c, bool allowThreeColour)
        {
            int c0 = d[o] | (d[o + 1] << 8), c1 = d[o + 2] | (d[o + 3] << 8);
            byte r0 = E5((c0 >> 11) & 31), g0 = E6((c0 >> 5) & 63), b0 = E5(c0 & 31);
            byte r1 = E5((c1 >> 11) & 31), g1 = E6((c1 >> 5) & 63), b1 = E5(c1 & 31);
            Set(c, 0, b0, g0, r0, 255);
            Set(c, 1, b1, g1, r1, 255);
            if (!allowThreeColour || c0 > c1)
            {
                Set(c, 2, (byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3), 255);
                Set(c, 3, (byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3), 255);
            }
            else
            {
                Set(c, 2, (byte)((b0 + b1) / 2), (byte)((g0 + g1) / 2), (byte)((r0 + r1) / 2), 255);
                Set(c, 3, 0, 0, 0, 0);
            }
        }

        static void Set(byte[] c, int i, byte b, byte g, byte r, byte a)
        {
            c[i * 4] = b; c[i * 4 + 1] = g; c[i * 4 + 2] = r; c[i * 4 + 3] = a;
        }

        static void PutBlock(byte[] p, int w, int h, int bx, int by, byte[] c, uint idx, byte[]? alpha)
        {
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    int x = bx * 4 + i, y = by * 4 + j;
                    if (x >= w || y >= h) continue;
                    int ci = (int)((idx >> (2 * (j * 4 + i))) & 3);
                    int pi = (y * w + x) * 4;
                    p[pi] = c[ci * 4];
                    p[pi + 1] = c[ci * 4 + 1];
                    p[pi + 2] = c[ci * 4 + 2];
                    p[pi + 3] = alpha != null ? alpha[j * 4 + i] : c[ci * 4 + 3];
                }
        }
    }
}
