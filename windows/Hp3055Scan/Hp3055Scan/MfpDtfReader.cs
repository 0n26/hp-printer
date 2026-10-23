using System;
using System.Net.Sockets;

namespace Hp3055Scan;

public class ScanResult
{
    public int Width;
    public int Height;
    public int Dpi;
    public byte[]? Jpeg;
}

public static class MfpDtfReader
{
    public static ScanResult Read(TcpClient client, int dpi)
    {
        var stream = client.GetStream();
        stream.ReadTimeout = 30000;

        int width = 0, height = 0, code = 0;
        var payload = new System.Collections.Generic.List<byte>();

        // read first NEW_DOC block (flags=4) if present
        // generic loop: read blocks until END_PAGE (flags & 2)
        while (true)
        {
            byte[] header;
            try { header = ReadExact(stream, 8); }
            catch (IOException) { break; }
            catch (SocketException) { break; }

            uint blockLen = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            ushort headerLen = (ushort)(header[4] | (header[5] << 8));
            byte dataType = header[6];
            byte pageFlag = header[7];

            if (blockLen < 8 || blockLen > 1 << 20)
                throw new Exception($"Invalid block length {blockLen}");

            byte[] body = ReadExact(stream, (int)(blockLen - 8));

            if ((pageFlag & 4) != 0)
            {
                // NEW_DOC: skip (variant header)
                continue;
            }
            if ((pageFlag & 1) != 0)
            {
                // NEW_PAGE: parse start page record (at offset 0 of body)
                // MFPDTF_START_PAGE: ID(1) Code(1) PageNumber(2) BlackPixelsPerRow(2) BlackBitsPerPixel(2) BlackRows(4) BlackHorzDPI(4) BlackVertDPI(4) + CMY...
                code = body[1];
                width = body[3] | (body[4] << 8); // BlackPixelsPerRow LE u16 at offset 3
                // Actually layout: offset 0 ID, 1 Code, 2-3 PageNumber, 4-5 BlackPixelsPerRow, 6-7 BlackBitsPerPixel, 8-11 BlackRows, 12-15 BlackHorzDPI, 16-19 BlackVertDPI
                width = body[4] | (body[5] << 8);
                int bpp = body[6] | (body[7] << 8);
                height = (int)(body[8] | (body[9] << 8) | (body[10] << 16) | (body[11] << 24));
                int hdpi = (int)(body[12] | (body[13] << 8) | (body[14] << 16) | (body[15] << 24));
                int vdpi = (int)(body[16] | (body[17] << 8) | (body[18] << 16) | (body[19] << 24));
                if (hdpi > 0) dpi = hdpi >> 16;
                Console.WriteLine($"Start page: code={code} width={width} bpp={bpp} rows={height} dpi={dpi}");
                continue;
            }
            if ((pageFlag & 2) != 0)
            {
                // END_PAGE
                Console.WriteLine("End page received.");
                break;
            }

            // RASTER block: body = ID(1) dummy(1) Size(2) payload
            if (body.Length < 4) continue;
            byte id = body[0];
            int size = body[2] | (body[3] << 8);
            if (id == 1 && size > 0 && size <= body.Length - 4)
            {
                for (int i = 0; i < size; i++) payload.Add(body[4 + i]);
            }
            // id==111 (0x6F): skip
        }

        return new ScanResult { Width = width, Height = height, Dpi = dpi, Jpeg = payload.ToArray() };
    }

    private static byte[] ReadExact(NetworkStream stream, int n)
    {
        var buf = new byte[n];
        int total = 0;
        while (total < n)
        {
            int r = stream.Read(buf, total, n - total);
            if (r == 0) throw new Exception("Unexpected EOF");
            total += r;
        }
        return buf;
    }
}
