using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Used only by the reserved input-integrity test. The window title exposes
// receiver-side observations without writing files from a low-integrity process.
internal sealed class PeerReceiver : Form
{
    private readonly List<string> edges = new List<string>();
    private PeerReceiver()
    {
        Text = "Hover Click peer";
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(540,300,280,160);
    }

    protected override void WndProc(ref Message message)
    {
        string edge = null;
        switch (message.Msg)
        {
            case 0x201: case 0x203: edge = "LD"; break;
            case 0x202: edge = "LU"; break;
            case 0x204: case 0x206: edge = "RD"; break;
            case 0x205: edge = "RU"; break;
        }
        if (edge != null)
        {
            edges.Add(edge);
            Text = "Hover Click peer " + string.Join(",",edges);
        }
        base.WndProc(ref message);
    }

    [STAThread]
    public static void Main() { Application.Run(new PeerReceiver()); }
}
