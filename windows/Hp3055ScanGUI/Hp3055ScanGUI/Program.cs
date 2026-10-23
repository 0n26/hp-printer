using System;
using System.Windows.Forms;

namespace Hp3055ScanGUI;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
