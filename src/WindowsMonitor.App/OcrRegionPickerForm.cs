namespace WindowsMonitor.App;

public sealed class OcrRegionPickerForm : Form
{
    private readonly Bitmap _image;
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private Point? _dragStart;
    private Rectangle? _selection;

    public Rectangle? SelectedRegion { get; private set; }

    public OcrRegionPickerForm(Bitmap image, Rectangle? initialRegion = null)
    {
        _image = new Bitmap(image);
        SelectedRegion = initialRegion;
        _selection = initialRegion;

        Text = "文字识别预览与区域选择";
        Size = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);

        _preview.Image = _image;
        _preview.Paint += OnPreviewPaint;
        _preview.MouseDown += OnPreviewMouseDown;
        _preview.MouseMove += OnPreviewMouseMove;
        _preview.MouseUp += OnPreviewMouseUp;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        var use = new AntdUI.Button { Text = "使用区域", Width = 110, Height = 32, DialogResult = DialogResult.OK, Type = AntdUI.TTypeMini.Primary, Radius = 6 };
        var whole = new AntdUI.Button { Text = "使用整图", Width = 110, Height = 32, DialogResult = DialogResult.OK, Radius = 6 };
        var cancel = new AntdUI.Button { Text = "取消", Width = 90, Height = 32, DialogResult = DialogResult.Cancel, Radius = 6 };
        use.Click += (_, _) => SelectedRegion = _selection;
        whole.Click += (_, _) => SelectedRegion = null;
        actions.Controls.Add(use);
        actions.Controls.Add(whole);
        actions.Controls.Add(cancel);

        Controls.Add(_preview);
        Controls.Add(actions);
    }

    private void OnPreviewMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragStart = e.Location;
        _selection = null;
    }

    private void OnPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        var start = _dragStart.Value;
        var viewRect = Rectangle.FromLTRB(
            Math.Min(start.X, e.X),
            Math.Min(start.Y, e.Y),
            Math.Max(start.X, e.X),
            Math.Max(start.Y, e.Y));
        _selection = ViewToImageRectangle(viewRect);
        _preview.Invalidate();
    }

    private void OnPreviewMouseUp(object? sender, MouseEventArgs e)
    {
        _dragStart = null;
    }

    private void OnPreviewPaint(object? sender, PaintEventArgs e)
    {
        if (_selection is null)
        {
            return;
        }

        var view = ImageToViewRectangle(_selection.Value);
        using var pen = new Pen(Color.FromArgb(22, 119, 255), 2);
        using var brush = new SolidBrush(Color.FromArgb(50, 22, 119, 255));
        e.Graphics.FillRectangle(brush, view);
        e.Graphics.DrawRectangle(pen, view);
    }

    private Rectangle ViewToImageRectangle(Rectangle viewRect)
    {
        var imageBounds = GetImageViewBounds();
        var clipped = Rectangle.Intersect(viewRect, imageBounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scaleX = _image.Width / (double)imageBounds.Width;
        var scaleY = _image.Height / (double)imageBounds.Height;
        return new Rectangle(
            Math.Clamp((int)((clipped.X - imageBounds.X) * scaleX), 0, _image.Width),
            Math.Clamp((int)((clipped.Y - imageBounds.Y) * scaleY), 0, _image.Height),
            Math.Clamp((int)(clipped.Width * scaleX), 0, _image.Width),
            Math.Clamp((int)(clipped.Height * scaleY), 0, _image.Height));
    }

    private Rectangle ImageToViewRectangle(Rectangle imageRect)
    {
        var imageBounds = GetImageViewBounds();
        var scaleX = imageBounds.Width / (double)_image.Width;
        var scaleY = imageBounds.Height / (double)_image.Height;
        return new Rectangle(
            imageBounds.X + (int)(imageRect.X * scaleX),
            imageBounds.Y + (int)(imageRect.Y * scaleY),
            (int)(imageRect.Width * scaleX),
            (int)(imageRect.Height * scaleY));
    }

    private Rectangle GetImageViewBounds()
    {
        var box = _preview.ClientRectangle;
        var imageRatio = _image.Width / (double)_image.Height;
        var boxRatio = box.Width / (double)box.Height;

        if (boxRatio > imageRatio)
        {
            var width = (int)(box.Height * imageRatio);
            return new Rectangle((box.Width - width) / 2, 0, width, box.Height);
        }

        var height = (int)(box.Width / imageRatio);
        return new Rectangle(0, (box.Height - height) / 2, box.Width, height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
        }

        base.Dispose(disposing);
    }
}
