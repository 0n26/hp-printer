using System;
using System.Net.Sockets;

namespace Hp3055ScanGUI;

public static class PmlScanner
{
    public const int ModeLineart = 1;
    public const int ModeGray = 8;
    public const int ModeColor = 24;

    public const int CompNone = 1;
    public const int CompJpeg = 6;

    private const string Base = "1.3.6.1.4.1.11.2.3.9.4.2.1";

    public static ScanResult Scan(string ip, int dpi, int mode, int compression, int jpegQuality)
    {
        var snmp = new SnmpClient(ip);

        // 1. ensure idle (reset if stuck in DONE/ABORTED/etc.)
        try { snmp.SetInt(Base + ".2.2.1.12.0", 1); } catch { }
        try { snmp.SetOctet(Base + ".1.1.25.0", new byte[16]); } catch { }
        Thread.Sleep(2000);
        int state = snmp.GetInt(Base + ".2.2.1.12.0");
        if (state != 1) // IDLE
        {
            Console.WriteLine($"Scanner not idle (state={state}), waiting...");
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);
                state = snmp.GetInt(Base + ".2.2.1.12.0");
                if (state == 1) break;
            }
            if (state != 1) throw new Exception($"Scanner not idle (state={state})");
        }

        // 2. set params
        snmp.SetInt(Base + ".1.1.18.0", 45); // upload timeout
        snmp.SetInt(Base + ".2.2.1.3.0", mode); // pixel data type
        // resolution: 8 bytes, dpi<<16, big-endian, both axes
        {
            int v = dpi << 16;
            var b = new byte[8];
            for (int i = 0; i < 4; i++) { b[i] = (byte)((v >> (24 - 8 * i)) & 0xFF); b[i + 4] = b[i]; }
            snmp.SetOctet(Base + ".2.2.1.2.0", b);
        }
        snmp.SetInt(Base + ".2.2.1.4.0", compression);
        snmp.SetInt(Base + ".2.2.1.5.0", jpegQuality);
        // copier reduction (optional, may not exist)
        try { snmp.SetInt(Base + ".1.5.1.4.0", 100); } catch { /* ignore noSuchName */ }
        // lock token
        snmp.SetOctet(Base + ".1.1.25.0", Enumerable.Repeat((byte)0x35, 16).ToArray());

        ScanResult? result = null;
        Exception? last = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                Console.WriteLine("Retrying scan...");
                Thread.Sleep(3000);
                try { snmp.SetInt(Base + ".2.2.1.12.0", 1); } catch { }
                try { snmp.SetOctet(Base + ".1.1.25.0", new byte[16]); } catch { }
                Thread.Sleep(2000);
            }

            TcpClient? tcp = null;
            try
            {
                tcp = new TcpClient();
                tcp.Connect(ip, 8290);
                tcp.ReceiveTimeout = 30000;

                snmp.SetInt(Base + ".2.2.1.12.0", 2); // START
                for (int i = 0; i < 40; i++)
                {
                    Thread.Sleep(1000);
                    state = snmp.GetInt(Base + ".2.2.1.12.0");
                    if (state == 3) break; // ACTIVE
                    if (state != 2) break; // bail
                }
                if (state != 3) throw new Exception($"Scan did not start (state={state})");

                result = MfpDtfReader.Read(tcp, dpi);
                tcp.Close();
                tcp = null;

                try { snmp.SetInt(Base + ".2.2.1.12.0", 1); } catch { }
                try { snmp.SetOctet(Base + ".1.1.25.0", new byte[16]); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                last = ex;
                try { tcp?.Close(); } catch { }
            }
        }
        throw last ?? new Exception("Scan failed");
    }
}
