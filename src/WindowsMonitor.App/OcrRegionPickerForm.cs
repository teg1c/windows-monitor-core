namespace WindowsMonitor.App;

public sealed class OcrRegionPickerForm : Form
{
    private readonly Bitmap _image;
    private readonly PreviewCanvas _preview;
    private Rectangle? _selection;

    public Rectangle? SelectedRegion { get; private set; }

    public OcrRegionPickerForm(Bitmap image, Rectangle? initialRegion = null)
    {
        _image = new Bitmap(image);
        _selection = NormalizeRegion(initialRegion, _image.Size);
        SelectedRegion = _selection;
        _preview = new PreviewCanvas(_image, _selection);

        Text = "文字识别预览与区域选择";
        Size = new Size(1080, 760);
        MinimumSize = new Size(860, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;
        ShowInTaskbar = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Text = "按住鼠标左键拖拽选择识别区域；不选择区域时将使用整张图。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            ForeColor = Color.FromArgb(75, 85, 99),
            BackColor = Color.White
        };

        _preview.SelectionChanged += (_, region) =>
        {
            _selection = NormalizeRegion(region, _image.Size);
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            BackColor = Color.White
        };
        var use = new AntdUI.Button { Text = "使用区域", Width = 110, Height = 32, Type = AntdUI.TTypeMini.Primary, Radius = 6 };
        var whole = new AntdUI.Button { Text = "使用整图", Width = 110, Height = 32, Radius = 6 };
        var cancel = new AntdUI.Button { Text = "取消", Width = 90, Height = 32, Radius = 6 };
        use.Click += (_, _) =>
        {
            if (_selection is null)
            {
                MessageBox.Show(this, "请先在预览图上拖拽选择区域，或点击“使用整图”。", "文字识别预览", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectedRegion = _selection;
            DialogResult = DialogResult.OK;
            Close();
        };
        whole.Click += (_, _) =>
        {
            SelectedRegion = null;
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        actions.Controls.Add(use);
        actions.Controls.Add(whole);
        actions.Controls.Add(cancel);

        Controls.Add(_preview);
        Controls.Add(hint);
        Controls.Add(actions);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _preview.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
            _preview.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Rectangle? NormalizeRegion(Rectangle? region, Size imageSize)
    {
        if (region is null)
        {
            return null;
        }

        var clipped = Rectangle.Intersect(region.Value, new Rectangle(Point.Empty, imageSize));
        return clipped.Width > 2 && clipped.Height > 2 ? clipped : null;
    }

    private sealed class PreviewCanvas : Control
    {
        private readonly Bitmap _image;
        private Point? _dragStart;
        private Rectangle? _selection;

        public event EventHandler<Rectangle?>? SelectionChanged;

        public PreviewCanvas(Bitmap image, Rectangle? initialSelection)
        {
            _image = image;
            _selection = initialSelection;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(17, 24, 39);
            TabStop = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            ResizeRedraw = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Focus();
            _dragStart = e.Location;
            _selection = null;
            SelectionChanged?.Invoke(this, null);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
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
            SelectionChanged?.Invoke(this, _selection);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragStart = null;
            _selection = NormalizeRegion(_selection, _image.Size);
            SelectionChanged?.Invoke(this, _selection);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            var imageBounds = GetImageViewBounds();
            e.Graphics.DrawImage(_image, imageBounds);

            if (_selection is null)
            {
                using var borderPen = new Pen(Color.FromArgb(90, 255, 255, 255), 1);
                e.Graphics.DrawRectangle(borderPen, imageBounds);
                return;
            }

            var view = ImageToViewRectangle(_selection.Value);
            using var dimBrush = new SolidBrush(Color.FromArgb(115, 0, 0, 0));
            using var selectionBrush = new SolidBrush(Color.FromArgb(45, 22, 119, 255));
            using var pen = new Pen(Color.FromArgb(22, 119, 255), 2);

            var outside = new Region(imageBounds);
            outside.Exclude(view);
            e.Graphics.FillRegion(dimBrush, outside);
            outside.Dispose();

            e.Graphics.FillRectangle(selectionBrush, view);
            e.Graphics.DrawRectangle(pen, view);
        }

        private Rectangle? ViewToImageRectangle(Rectangle viewRect)
        {
            var imageBounds = GetImageViewBounds();
            var clipped = Rectangle.Intersect(viewRect, imageBounds);
            if (clipped.Width <= 2 || clipped.Height <= 2)
            {
                return null;
            }

            var scaleX = _image.Width / (double)imageBounds.Width;
            var scaleY = _image.Height / (double)imageBounds.Height;
            var x = Math.Clamp((int)Math.Round((clipped.X - imageBounds.X) * scaleX), 0, _image.Width - 1);
            var y = Math.Clamp((int)Math.Round((clipped.Y - imageBounds.Y) * scaleY), 0, _image.Height - 1);
            var width = Math.Clamp((int)Math.Round(clipped.Width * scaleX), 1, _image.Width - x);
            var height = Math.Clamp((int)Math.Round(clipped.Height * scaleY), 1, _image.Height - y);
            return new Rectangle(x, y, width, height);
        }

        private Rectangle ImageToViewRectangle(Rectangle imageRect)
        {
            var imageBounds = GetImageViewBounds();
            var scaleX = imageBounds.Width / (double)_image.Width;
            var scaleY = imageBounds.Height / (double)_image.Height;
            return new Rectangle(
                imageBounds.X + (int)Math.Round(imageRect.X * scaleX),
                imageBounds.Y + (int)Math.Round(imageRect.Y * scaleY),
                Math.Max(1, (int)Math.Round(imageRect.Width * scaleX)),
                Math.Max(1, (int)Math.Round(imageRect.Height * scaleY)));
        }

        private Rectangle GetImageViewBounds()
        {
            var box = ClientRectangle;
            if (box.Width <= 0 || box.Height <= 0 || _image.Width <= 0 || _image.Height <= 0)
            {
                return Rectangle.Empty;
            }

            var imageRatio = _image.Width / (double)_image.Height;
            var boxRatio = box.Width / (double)box.Height;
            if (boxRatio > imageRatio)
            {
                var width = Math.Max(1, (int)Math.Round(box.Height * imageRatio));
                return new Rectangle((box.Width - width) / 2, 0, width, box.Height);
            }

            var height = Math.Max(1, (int)Math.Round(box.Width / imageRatio));
            return new Rectangle(0, (box.Height - height) / 2, box.Width, height);
        }
    }
}
