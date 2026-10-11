using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ImagePicker;

enum Decision { None, Picked, Rejected }

sealed class Item
{
    public required string Path { get; init; }
    public Decision State { get; set; }
    public Panel Frame { get; set; } = null!;
    public PictureBox Pic { get; set; } = null!;
}

static class Program
{
    static readonly string[] Exts = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var cwd = Directory.GetCurrentDirectory();
        var source = args.Length > 0 ? Path.GetFullPath(args[0]) : cwd;

        if (!Directory.Exists(source))
        {
            MessageBox.Show($"Folder not found:\n{source}", "Image Picker");
            return;
        }

        var files = Directory.EnumerateFiles(source)
            .Where(f => Exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), Comparer<string>.Create(StrCmpLogicalW))
            .ToList();

        if (files.Count == 0)
        {
            MessageBox.Show($"No images found in:\n{source}", "Image Picker");
            return;
        }

        var pickedDir = Path.Combine(cwd, "picked");
        Application.Run(new MainForm(source, files, pickedDir));
    }
}

sealed class MainForm : Form
{
    static readonly Color Green = Color.FromArgb(46, 160, 67);
    static readonly Color Red = Color.FromArgb(207, 56, 56);
    static readonly Color Neutral = Color.FromArgb(55, 55, 55);

    readonly List<Item> items;
    readonly string pickedDir;
    int index = -1;
    int loadVersion;
    bool closing;

    readonly PictureBox main = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(18, 18, 18),
    };

    readonly FlowLayoutPanel strip = new()
    {
        Dock = DockStyle.Fill,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        AutoScroll = true,
        BackColor = Color.FromArgb(30, 30, 30),
    };

    readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.Gainsboro,
        BackColor = Color.FromArgb(24, 24, 24),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0),
        Font = new Font("Segoe UI", 10f),
    };

    public MainForm(string source, List<string> files, string pickedDir)
    {
        this.pickedDir = pickedDir;
        Text = $"Image Picker - {source}";
        BackColor = Color.FromArgb(24, 24, 24);
        Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        DoubleBuffered = true;

        items = files.Select(f => new Item
        {
            Path = f,
            State = File.Exists(Path.Combine(pickedDir, Path.GetFileName(f))) ? Decision.Picked : Decision.None,
        }).ToList();

        BuildUi();
        BuildStrip();

        Shown += (_, _) =>
        {
            Go(0);
            LoadThumbnails();
        };
        FormClosed += (_, _) =>
        {
            closing = true;
            main.Image?.Dispose();
        };
    }

    void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.FromArgb(24, 24, 24),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        bar.Controls.Add(MakeButton("\u25C0", Neutral, () => Go(index - 1)), 0, 0);
        bar.Controls.Add(MakeButton("\u2717  Reject (X)", Red, () => Decide(Decision.Rejected)), 1, 0);
        bar.Controls.Add(MakeButton("\u2713  Pick (P)", Green, () => Decide(Decision.Picked)), 2, 0);
        bar.Controls.Add(MakeButton("\u25B6", Neutral, () => Go(index + 1)), 3, 0);

        root.Controls.Add(main, 0, 0);
        root.Controls.Add(bar, 0, 1);
        root.Controls.Add(strip, 0, 2);
        root.Controls.Add(status, 0, 3);
        Controls.Add(root);

        MouseWheel += OnWheel;
        main.MouseWheel += OnWheel;
        strip.MouseWheel += OnWheel;
    }

    Button MakeButton(string text, Color color, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) =>
        {
            onClick();
            ActiveControl = null;
        };
        return b;
    }

    void BuildStrip()
    {
        strip.SuspendLayout();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            int idx = i;

            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(40, 40, 40),
                Cursor = Cursors.Hand,
            };
            var frame = new Panel
            {
                Width = 138,
                Height = 112,
                Margin = new Padding(4),
                Padding = new Padding(7),
                BackColor = StateColor(item.State),
            };
            frame.Controls.Add(pic);
            frame.Paint += (_, e) =>
            {
                if (items[idx] != CurrentItem) return;
                using var pen = new Pen(Color.White, 3);
                e.Graphics.DrawRectangle(pen, 1, 1, frame.Width - 3, frame.Height - 3);
            };
            frame.Click += (_, _) => Go(idx);
            pic.Click += (_, _) => Go(idx);

            item.Pic = pic;
            item.Frame = frame;
            strip.Controls.Add(frame);
        }
        strip.ResumeLayout();
    }

    void LoadThumbnails()
    {
        var snapshot = items.ToList();
        Task.Run(() =>
        {
            foreach (var it in snapshot)
            {
                if (closing) return;
                var bmp = ImageUtil.MakeThumb(it.Path);
                try
                {
                    BeginInvoke(() =>
                    {
                        if (closing || it.Pic.IsDisposed) { bmp?.Dispose(); return; }
                        it.Pic.Image = bmp;
                    });
                }
                catch (InvalidOperationException) { return; }
            }
        });
    }

    Item? CurrentItem => index >= 0 && index < items.Count ? items[index] : null;

    static Color StateColor(Decision d) => d switch
    {
        Decision.Picked => Green,
        Decision.Rejected => Red,
        _ => Neutral,
    };

    void OnWheel(object? sender, MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs h) h.Handled = true;
        Go(index + (e.Delta > 0 ? -1 : 1));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Left: Go(index - 1); return true;
            case Keys.Right: Go(index + 1); return true;
            case Keys.Home: Go(0); return true;
            case Keys.End: Go(items.Count - 1); return true;
            case Keys.P:
            case Keys.Enter:
            case Keys.Up: Decide(Decision.Picked); return true;
            case Keys.X:
            case Keys.Delete:
            case Keys.Down: Decide(Decision.Rejected); return true;
            case Keys.Escape: Close(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Go(int i)
    {
        if (items.Count == 0) return;
        i = Math.Clamp(i, 0, items.Count - 1);
        if (i == index) return;

        var old = CurrentItem;
        index = i;
        old?.Frame.Invalidate();
        CurrentItem!.Frame.Invalidate();

        CenterInStrip(CurrentItem!.Frame);
        ShowMain(items[index].Path);
        UpdateStatus();
    }

    void CenterInStrip(Panel frame)
    {
        int originalLeft = frame.Left - strip.AutoScrollPosition.X;
        int target = originalLeft - (strip.ClientSize.Width - frame.Width) / 2;
        strip.AutoScrollPosition = new Point(Math.Max(0, target), 0);
    }

    async void ShowMain(string path)
    {
        int version = ++loadVersion;
        Image? img = null;
        string? error = null;
        try
        {
            img = await Task.Run(() => ImageUtil.Load(path));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (version != loadVersion || closing || IsDisposed)
        {
            img?.Dispose();
            return;
        }

        var old = main.Image;
        main.Image = img;
        old?.Dispose();

        if (error != null) status.Text = $"Could not open {Path.GetFileName(path)}: {error}";
    }

    void Decide(Decision d)
    {
        var item = CurrentItem;
        if (item == null) return;

        try
        {
            var dest = Path.Combine(pickedDir, Path.GetFileName(item.Path));
            if (d == Decision.Picked)
            {
                Directory.CreateDirectory(pickedDir);
                File.Copy(item.Path, dest, overwrite: true);
            }
            else if (item.State == Decision.Picked && File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Image Picker");
            return;
        }

        item.State = d;
        item.Frame.BackColor = StateColor(d);
        item.Frame.Invalidate();
        UpdateStatus();

        if (index < items.Count - 1) Go(index + 1);
    }

    void UpdateStatus()
    {
        int picked = items.Count(i => i.State == Decision.Picked);
        int rejected = items.Count(i => i.State == Decision.Rejected);
        int left = items.Count - picked - rejected;
        var item = CurrentItem;
        var state = item?.State switch
        {
            Decision.Picked => "  [PICKED]",
            Decision.Rejected => "  [REJECTED]",
            _ => "",
        };
        status.Text = $"{index + 1} / {items.Count}   {(item != null ? Path.GetFileName(item.Path) : "")}{state}" +
                      $"   |   picked {picked}   rejected {rejected}   undecided {left}";
    }
}

static class ImageUtil
{
    public static Image Load(string path)
    {
        var ms = new MemoryStream(File.ReadAllBytes(path));
        var img = Image.FromStream(ms);
        ApplyOrientation(img);
        return img;
    }

    public static Bitmap? MakeThumb(string path)
    {
        try
        {
            using var img = Load(path);
            const int W = 160, H = 120;
            double scale = Math.Min((double)W / img.Width, (double)H / img.Height);
            int w = Math.Max(1, (int)(img.Width * scale));
            int h = Math.Max(1, (int)(img.Height * scale));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(img, 0, 0, w, h);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    static void ApplyOrientation(Image img)
    {
        const int OrientationId = 0x0112;
        if (!img.PropertyIdList.Contains(OrientationId)) return;

        var value = img.GetPropertyItem(OrientationId)?.Value;
        if (value == null || value.Length == 0) return;

        var flip = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.RotateNoneFlipY,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
        if (flip != RotateFlipType.RotateNoneFlipNone)
        {
            img.RotateFlip(flip);
            img.RemovePropertyItem(OrientationId);
        }
    }
}
