using System;
using System.IO;
using System.Linq;

namespace Hp3055Scan;

class Program
{
    static int Main(string[] args)
    {
        string ip = "192.168.1.118";
        int dpi = 300;
        string mode = "gray";
        string format = "pdf";
        string? outPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip": ip = args[++i]; break;
                case "--dpi": dpi = int.Parse(args[++i]); break;
                case "--mode": mode = args[++i].ToLowerInvariant(); break;
                case "--format": format = args[++i].ToLowerInvariant(); break;
                case "--out": outPath = args[++i]; break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    PrintHelp();
                    return 2;
            }
        }

        int dataType = mode switch
        {
            "lineart" => PmlScanner.ModeLineart,
            "color" => PmlScanner.ModeColor,
            _ => PmlScanner.ModeGray,
        };
        int compression = PmlScanner.CompJpeg; // JPEG supported for all modes on this device

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        if (outPath == null)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
            Directory.CreateDirectory(dir);
            outPath = Path.Combine(dir, $"scan-{stamp}.{(format == "pdf" ? "pdf" : "jpg")}");
        }

        Console.WriteLine($"Scanning {mode} {dpi} dpi from {ip} ...");
        var result = PmlScanner.Scan(ip, dpi, dataType, compression, 10);
        bool isJpeg = result.Jpeg != null && result.Jpeg.Length >= 2 && result.Jpeg[0] == 0xFF && result.Jpeg[1] == 0xD8;
        Console.WriteLine($"Got {(isJpeg ? "JPEG" : "bitmap")}: {result.Jpeg?.Length} bytes, {result.Width}x{result.Height}");

        if (!isJpeg)
        {
            // lineart: raw 1-bit bitmap -> PNG
            var pngPath = Path.ChangeExtension(outPath, ".png");
            PngWriter.SaveBitmap1Bit(pngPath, result.Jpeg!, result.Width, result.Height);
            outPath = pngPath;
        }
        else if (format == "pdf")
        {
            WritePdf(outPath, result.Jpeg!, result.Width, result.Height, dpi, mode == "color");
        }
        else
        {
            File.WriteAllBytes(outPath, result.Jpeg!);
        }

        Console.WriteLine($"Saved: {outPath}");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
        return 0;
    }

    private static void WritePdf(string path, byte[] jpeg, int w, int h, int dpi, bool color)
    {
        double pw = w * 72.0 / dpi;
        double ph = h * 72.0 / dpi;
        string cs = color ? "/DeviceRGB" : "/DeviceGray";
        using var fs = new FileStream(path, FileMode.Create);
        using var wtr = new StreamWriter(fs, new System.Text.ASCIIEncoding(), leaveOpen: true) { AutoFlush = false };
        // We'll write binary via a helper; use a MemoryStream for the whole PDF
        var ms = new MemoryStream();
        var sw = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        sw.Write("%PDF-1.4\n");
        // obj 1 catalog
        long o1 = ms.Position; sw.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; sw.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        long o3 = ms.Position; sw.Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pw:F2} {ph:F2}] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
        long o4 = ms.Position;
        sw.Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        sw.Flush();
        ms.Write(jpeg, 0, jpeg.Length);
        sw.Write("\nendstream\nendobj\n");
        long o5 = ms.Position;
        string content = $"q {pw:F2} 0 0 {ph:F2} 0 0 cm /Im1 Do Q\n";
        sw.Write($"5 0 obj\n<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");
        long xref = ms.Position;
        sw.Write("xref\n0 6\n0000000000 65535 f \n");
        // offsets (10-digit zero-padded)
        foreach (var o in new[] { o1, o2, o3, o4, o5 })
            sw.Write($"{o:D10} 00000 n \n");
        sw.Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        sw.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void PrintHelp()
    {
        Console.WriteLine("hp3055scan - HP LaserJet 3055 network scanner (native PML client)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --ip ADDR     printer IP (default 192.168.1.118)");
        Console.WriteLine("  --dpi N       75|100|150|200|300|600|1200 (default 300)");
        Console.WriteLine("  --mode M      gray|color|lineart (default gray)");
        Console.WriteLine("  --format F    pdf|jpeg (default pdf)");
        Console.WriteLine("  --out PATH    output file");
        Console.WriteLine("  -h, --help    this help");
    }
}
