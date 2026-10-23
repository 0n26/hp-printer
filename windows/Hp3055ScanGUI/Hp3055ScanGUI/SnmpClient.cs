using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hp3055ScanGUI;

public class SnmpClient
{
    private readonly string _ip;
    private readonly int _port;
    public SnmpClient(string ip, int port = 161)
    {
        _ip = ip;
        _port = port;
    }

    public int GetInt(string oid)
    {
        var (type, val) = Get(oid);
        if (type != 0x02) throw new Exception($"Expected INTEGER, got 0x{type:X2}");
        // big-endian -> host
        uint v = 0;
        foreach (var b in val) v = (v << 8) | b;
        return (int)v;
    }

    public byte[] GetOctet(string oid)
    {
        var (type, val) = Get(oid);
        if (type != 0x04) throw new Exception($"Expected OCTET STRING, got 0x{type:X2}");
        return val;
    }

    public (byte type, byte[] value) Get(string oid)
    {
        var req = BuildGet(oid);
        var resp = Send(req);
        return ParseGetResponse(resp);
    }

    public void SetInt(string oid, int value)
    {
        var bytes = new byte[4];
        for (int i = 3; i >= 0; i--) { bytes[i] = (byte)(value & 0xFF); value >>= 8; }
        Set(oid, 0x02, bytes);
    }

    public void SetOctet(string oid, byte[] value)
    {
        Set(oid, 0x04, value);
    }

    public void Set(string oid, byte type, byte[] value)
    {
        var req = BuildSet(oid, type, value);
        var resp = Send(req);
        ParseSetResponse(resp);
    }

    // ---- BER helpers ----

    private static byte[] BuildGet(string oid)
    {
        var pdu = new List<byte>();
        // request-id, error-status, error-index
        AppendInteger(pdu, 1);
        AppendInteger(pdu, 0);
        AppendInteger(pdu, 0);
        // varbind list
        var vblist = new List<byte>();
        var vb = new List<byte>();
        AppendOid(vb, oid);
        AppendNull(vb);
        Wrap(vblist, 0x30, vb);   // varbind sequence
        Wrap(pdu, 0x30, vblist);  // varbind list
        // wrap PDU (GetRequest 0xA0)
        var pduBytes = pdu.ToArray();
        var packet = new List<byte>();
        AppendInteger(packet, 0); // version
        AppendOctetString(packet, Encoding.ASCII.GetBytes("public"));
        // PDU
        var pduWrap = new List<byte>();
        pduWrap.Add(0xA0);
        var lenBytes = EncodeLength(pduBytes.Length);
        pduWrap.AddRange(lenBytes);
        pduWrap.AddRange(pduBytes);
        packet.AddRange(pduWrap);
        // wrap whole message
        var msg = packet.ToArray();
        var result = new List<byte>();
        result.Add(0x30);
        result.AddRange(EncodeLength(msg.Length));
        result.AddRange(msg);
        return result.ToArray();
    }

    private static byte[] BuildSet(string oid, byte type, byte[] value)
    {
        var pdu = new List<byte>();
        AppendInteger(pdu, 2);
        AppendInteger(pdu, 0);
        AppendInteger(pdu, 0);
        var vblist = new List<byte>();
        var vb = new List<byte>();
        AppendOid(vb, oid);
        vb.Add(type);
        if (type == 0x02)
        {
            AppendLength(vb, value.Length);
            vb.AddRange(value);
        }
        else
        {
            AppendLength(vb, value.Length);
            vb.AddRange(value);
        }
        Wrap(vblist, 0x30, vb);
        Wrap(pdu, 0x30, vblist);
        var pduBytes = pdu.ToArray();
        var packet = new List<byte>();
        AppendInteger(packet, 0);
        AppendOctetString(packet, Encoding.ASCII.GetBytes("public"));
        var pduWrap = new List<byte>();
        pduWrap.Add(0xA3); // SetRequest
        pduWrap.AddRange(EncodeLength(pduBytes.Length));
        pduWrap.AddRange(pduBytes);
        packet.AddRange(pduWrap);
        var msg = packet.ToArray();
        var result = new List<byte>();
        result.Add(0x30);
        result.AddRange(EncodeLength(msg.Length));
        result.AddRange(msg);
        return result.ToArray();
    }

    private byte[] Send(byte[] data)
    {
        if (Environment.GetEnvironmentVariable("HP3055_DEBUG") == "1")
        {
            Console.Error.WriteLine("REQ: " + BitConverter.ToString(data).Replace("-", " "));
        }
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = 5000;
        udp.Send(data, data.Length, _ip, _port);
        var ep = new IPEndPoint(IPAddress.Any, 0);
        var resp = udp.Receive(ref ep);
        if (Environment.GetEnvironmentVariable("HP3055_DEBUG") == "1")
        {
            Console.Error.WriteLine("RESP: " + BitConverter.ToString(resp).Replace("-", " "));
        }
        return resp;
    }

    private static (byte type, byte[] value) ParseGetResponse(byte[] data)
    {
        // skip outer sequence
        int idx = 0;
        if (data[idx++] != 0x30) throw new Exception("not a sequence");
        int len = ReadLength(data, ref idx);
        // version
        if (data[idx++] != 0x02) throw new Exception("bad version");
        int vlen = ReadLength(data, ref idx);
        idx += vlen;
        // community
        if (data[idx++] != 0x04) throw new Exception("bad community");
        int clen = ReadLength(data, ref idx);
        idx += clen;
        // PDU (0xA2)
        if (data[idx++] != 0xA2) throw new Exception($"expected GetResponse 0xA2, got 0x{data[idx-1]:X2}");
        ReadLength(data, ref idx);
        // request-id
        if (data[idx++] != 0x02) throw new Exception("bad reqid");
        int rl = ReadLength(data, ref idx); idx += rl;
        // error-status
        if (data[idx++] != 0x02) throw new Exception("bad errstat");
        int esl = ReadLength(data, ref idx);
        int errstat = 0;
        for (int i = 0; i < esl; i++) errstat = (errstat << 8) | data[idx++];
        if (errstat != 0) throw new Exception($"SNMP error status {errstat}");
        // error-index
        if (data[idx++] != 0x02) throw new Exception("bad erridx");
        int eil = ReadLength(data, ref idx); idx += eil;
        // varbind list
        if (data[idx++] != 0x30) throw new Exception("bad vblist");
        ReadLength(data, ref idx);
        // varbind
        if (data[idx++] != 0x30) throw new Exception("bad varbind");
        ReadLength(data, ref idx);
        // OID
        if (data[idx++] != 0x06) throw new Exception("bad oid");
        int ol = ReadLength(data, ref idx); idx += ol;
        // value
        byte type = data[idx++];
        int vlen2 = ReadLength(data, ref idx);
        var val = new byte[vlen2];
        Array.Copy(data, idx, val, 0, vlen2);
        return (type, val);
    }

    private static void ParseSetResponse(byte[] data)
    {
        int idx = 0;
        if (data[idx++] != 0x30) throw new Exception("not a sequence");
        ReadLength(data, ref idx);
        if (data[idx++] != 0x02) throw new Exception("bad version");
        int vlen = ReadLength(data, ref idx); idx += vlen;
        if (data[idx++] != 0x04) throw new Exception("bad community");
        int clen = ReadLength(data, ref idx); idx += clen;
        if (data[idx++] != 0xA2) throw new Exception($"expected GetResponse, got 0x{data[idx-1]:X2}");
        ReadLength(data, ref idx);
        if (data[idx++] != 0x02) throw new Exception("bad reqid");
        int rl = ReadLength(data, ref idx); idx += rl;
        if (data[idx++] != 0x02) throw new Exception("bad errstat");
        int esl = ReadLength(data, ref idx);
        int errstat = 0;
        for (int i = 0; i < esl; i++) errstat = (errstat << 8) | data[idx++];
        if (errstat != 0) throw new Exception($"SNMP SET error status {errstat}");
        if (data[idx++] != 0x02) throw new Exception("bad erridx");
        int eil = ReadLength(data, ref idx); idx += eil;
    }

    // ---- encoding helpers ----

    private static void AppendInteger(List<byte> buf, int v)
    {
        // encode v as 4-byte big-endian (handle negatives as two's complement)
        var bytes = new byte[4];
        for (int i = 3; i >= 0; i--) { bytes[i] = (byte)(v & 0xFF); v >>= 8; }
        // trim leading redundant bytes
        int start = 0;
        while (start < bytes.Length - 1)
        {
            if (bytes[start] == 0x00 && (bytes[start + 1] & 0x80) == 0) start++;
            else if (bytes[start] == 0xFF && (bytes[start + 1] & 0x80) != 0) start++;
            else break;
        }
        buf.Add(0x02);
        var trimmed = bytes[start..];
        AppendLength(buf, trimmed.Length);
        buf.AddRange(trimmed);
    }

    private static void AppendOctetString(List<byte> buf, byte[] value)
    {
        buf.Add(0x04);
        AppendLength(buf, value.Length);
        buf.AddRange(value);
    }

    private static void AppendOid(List<byte> buf, string oid)
    {
        var parts = oid.Split('.');
        var body = new List<byte>();
        // first two components: 40*first + second
        int first = int.Parse(parts[0]);
        int second = int.Parse(parts[1]);
        body.Add((byte)(40 * first + second));
        for (int i = 2; i < parts.Length; i++)
        {
            int v = int.Parse(parts[i]);
            EncodeSubId(body, v);
        }
        buf.Add(0x06);
        AppendLength(buf, body.Count);
        buf.AddRange(body);
    }

    private static void EncodeSubId(List<byte> buf, int v)
    {
        if (v < 128) { buf.Add((byte)v); return; }
        // base-128, high bit set on all but last
        var bytes = new List<byte>();
        bytes.Add((byte)(v & 0x7F));
        v >>= 7;
        while (v > 0)
        {
            bytes.Insert(0, (byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
        buf.AddRange(bytes);
    }

    private static void AppendNull(List<byte> buf)
    {
        buf.Add(0x05);
        buf.Add(0x00);
    }

    private static void AppendLength(List<byte> buf, int len)
    {
        buf.AddRange(EncodeLength(len));
    }

    private static byte[] EncodeLength(int len)
    {
        if (len < 128) return new[] { (byte)len };
        var bytes = new List<byte>();
        int tmp = len;
        var rev = new List<byte>();
        while (tmp > 0) { rev.Add((byte)(tmp & 0xFF)); tmp >>= 8; }
        rev.Reverse();
        bytes.Add((byte)(0x80 | rev.Count));
        bytes.AddRange(rev);
        return bytes.ToArray();
    }

    private static void Wrap(List<byte> dest, byte tag, List<byte> content)
    {
        dest.Add(tag);
        dest.AddRange(EncodeLength(content.Count));
        dest.AddRange(content);
    }

    private static int ReadLength(byte[] data, ref int idx)
    {
        if (data[idx] < 128)
        {
            int l = data[idx++];
            return l;
        }
        int numBytes = data[idx++] & 0x7F;
        int result = 0;
        for (int i = 0; i < numBytes; i++) result = (result << 8) | data[idx++];
        return result;
    }
}
