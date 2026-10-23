using System;
using System.IO;
using System.IO.Compression;

namespace Hp3055ScanGUI;

public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] data, int offset, int length)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < length; i++)
            c = CrcTable[(c ^ data[offset + i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = BitConverter.GetBytes(data.Length);
        Array.Reverse(len);
        s.Write(len, 0, 4);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);
        var crcData = new byte[4 + data.Length];
        typeBytes.CopyTo(crcData, 0);
        data.CopyTo(crcData, 4);
        var crc = Crc32(crcData, 0, crcData.Length);
        var crcBytes = BitConverter.GetBytes(crc);
        Array.Reverse(crcBytes);
        s.Write(crcBytes, 0, 4);
    }

    public static void SaveGrayscale(string path, byte[] gray, int w, int h)
    {
        using var fs = new FileStream(path, FileMode.Create);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, (uint)w);
        WriteBe32(ihdr, 4, (uint)h);
        ihdr[8] = 8;
        ihdr[9] = 0;
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(fs, "IHDR", ihdr);
        var raw = new byte[h * (w + 1)];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w + 1)] = 0;
            for (int x = 0; x < w; x++)
                raw[y * (w + 1) + 1 + x] = gray[y * w + x];
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        WriteChunk(fs, "IDAT", ms.ToArray());
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    public static void SaveBitmap1Bit(string path, byte[] packed, int w, int h)
    {
        int bytesPerRow = (w + 7) / 8;
        var gray = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int bi = y * bytesPerRow + (x / 8);
                int bit = 7 - (x % 8);
                gray[y * w + x] = (byte)(((packed[bi] >> bit) & 1) == 0 ? 255 : 0);
            }
        }
        SaveGrayscale(path, gray, w, h);
    }

    private static void WriteBe32(byte[] b, int off, uint v)
    {
        b[off] = (byte)((v >> 24) & 0xFF);
        b[off + 1] = (byte)((v >> 16) & 0xFF);
        b[off + 2] = (byte)((v >> 8) & 0xFF);
        b[off + 3] = (byte)(v & 0xFF);
    }
}
