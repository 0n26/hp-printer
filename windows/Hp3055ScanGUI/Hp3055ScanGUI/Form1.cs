using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Hp3055ScanGUI;

public class MainForm : Form
{
    private readonly TextBox txtIp;
    private readonly ComboBox cbMode;
    private readonly ComboBox cbDpi;
    private readonly ComboBox cbFormat;
    private readonly TextBox txtOut;
    private readonly Button btnBrowse;
    private readonly Button btnScan;
    private readonly Label lblStatus;
    private readonly PictureBox picPreview;
    private readonly TextBox txtLog;

    public MainForm()
    {
        Text = "HP LaserJet 3055 — сканер";
        Width = 760;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        int x = 12, y = 12, lw = 90, cw = 200;

        Controls.Add(new Label { Text = "IP принтера:", Left = x, Top = y + 3, Width = lw });
        txtIp = new TextBox { Left = x + lw, Top = y, Width = cw, Text = "192.168.1.118" };
        Controls.Add(txtIp);
        y += 32;

        Controls.Add(new Label { Text = "Режим:", Left = x, Top = y + 3, Width = lw });
        cbMode = new ComboBox { Left = x + lw, Top = y, Width = cw, DropDownStyle = ComboBoxStyle.DropDownList };
        cbMode.Items.AddRange(new object[] { "gray", "color", "lineart" });
        cbMode.SelectedIndex = 0;
        Controls.Add(cbMode);
        y += 32;

        Controls.Add(new Label { Text = "DPI:", Left = x, Top = y + 3, Width = lw });
        cbDpi = new ComboBox { Left = x + lw, Top = y, Width = cw, DropDownStyle = ComboBoxStyle.DropDownList };
        cbDpi.Items.AddRange(new object[] { "75", "100", "150", "200", "300", "600", "1200" });
        cbDpi.SelectedIndex = 4; // 300
        Controls.Add(cbDpi);
        y += 32;

        Controls.Add(new Label { Text = "Формат:", Left = x, Top = y + 3, Width = lw });
        cbFormat = new ComboBox { Left = x + lw, Top = y, Width = cw, DropDownStyle = ComboBoxStyle.DropDownList };
        cbFormat.Items.AddRange(new object[] { "pdf", "jpeg", "png" });
        cbFormat.SelectedIndex = 0;
        Controls.Add(cbFormat);
        y += 32;

        Controls.Add(new Label { Text = "Файл:", Left = x, Top = y + 3, Width = lw });
        txtOut = new TextBox { Left = x + lw, Top = y, Width = 360 };
        Controls.Add(txtOut);
        btnBrowse = new Button { Text = "Обзор…", Left = x + lw + 368, Top = y - 1, Width = 90 };
        btnBrowse.Click += (_, _) => Browse();
        Controls.Add(btnBrowse);
        y += 32;

        btnScan = new Button { Text = "Сканировать", Left = x, Top = y, Width = 120, Height = 30 };
        btnScan.Click += async (_, _) => await RunScan();
        Controls.Add(btnScan);
        lblStatus = new Label { Text = "Готово", Left = x + 130, Top = y + 6, Width = 400 };
        Controls.Add(lblStatus);
        y += 40;

        picPreview = new PictureBox { Left = x, Top = y, Width = 300, Height = 400, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(picPreview);

        txtLog = new TextBox { Left = x + 312, Top = y, Width = 420, Height = 400, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
        Controls.Add(txtLog);
    }

    private void Browse()
    {
        using var dlg = new SaveFileDialog();
        dlg.Filter = "PDF (*.pdf)|*.pdf|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png";
        dlg.FileName = txtOut.Text;
        if (dlg.ShowDialog() == DialogResult.OK) txtOut.Text = dlg.FileName;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { Invoke(new Action(() => Log(msg))); return; }
        txtLog.AppendText(msg + Environment.NewLine);
    }

    private async Task RunScan()
    {
        btnScan.Enabled = false;
        lblStatus.Text = "Сканирование…";
        txtLog.Clear();
        try
        {
            var ip = txtIp.Text.Trim();
            int dpi = int.Parse(cbDpi.Text);
            string mode = cbMode.Text.ToLowerInvariant();
            string format = cbFormat.Text.ToLowerInvariant();
            string outPath = txtOut.Text.Trim();
            if (string.IsNullOrEmpty(outPath))
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scans");
                Directory.CreateDirectory(dir);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                outPath = Path.Combine(dir, $"scan-{stamp}.{(format == "pdf" ? "pdf" : (mode == "lineart" ? "png" : "jpg"))}");
                txtOut.Text = outPath;
            }

            Log($"IP={ip} mode={mode} dpi={dpi} format={format}");
            var result = await Task.Run(() => PmlScanner.Scan(ip, dpi,
                mode switch { "lineart" => PmlScanner.ModeLineart, "color" => PmlScanner.ModeColor, _ => PmlScanner.ModeGray },
                PmlScanner.CompJpeg, 10));
            Log($"Получено: {(result.Jpeg != null && result.Jpeg.Length >= 2 && result.Jpeg[0] == 0xFF && result.Jpeg[1] == 0xD8 ? "JPEG" : "bitmap")} {result.Jpeg?.Length} байт, {result.Width}x{result.Height}");

            bool isJpeg = result.Jpeg != null && result.Jpeg.Length >= 2 && result.Jpeg[0] == 0xFF && result.Jpeg[1] == 0xD8;
            if (!isJpeg)
            {
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

            txtOut.Text = outPath;
            lblStatus.Text = $"Готово: {result.Width}x{result.Height}";
            Log($"Сохранено: {outPath}");
            try
            {
                if (File.Exists(outPath)) picPreview.Image = new Bitmap(outPath);
            }
            catch { }
        }
        catch (Exception ex)
        {
            lblStatus.Text = "Ошибка";
            Log("ОШИБКА: " + ex);
            MessageBox.Show(ex.Message, "Ошибка сканирования", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnScan.Enabled = true;
        }
    }

    private static void WritePdf(string path, byte[] jpeg, int w, int h, int dpi, bool color)
    {
        double pw = w * 72.0 / dpi;
        double ph = h * 72.0 / dpi;
        string cs = color ? "/DeviceRGB" : "/DeviceGray";
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        sw.Write("%PDF-1.4\n");
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
        foreach (var o in new[] { o1, o2, o3, o4, o5 })
            sw.Write($"{o:D10} 00000 n \n");
        sw.Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        sw.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }
}
