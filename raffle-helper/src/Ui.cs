// 추첨 도우미 - 화면
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Raffle {

 // ───────────────────────── 색·글꼴
 static class Th {
  public static readonly Color Bg = Color.FromArgb(245, 245, 247), Side = Color.FromArgb(236, 236, 240), Card = Color.White, Sep = Color.FromArgb(229, 229, 234),
   Ink = Color.FromArgb(29, 29, 31), Sub = Color.FromArgb(142, 142, 147), Fill = Color.FromArgb(237, 237, 240), Accent = Color.FromArgb(255, 106, 31),
   Blue = Color.FromArgb(46, 139, 216), Orange = Color.FromArgb(240, 138, 60), Red = Color.FromArgb(229, 72, 77), Yellow = Color.FromArgb(232, 164, 0),
   Green = Color.FromArgb(47, 163, 90), NavSel = Color.FromArgb(220, 220, 226), SelRow = Color.FromArgb(255, 244, 236), HighQ = Color.FromArgb(36, 86, 199);
  static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
  public static Font F(float size, bool bold = false) {
   string k = size + (bold ? "b" : "");
   Font f; if (!cache.TryGetValue(k, out f)) { f = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular); cache[k] = f; }
   return f;
  }
  public static GraphicsPath Round(RectangleF r, float rad) {
   var p = new GraphicsPath(); float d = rad * 2; if (d <= 0) { p.AddRectangle(r); return p; }
   p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
  }
  public static Color SecColor(string sec) { return sec == "특별" ? Blue : sec == "선착순" ? Orange : sec == "제외" ? Red : Yellow; }
  public static string SecName(string sec) { return sec == "특별" ? "특별 후보" : sec == "선착순" ? "선착순" : sec == "제외" ? "제외" : "직접 검토"; }
  public static void Open(string target) { try { Process.Start(target); } catch { } }
 }

 // ───────────────────────── 아이콘 (검은 바탕 + 주황 티켓 + 흰 별)
 public static class Logo {
  public static void Draw(Graphics g, float s) {
   g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
   using (var bg = Th.Round(new RectangleF(0, 0, s, s), s * 0.23f)) using (var b = new SolidBrush(Color.FromArgb(29, 29, 31))) g.FillPath(b, bg);
   float x = s * 0.18f, y = s * 0.31f, w = s * 0.64f, h = s * 0.38f, notch = s * 0.085f;
   using (var t = new GraphicsPath()) {
    t.AddLine(x, y, x + w, y); t.AddArc(x + w - notch, y + h / 2 - notch, notch * 2, notch * 2, 270, -180); t.AddLine(x + w, y + h, x, y + h); t.AddArc(x - notch, y + h / 2 - notch, notch * 2, notch * 2, 90, -180); t.CloseFigure();
    using (var b = new SolidBrush(Color.FromArgb(255, 106, 31))) g.FillPath(b, t);
   }
   float cx = s * 0.5f, cy = s * 0.5f, ro = s * 0.12f, ri = ro * 0.45f; var pts = new PointF[10];
   for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float r = i % 2 == 0 ? ro : ri; pts[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r)); }
   g.FillPolygon(Brushes.White, pts);
  }
  public static Bitmap Bitmap(int z) { var bmp = new Bitmap(z, z, PixelFormat.Format32bppArgb); using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); Draw(g, z); } return bmp; }
  static void Dib(Bitmap bmp, Stream s) {
   int w = bmp.Width, h = bmp.Height; var bw = new BinaryWriter(s);
   bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32); for (int i = 0; i < 6; i++) bw.Write(0);
   for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++) { var c = bmp.GetPixel(x, y); bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A); }
   int stride = ((w + 31) / 32) * 4;
   for (int y = h - 1; y >= 0; y--) { var row = new byte[stride]; for (int x = 0; x < w; x++) if (bmp.GetPixel(x, y).A < 128) row[x / 8] |= (byte)(0x80 >> (x % 8)); bw.Write(row); }
   bw.Flush();
  }
  public static void SaveIco(string path) {
   int[] sizes = { 16, 24, 32, 48, 64, 128, 256 }; var data = new List<byte[]>();
   foreach (int z in sizes) using (var bmp = Bitmap(z)) using (var ms = new MemoryStream()) { if (z >= 256) bmp.Save(ms, ImageFormat.Png); else Dib(bmp, ms); data.Add(ms.ToArray()); }
   using (var fs = File.Create(path)) using (var w = new BinaryWriter(fs)) {
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int off = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++) { byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(data[i].Length); w.Write(off); off += data[i].Length; }
    foreach (var d in data) w.Write(d);
   }
  }
 }

 // ───────────────────────── 화면 부품
 public class Box : Panel {
  public int Radius = 12; public Color FillColor = Th.Card;
  public Box() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Th.Card; }
  protected override void OnPaintBackground(PaintEventArgs e) {
   e.Graphics.Clear(Parent == null ? Th.Bg : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
   using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), Radius)) using (var b = new SolidBrush(FillColor)) e.Graphics.FillPath(b, p);
  }
 }
 public class Btn : Control {
  public enum K { Gray, Primary, Dark, Link, Plain, Ghost }
  public K Kind; public string Badge = ""; bool hover;
  public Btn(string text, K kind, Action click) {
   SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
   Text = text; Kind = kind; Cursor = Cursors.Hand; Font = Th.F(9.5f, true); Height = 32; Margin = new Padding(0);
   if (click != null) Click += (s, e) => { if (Enabled) click(); };
   FitWidth();
  }
  public Btn Big() { Font = Th.F(10.5f, true); Height = 40; FitWidth(); return this; }
  public void FitWidth() { var sz = TextRenderer.MeasureText(Text ?? "", Font); int pad = Kind == K.Link || Kind == K.Plain ? 6 : 30; Width = sz.Width + pad + (Badge != "" ? TextRenderer.MeasureText(Badge, Th.F(8.5f, true)).Width + 14 : 0); }
  protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
  protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Parent == null ? Th.Bg : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
   Color bg, fg;
   switch (Kind) {
    case K.Primary: bg = Th.Accent; fg = Color.White; break;
    case K.Dark: bg = Th.Ink; fg = Color.White; break;
    case K.Link: bg = Color.Empty; fg = Th.Accent; break;
    case K.Plain: bg = Color.Empty; fg = Th.Sub; break;
    case K.Ghost: bg = Color.Empty; fg = Th.Ink; break;
    default: bg = Th.Fill; fg = Th.Ink; break;
   }
   if (!Enabled) { if (bg != Color.Empty) bg = Th.Fill; fg = Color.FromArgb(180, 180, 186); }
   else if (hover) { if (bg == Color.Empty) fg = Kind == K.Link ? Color.FromArgb(214, 80, 10) : Th.Ink; else bg = ControlPaint.Dark(bg, 0.04f); if (Kind == K.Gray) bg = Color.FromArgb(226, 226, 231); }
   if (bg != Color.Empty) using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), Math.Min(8, Height / 2f))) using (var b = new SolidBrush(bg)) g.FillPath(b, p);
   var tsz = TextRenderer.MeasureText(Text ?? "", Font);
   int bw = Badge != "" ? TextRenderer.MeasureText(Badge, Th.F(8.5f, true)).Width + 10 : 0;
   int tx = (Width - tsz.Width - (bw > 0 ? bw + 4 : 0)) / 2;
   TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, tsz.Width + 2, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
   if (bw > 0) {
    var r = new RectangleF(tx + tsz.Width + 4, Height / 2f - 9, bw, 18);
    using (var p = Th.Round(r, 9)) using (var b = new SolidBrush(Th.Accent)) g.FillPath(b, p);
    TextRenderer.DrawText(g, Badge, Th.F(8.5f, true), Rectangle.Round(r), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
   }
  }
 }
 public class NavItem : Control {
  public int Icon; public bool Selected;
  public NavItem(string text, int icon) { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Text = text; Icon = icon; Height = 36; Cursor = Cursors.Hand; Font = Th.F(9.5f, true); }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Th.Side); g.SmoothingMode = SmoothingMode.AntiAlias;
   if (Selected) using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8)) using (var b = new SolidBrush(Th.NavSel)) g.FillPath(b, p);
   using (var pen = new Pen(Th.Ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
    float x = 12, y = Height / 2f;
    if (Icon == 0) { for (int i = -1; i <= 1; i++) { g.FillEllipse(Brushes.Black, x, y + i * 5 - 1, 2.2f, 2.2f); g.DrawLine(pen, x + 6, y + i * 5, x + 16, y + i * 5); } }
    else if (Icon == 1) { g.DrawEllipse(pen, x, y - 8, 16, 16); g.DrawLine(pen, x + 8, y - 4, x + 8, y); g.DrawLine(pen, x + 8, y, x + 11, y + 2); }
    else { g.DrawEllipse(pen, x + 4.5f, y - 3.5f, 7, 7); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, x + 8 + (float)Math.Cos(a) * 6, y + (float)Math.Sin(a) * 6, x + 8 + (float)Math.Cos(a) * 8.2f, y + (float)Math.Sin(a) * 8.2f); } }
   }
   TextRenderer.DrawText(g, Text, Font, new Rectangle(40, 0, Width - 40, Height), Th.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
  }
 }
 public class Tile : Control {
  public string Label = "", Sec = ""; public int Count; public bool Selected; bool hover;
  public Tile(string label, string sec) { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Label = label; Sec = sec; Cursor = Cursors.Hand; Height = 78; }
  protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); } protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Th.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
   var r = new RectangleF(1, 1, Width - 3, Height - 3);
   using (var p = Th.Round(r, 12)) { using (var b = new SolidBrush(hover && !Selected ? Color.FromArgb(252, 252, 253) : Th.Card)) g.FillPath(b, p); if (Selected) using (var pen = new Pen(Th.Ink, 2)) g.DrawPath(pen, p); }
   using (var b = new SolidBrush(Th.SecColor(Sec))) g.FillEllipse(b, 17, 19, 8, 8);
   TextRenderer.DrawText(g, Label, Th.F(9.5f, true), new Point(29, 14), Th.Sub);
   TextRenderer.DrawText(g, Count.ToString("#,##0"), Th.F(19, true), new Point(13, 34), Th.Ink);
  }
 }
 public class Seg : Control {
  public string[] Items; public int Index; public event EventHandler Changed;
  public Seg(params string[] items) { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Items = items; Height = 30; Font = Th.F(9, true); Cursor = Cursors.Hand; Width = items.Sum(s => TextRenderer.MeasureText(s, Font).Width + 22) + 4; }
  int ItemW(int i) { return TextRenderer.MeasureText(Items[i], Font).Width + 22; }
  protected override void OnMouseDown(MouseEventArgs e) { int x = 2; for (int i = 0; i < Items.Length; i++) { int w = ItemW(i); if (e.X >= x && e.X < x + w) { if (Index != i) { Index = i; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } break; } x += w; } }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Parent == null ? Th.Bg : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8)) using (var b = new SolidBrush(Th.Fill)) g.FillPath(b, p);
   int x = 2;
   for (int i = 0; i < Items.Length; i++) {
    int w = ItemW(i); var r = new Rectangle(x, 2, w, Height - 5);
    if (i == Index) using (var p = Th.Round(r, 6)) { using (var sh = new SolidBrush(Color.FromArgb(25, 0, 0, 0))) { g.TranslateTransform(0, 1); g.FillPath(sh, p); g.TranslateTransform(0, -1); } g.FillPath(Brushes.White, p); }
    TextRenderer.DrawText(g, Items[i], Font, r, i == Index ? Th.Ink : Th.Sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    x += w;
   }
  }
 }
 public class Line : Control {
  public float Value; public Line() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Height = 6; }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Parent == null ? Th.Bg : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f)) using (var b = new SolidBrush(Color.FromArgb(225, 225, 230))) g.FillPath(b, p);
   float w = Math.Max(Height, (Width - 1) * Math.Max(0, Math.Min(1, Value)));
   using (var p = Th.Round(new RectangleF(0, 0, w, Height - 1), Height / 2f)) using (var b = new SolidBrush(Th.Ink)) g.FillPath(b, p);
  }
 }
 public class DropZone : Control {
  public DropZone() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Size = new Size(560, 300); }
  protected override void OnPaint(PaintEventArgs e) {
   var g = e.Graphics; g.Clear(Th.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
   using (var p = Th.Round(new RectangleF(1, 1, Width - 3, Height - 3), 18)) { g.FillPath(Brushes.White, p); using (var pen = new Pen(Color.FromArgb(207, 207, 214), 2) { DashPattern = new float[] { 4, 3 } }) g.DrawPath(pen, p); }
   float cx = Width / 2f, cy = 92;
   using (var pen = new Pen(Color.FromArgb(180, 180, 188), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
    g.DrawLine(pen, cx, cy + 12, cx, cy - 16); g.DrawLines(pen, new[] { new PointF(cx - 9, cy - 7), new PointF(cx, cy - 16), new PointF(cx + 9, cy - 7) });
    g.DrawLines(pen, new[] { new PointF(cx - 22, cy + 6), new PointF(cx - 22, cy + 20), new PointF(cx + 22, cy + 20), new PointF(cx + 22, cy + 6) });
   }
   TextRenderer.DrawText(g, "네이버폼 raw 파일을 끌어다 놓으세요", Th.F(14, true), new Rectangle(0, 130, Width, 30), Th.Ink, TextFormatFlags.HorizontalCenter);
   TextRenderer.DrawText(g, "예) kt M모바일 ○월 ○차 소문내기 이벤트_raw data.xlsx", Th.F(9.5f), new Rectangle(0, 164, Width, 22), Th.Sub, TextFormatFlags.HorizontalCenter);
  }
 }
 public class NumBox : TextBox {
  public NumBox(int v, int w) { Text = v.ToString(); Width = w; TextAlign = HorizontalAlignment.Right; BorderStyle = BorderStyle.FixedSingle; BackColor = Th.Fill; Font = Th.F(9.5f); }
  protected override void OnKeyPress(KeyPressEventArgs e) { if (!Char.IsDigit(e.KeyChar) && !Char.IsControl(e.KeyChar)) e.Handled = true; base.OnKeyPress(e); }
  public int Value(int def) { int n; return Int32.TryParse(Text.Replace(",", "").Trim(), out n) ? n : def; }
 }
 // 위아래 구분선이 있는 한 줄 (설정·정보 묶음 안)
 public class Row : Panel {
  public bool TopLine; public Label Key = new Label(), Val = new Label(); public FlowLayoutPanel Tools = new FlowLayoutPanel();
  public Row(string key, int keyW) {
   Height = 46; BackColor = Th.Card; DoubleBuffered = true; Padding = new Padding(16, 1, 12, 0);
   Key.Text = key; Key.AutoSize = false; Key.Width = keyW; Key.Dock = DockStyle.Left; Key.TextAlign = ContentAlignment.MiddleLeft; Key.ForeColor = Th.Sub; Key.Font = Th.F(9.5f); Key.BackColor = Th.Card;
   Tools.AutoSize = true; Tools.AutoSizeMode = AutoSizeMode.GrowAndShrink; Tools.Dock = DockStyle.Right; Tools.WrapContents = false; Tools.BackColor = Th.Card; Tools.Padding = new Padding(0, 7, 0, 0);
   Val.Dock = DockStyle.Fill; Val.TextAlign = ContentAlignment.MiddleLeft; Val.AutoEllipsis = true; Val.Font = Th.F(9.5f); Val.ForeColor = Th.Ink; Val.BackColor = Th.Card; Val.UseMnemonic = false;
   Controls.Add(Val); Controls.Add(Tools); Controls.Add(Key);
  }
  public Row Add(Control c) { c.Margin = new Padding(6, 0, 0, 0); Tools.Controls.Add(c); return this; }
  protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if (TopLine) using (var pen = new Pen(Th.Sep)) e.Graphics.DrawLine(pen, 16, 0, Width, 0); }
 }
 public class Group : Box {
  public List<Row> Rows = new List<Row>();
  public Group() { Padding = new Padding(4, 4, 4, 4); }
  public Row AddRow(Row r) { r.TopLine = Rows.Count > 0; r.Dock = DockStyle.Top; Rows.Add(r); Controls.Add(r); r.BringToFront(); Height = Rows.Sum(x => x.Height) + 8; return r; }
 }

 // ───────────────────────── 결과 표
 public class ListGrid : DataGridView {
  public ListGrid() {
   DoubleBuffered = true; BorderStyle = BorderStyle.None; BackgroundColor = Th.Card; RowHeadersVisible = false; AllowUserToAddRows = false; AllowUserToDeleteRows = false; AllowUserToResizeRows = false;
   ReadOnly = true; SelectionMode = DataGridViewSelectionMode.FullRowSelect; MultiSelect = false; CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; GridColor = Th.Sep;
   EnableHeadersVisualStyles = false; ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; ColumnHeadersHeight = 34;
   ColumnHeadersDefaultCellStyle.BackColor = Th.Card; ColumnHeadersDefaultCellStyle.ForeColor = Th.Sub; ColumnHeadersDefaultCellStyle.Font = Th.F(8.5f, true); ColumnHeadersDefaultCellStyle.SelectionBackColor = Th.Card; ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
   DefaultCellStyle.Font = Th.F(9.5f); DefaultCellStyle.ForeColor = Th.Ink; DefaultCellStyle.BackColor = Th.Card; DefaultCellStyle.SelectionBackColor = Th.SelRow; DefaultCellStyle.SelectionForeColor = Th.Ink; DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
   RowTemplate.Height = 48; AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; ScrollBars = ScrollBars.Vertical; StandardTab = true;
  }
  public DataGridViewTextBoxColumn Col(string name, string head, int width, bool fill, DataGridViewContentAlignment align) {
   var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = head, SortMode = DataGridViewColumnSortMode.NotSortable };
   if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.FillWeight = width; } else c.Width = width;
   c.DefaultCellStyle.Alignment = align; c.HeaderCell.Style.Alignment = align;
   Columns.Add(c); return c;
  }
 }

 // ───────────────────────── 입력 창 (작은 대화상자)
 public class Ask : Form {
  public List<Control> Inputs = new List<Control>(); TableLayoutPanel t = new TableLayoutPanel();
  public Ask(string title, string info) {
   Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
   BackColor = Th.Bg; Font = Th.F(9.5f); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(480, 100); Padding = new Padding(20, 16, 20, 16);
   t.Dock = DockStyle.Fill; t.ColumnCount = 2; t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); t.AutoSize = true; Controls.Add(t);
   if (!String.IsNullOrEmpty(info)) { var l = new Label { Text = info, AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Th.Sub, Margin = new Padding(0, 0, 0, 10) }; t.Controls.Add(l, 0, 0); t.SetColumnSpan(l, 2); }
  }
  public T Field<T>(string label, T c) where T : Control {
   c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 3, 0, 3); c.Font = Th.F(9.5f);
   var tb = c as TextBox; if (tb != null) { tb.BorderStyle = BorderStyle.FixedSingle; }
   t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Th.Ink, Margin = new Padding(0, 8, 8, 0) }); t.Controls.Add(c); Inputs.Add(c); return c;
  }
  public TextBox Text1(string label, string value) { return Field(label, new TextBox { Text = value ?? "" }); }
  public bool Run(IWin32Window owner) {
   var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 10, 0, 0) };
   var ok = new Btn("확인", Btn.K.Dark, () => { DialogResult = DialogResult.OK; Close(); }); var cancel = new Btn("취소", Btn.K.Gray, () => { DialogResult = DialogResult.Cancel; Close(); });
   ok.Margin = new Padding(8, 0, 0, 0); bar.Controls.Add(ok); bar.Controls.Add(cancel); Controls.Add(bar);
   var hidden = new Button { DialogResult = DialogResult.OK, Size = new Size(0, 0), Location = new Point(-50, -50) }; Controls.Add(hidden); AcceptButton = hidden;
   var esc = new Button { DialogResult = DialogResult.Cancel, Size = new Size(0, 0), Location = new Point(-50, -50) }; Controls.Add(esc); CancelButton = esc;
   PerformLayout(); ClientSize = new Size(480, Padding.Vertical + t.PreferredSize.Height + 54);
   return ShowDialog(owner) == DialogResult.OK;
  }
 }

 // ───────────────────────── 직접 검토 시트 (메인 위에 뜨는 창)
 public class DimForm : Form {
  public DimForm(Form owner) { FormBorderStyle = FormBorderStyle.None; BackColor = Color.Black; Opacity = 0.28; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Bounds = owner.RectangleToScreen(owner.ClientRectangle); }
 }
 public class ReviewSheet : Form {
  readonly MainForm main; FlowLayoutPanel list = new FlowLayoutPanel(); Label sub = new Label(); Panel scroll = new Panel();
  [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
  public ReviewSheet(MainForm m) {
   main = m; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; BackColor = Th.Bg; Font = Th.F(9.5f); KeyPreview = true;
   Width = 760; Padding = new Padding(24, 20, 24, 20);
   var head = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Th.Bg };
   head.Controls.Add(new Label { Text = "직접 검토", Font = Th.F(15, true), AutoSize = true, Location = new Point(0, 0), BackColor = Th.Bg });
   sub.AutoSize = true; sub.Location = new Point(2, 32); sub.ForeColor = Th.Sub; sub.BackColor = Th.Bg; head.Controls.Add(sub);
   var done = new Btn("완료", Btn.K.Dark, () => Close()); head.Controls.Add(done);
   head.Resize += (s, e) => done.Location = new Point(head.Width - done.Width, 4);
   scroll.Dock = DockStyle.Fill; scroll.AutoScroll = true; scroll.BackColor = Th.Bg;
   list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AutoSize = true; list.AutoSizeMode = AutoSizeMode.GrowAndShrink; list.BackColor = Th.Bg; list.Location = new Point(0, 0);
   scroll.Controls.Add(list); Controls.Add(scroll); Controls.Add(head);
   KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
   Build();
  }
  int InnerW { get { return Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2; } }
  public void Build() {
   list.SuspendLayout(); list.Controls.Clear();
   var ps = main.Participants; var all = ps.SelectMany(p => p.Posts).ToList();
   var manual = all.Where(p => p.Kind == RKind.Manual && (p.State == PState.Review || p.Decided)).ToList();
   var copy = all.Where(p => p.CopyOf != null && (p.State == PState.Review || p.Decided || p.Reason == "복붙의심")).ToList();
   var cmt = ps.Where(p => !p.CommentMatched && main.CommentsChecked).ToList();
   if (manual.Count > 0) Section("직접 확인 · 글을 열어 조회수와 정성도를 넣어 주세요", manual.Select(p => ManualRow(p)).ToList());
   if (copy.Count > 0) Section("복붙의심 · 먼저 올라온 글은 그대로 두었어요", copy.Select(p => CopyRow(p)).ToList());
   if (cmt.Count > 0) Section("댓글 미확인 · 이벤트 글 댓글에서 닉네임을 못 찾았어요", cmt.Select(p => CommentRow(p)).ToList());
   if (list.Controls.Count == 0) list.Controls.Add(new Label { Text = "검토할 게 없어요.", AutoSize = true, ForeColor = Th.Sub, Margin = new Padding(4, 10, 0, 0) });
   list.ResumeLayout();
   int left = Engine.ReviewCount(ps); sub.Text = left > 0 ? left + "건 남음 · 처리하면 바로 반영돼요" : "다 끝났어요";
   int h = Padding.Vertical + 58 + list.PreferredSize.Height + 8; Height = Math.Min(Math.Max(220, h), main.ClientSize.Height - 40);
   Region = Region.FromHrgn(CreateRoundRectRgn(0, -20, Width + 1, Height + 1, 26, 26));
  }
  void Section(string title, List<Control> rows) {
   var cap = new Label { Text = title, AutoSize = true, ForeColor = Th.Sub, Font = Th.F(9, true), Margin = new Padding(4, list.Controls.Count == 0 ? 0 : 14, 0, 6), BackColor = Th.Bg };
   list.Controls.Add(cap);
   var g = new Box { Width = InnerW, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2) };
   int y = 4; bool firstRow = true;
   foreach (var r in rows) { r.Location = new Point(4, y); r.Width = g.Width - 8; if (!firstRow) r.Paint += (s, e) => { using (var pen = new Pen(Th.Sep)) e.Graphics.DrawLine(pen, 16, 0, ((Control)s).Width, 0); }; g.Controls.Add(r); y += r.Height; firstRow = false; }
   g.Height = y + 4; list.Controls.Add(g);
  }
  Panel RowBase(string nick, string subText, out FlowLayoutPanel acts, out Label subLbl) {
   var p = new Panel { Height = 62, BackColor = Th.Card };
   var nickLbl = new Label { Text = nick, Font = Th.F(10, true), AutoSize = true, Location = new Point(16, 11), BackColor = Th.Card, UseMnemonic = false };
   p.Controls.Add(nickLbl);
   subLbl = new Label { Text = subText, ForeColor = Th.Sub, AutoSize = false, AutoEllipsis = true, Location = new Point(16, 34), Size = new Size(200, 18), BackColor = Th.Card, UseMnemonic = false };
   p.Controls.Add(subLbl);
   acts = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Th.Card };
   p.Controls.Add(acts);
   var a = acts; var sl = subLbl;
   p.Layout += (s, e) => { a.Location = new Point(p.Width - a.Width - 14, (p.Height - a.Height) / 2); sl.Width = Math.Max(80, a.Left - 30); };
   return p;
  }
  Label Done(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Th.Green, Font = Th.F(9.5f, true), Margin = new Padding(0, 8, 4, 0), BackColor = Th.Card }; }
  Control ManualRow(Post post) {
   FlowLayoutPanel acts; Label subL;
   bool plainSite = post.ManualWhy.EndsWith("자동으로 읽히지 않아요");
   var p = RowBase(post.P.Nick, post.P.Id + " · " + post.P.No + "번 · " + (plainSite ? post.Community : post.ManualWhy), out acts, out subL);
   var open = new Btn("글 열기 ↗", Btn.K.Link, () => Th.Open(post.FinalUrl != "" ? post.FinalUrl : post.Url)); open.Font = Th.F(9, true); open.FitWidth(); open.Height = 22;
   p.Controls.Add(open); open.Location = new Point(16 + TextRenderer.MeasureText(post.P.Nick, Th.F(10, true)).Width + 8, 10);
   if (post.Decided) {
    Decision d; main.Decisions.TryGetValue("post:" + post.Key, out d);
    acts.Controls.Add(Done(d != null && d.Accept ? "✓ 조회수 " + d.Views.ToString("#,##0") + " · " + d.Quality : "✓ 확인 불가 → 제외"));
    acts.Controls.Add(Undo("post:" + post.Key));
   } else {
    var views = new NumBox(0, 64) { Text = post.Views >= 0 ? post.Views.ToString() : "", Margin = new Padding(4, 4, 6, 0) };
    var seg = new Seg("상", "중", "하") { Index = post.Quality == "상" ? 0 : post.Quality == "하" ? 2 : 1, Margin = new Padding(0, 1, 6, 0) };
    acts.Controls.Add(new Label { Text = "조회수", AutoSize = true, ForeColor = Th.Sub, Margin = new Padding(0, 8, 0, 0), BackColor = Th.Card });
    acts.Controls.Add(views); acts.Controls.Add(seg);
    acts.Controls.Add(new Btn("확인 불가", Btn.K.Plain, () => Decide("post:" + post.Key, new Decision { Accept = false })) { Margin = new Padding(0, 0, 6, 0) });
    acts.Controls.Add(new Btn("확인", Btn.K.Dark, () => {
     int v = views.Value(-1); if (v < 0) { MessageBox.Show(this, "조회수를 숫자로 넣어 주세요.", "직접 검토"); views.Focus(); return; }
     Decide("post:" + post.Key, new Decision { Accept = true, Views = v, Quality = seg.Items[seg.Index] });
    }));
   }
   return p;
  }
  Control CopyRow(Post post) {
   FlowLayoutPanel acts; Label subL; var orig = post.CopyOf;
   var p = RowBase(post.P.Nick, post.P.Id + " · " + post.P.No + "번 · " + (orig != null ? orig.P.Nick + " 글과 " + post.CopyPct + "% 같음" : "복붙의심"), out acts, out subL);
   var wrap = new Panel { BackColor = Th.Card, Height = p.Height };
   p.Dock = DockStyle.Top; wrap.Controls.Add(p);
   var cmp = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 0, Visible = false, BackColor = Th.Card, Padding = new Padding(14, 0, 14, 12) };
   cmp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cmp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
   Func<string, string, Control> pane = (t, body) => { var tb = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Th.Bg, Text = t + "\r\n" + body, Dock = DockStyle.Fill, Margin = new Padding(3), Font = Th.F(9), ScrollBars = ScrollBars.Vertical }; return tb; };
   if (orig != null) { cmp.Controls.Add(pane("먼저 · " + orig.P.Nick + " · " + orig.Community + (orig.Written.HasValue ? " · " + orig.Written.Value.ToString("MM-dd HH:mm") : ""), orig.Excerpt), 0, 0); }
   cmp.Controls.Add(pane("나중 · " + post.P.Nick + " · " + post.Community + (post.Written.HasValue ? " · " + post.Written.Value.ToString("MM-dd HH:mm") : ""), post.Excerpt), 1, 0);
   wrap.Controls.Add(cmp); cmp.BringToFront(); p.SendToBack();
   var toggle = new Btn("두 글 비교 ▾", Btn.K.Link, null);
   toggle.Click += (s, e) => { cmp.Visible = !cmp.Visible; cmp.Height = cmp.Visible ? 130 : 0; wrap.Height = p.Height + cmp.Height; toggle.Text = cmp.Visible ? "두 글 비교 ▴" : "두 글 비교 ▾"; Relayout(); };
   if (post.Decided) {
    Decision d; main.Decisions.TryGetValue("copy:" + post.Key, out d);
    acts.Controls.Add(Done(d != null && d.Accept ? "✓ 인정" : "✓ 제외 (복붙의심)")); acts.Controls.Add(Undo("copy:" + post.Key));
   } else {
    toggle.Margin = new Padding(0, 6, 10, 0); acts.Controls.Add(toggle);
    acts.Controls.Add(new Btn("인정", Btn.K.Gray, () => Decide("copy:" + post.Key, new Decision { Accept = true })) { Margin = new Padding(0, 0, 6, 0) });
    acts.Controls.Add(new Btn("제외", Btn.K.Dark, () => Decide("copy:" + post.Key, new Decision { Accept = false })));
   }
   return wrap;
  }
  Control CommentRow(Participant pa) {
   FlowLayoutPanel acts; Label subL;
   var p = RowBase(pa.Nick, pa.Id + " · " + pa.No + "번 · " + (pa.CommentHint != "" ? "비슷한 댓글 닉네임: " + pa.CommentHint : "비슷한 닉네임 없음"), out acts, out subL);
   Decision d;
   if (main.Decisions.TryGetValue("cmt:" + pa.No, out d)) { acts.Controls.Add(Done(d.Accept ? "✓ 인정" : "✓ 제외 (댓글 미작성)")); acts.Controls.Add(Undo("cmt:" + pa.No)); }
   else {
    var open = new Btn("댓글 글 열기 ↗", Btn.K.Link, () => Th.Open(main.Set.CommentUrl)); open.Margin = new Padding(0, 6, 10, 0); acts.Controls.Add(open);
    acts.Controls.Add(new Btn("인정", Btn.K.Gray, () => Decide("cmt:" + pa.No, new Decision { Accept = true })) { Margin = new Padding(0, 0, 6, 0) });
    acts.Controls.Add(new Btn("제외", Btn.K.Dark, () => Decide("cmt:" + pa.No, new Decision { Accept = false })));
   }
   return p;
  }
  Btn Undo(string key) { var b = new Btn("되돌리기", Btn.K.Plain, () => { main.Decisions.Remove(key); main.Reanalyze(); Rebuild(); }); b.Margin = new Padding(8, 0, 0, 0); return b; }
  void Decide(string key, Decision d) { main.Decisions[key] = d; main.Reanalyze(); Rebuild(); }
  void Rebuild() { int y = scroll.VerticalScroll.Value; Build(); try { scroll.AutoScrollPosition = new Point(0, y); } catch { } }
  void Relayout() {
   foreach (Control c in list.Controls) { var g = c as Box; if (g == null) continue; int y = 4; foreach (Control r in g.Controls) { r.Top = y; y += r.Height; } g.Height = y + 4; }
   int h = Padding.Vertical + 58 + list.PreferredSize.Height + 8; Height = Math.Min(Math.Max(220, h), main.ClientSize.Height - 40);
   Region = Region.FromHrgn(CreateRoundRectRgn(0, -20, Width + 1, Height + 1, 26, 26));
  }
  protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); }
 }

 // ───────────────────────── 메인 창
 public class MainForm : Form {
  public Settings Set; public History Hist; public List<Participant> Participants = new List<Participant>(); public Dictionary<string, Decision> Decisions = new Dictionary<string, Decision>();
  public bool CommentsChecked { get { return comments != null; } }
  List<string> comments; string commentError = "", rawPath = "", eventName = "", baseOverride = null; DateTime checkedAt; bool busy; string filter = null;
  readonly string dataDir;
  // 화면
  NavItem navMain, navHist, navSet; Panel content = new Panel(), pageEmpty = new Panel(), pageMain = new Panel(), pageHist = new Panel(), pageSet = new Panel();
  Label title = new Label(), subtitle = new Label(); Btn recheck; Row rowFile, rowPrize, rowComment; Btn fileEdit; TextBox fileBox;
  Panel progRow = new Panel(); Line prog = new Line(); Label progText = new Label();
  Tile tSp, tFc, tEx, tRv; ListGrid grid = new ListGrid(); Btn reviewBtn, exportBtn; Label reviewNote = new Label();
  Box toast = new Box(); Label toastText = new Label(); string lastSaved = ""; System.Windows.Forms.Timer toastTimer = new System.Windows.Forms.Timer();
  // 이력
  Label histSub = new Label(); TextBox search = new TextBox(); Seg histSeg = new Seg("전체", "선착순", "특별"); FlowLayoutPanel months = new FlowLayoutPanel(); Panel monthsScroll = new Panel(); HashSet<string> openMonths = new HashSet<string>();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

  public MainForm(string startFile) {
   dataDir = Path.GetDirectoryName(Application.ExecutablePath);
   Set = Settings.Load(Path.Combine(dataDir, "설정.ini")); Hist = History.Load(Path.Combine(dataDir, "당첨이력.csv"));
   Text = "추첨 도우미"; try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
   Size = new Size(1280, 820); MinimumSize = new Size(1080, 700); StartPosition = FormStartPosition.CenterScreen; BackColor = Th.Bg; Font = Th.F(9.5f); AutoScaleMode = AutoScaleMode.Dpi; AllowDrop = true; KeyPreview = true;
   var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Th.Bg };
   shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);
   // 왼쪽 메뉴
   var side = new Panel { Dock = DockStyle.Fill, BackColor = Th.Side, Margin = Padding.Empty, Padding = new Padding(10, 14, 10, 10) };
   var mark = new PictureBox { Size = new Size(28, 28), Location = new Point(18, 20), Image = Logo.Bitmap(28), BackColor = Th.Side }; side.Controls.Add(mark);
   side.Controls.Add(new Label { Text = "추첨 도우미", Font = Th.F(11, true), AutoSize = true, Location = new Point(54, 23), BackColor = Th.Side });
   navMain = new NavItem("이벤트 정리", 0) { Location = new Point(10, 72), Width = 180 }; navHist = new NavItem("당첨 이력", 1) { Location = new Point(10, 110), Width = 180 }; navSet = new NavItem("설정", 2) { Location = new Point(10, 148), Width = 180 };
   navMain.Click += (s, e) => Show(Participants.Count > 0 ? pageMain : pageEmpty); navHist.Click += (s, e) => Show(pageHist); navSet.Click += (s, e) => Show(pageSet);
   side.Controls.Add(navMain); side.Controls.Add(navHist); side.Controls.Add(navSet);
   side.Controls.Add(new Label { Text = "버전 1.0", ForeColor = Th.Sub, Font = Th.F(8.5f), AutoSize = true, Dock = DockStyle.Bottom, BackColor = Th.Side, Padding = new Padding(8, 0, 0, 4) });
   shell.Controls.Add(side, 0, 0);
   content.Dock = DockStyle.Fill; content.Margin = Padding.Empty; content.BackColor = Th.Bg; shell.Controls.Add(content, 1, 0);
   foreach (var p in new[] { pageEmpty, pageMain, pageHist, pageSet }) { p.Dock = DockStyle.Fill; p.BackColor = Th.Bg; p.Padding = new Padding(36, 26, 36, 18); p.Visible = false; content.Controls.Add(p); }
   BuildEmpty(); BuildMain(); BuildHistory(); BuildSettings();
   DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
   DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) LoadRaw(f[0]); };
   toastTimer.Interval = 7000; toastTimer.Tick += (s, e) => { toast.Visible = false; toastTimer.Stop(); };
   Show(pageEmpty);
   if (!String.IsNullOrEmpty(startFile)) Shown += (s, e) => LoadRaw(startFile);
  }
  void Show(Panel page) {
   foreach (var p in new[] { pageEmpty, pageMain, pageHist, pageSet }) p.Visible = p == page;
   navMain.Selected = page == pageEmpty || page == pageMain; navHist.Selected = page == pageHist; navSet.Selected = page == pageSet;
   navMain.Invalidate(); navHist.Invalidate(); navSet.Invalidate();
   if (page == pageHist) RefreshHistory();
  }
  static Label Title(string t) { return new Label { Text = t, Font = Th.F(20, true), AutoSize = true, Location = new Point(0, 0), BackColor = Th.Bg, UseMnemonic = false }; }
  static Label Sub(string t) { return new Label { Text = t, ForeColor = Th.Sub, AutoSize = true, Location = new Point(2, 44), BackColor = Th.Bg, UseMnemonic = false }; }

  // ── 처음 화면
  void BuildEmpty() {
   var head = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Th.Bg };
   head.Controls.Add(Title("이벤트 정리")); head.Controls.Add(Sub("네이버폼 raw 파일 하나만 넣으면 시작해요."));
   var zone = new DropZone(); var pick = new Btn("파일 선택", Btn.K.Primary, () => PickRaw()).Big(); zone.Controls.Add(pick);
   zone.Resize += (s, e) => pick.Location = new Point((zone.Width - pick.Width) / 2, 206);
   zone.Click += (s, e) => PickRaw();
   var center = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg }; center.Controls.Add(zone);
   center.Resize += (s, e) => zone.Location = new Point(Math.Max(0, (center.Width - zone.Width) / 2), Math.Max(0, (center.Height - zone.Height) / 2 - 30));
   pageEmpty.Controls.Add(center); pageEmpty.Controls.Add(head);
  }
  void PickRaw() { using (var d = new OpenFileDialog { Filter = "엑셀 파일 (*.xlsx)|*.xlsx", Title = "네이버폼 raw 파일 선택" }) if (d.ShowDialog(this) == DialogResult.OK) LoadRaw(d.FileName); }

  // ── 이벤트 정리
  void BuildMain() {
   var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Th.Bg, Margin = Padding.Empty };
   t.RowStyles.Add(new RowStyle(SizeType.Absolute, 78)); t.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); t.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
   t.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); t.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); t.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
   pageMain.Controls.Add(t);
   // 제목
   var head = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg, Margin = Padding.Empty };
   title = Title(""); title.AutoSize = false; title.AutoEllipsis = true; title.Height = 40; subtitle = Sub("");
   recheck = new Btn("다시 확인", Btn.K.Gray, () => RunCheck()); recheck.Anchor = AnchorStyles.Top | AnchorStyles.Right;
   var other = new Btn("다른 파일", Btn.K.Plain, () => PickRaw()); other.Anchor = AnchorStyles.Top | AnchorStyles.Right;
   head.Controls.Add(title); head.Controls.Add(subtitle); head.Controls.Add(recheck); head.Controls.Add(other);
   head.Resize += (s, e) => { recheck.Location = new Point(head.Width - recheck.Width, 6); other.Location = new Point(recheck.Left - other.Width - 10, 6); title.Width = Math.Max(100, other.Left - 16); };
   t.Controls.Add(head, 0, 0);
   // 정보 세 줄
   var info = new Group { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };
   fileEdit = new Btn("수정", Btn.K.Link, () => EditFileName());
   fileBox = new TextBox { Width = 520, BorderStyle = BorderStyle.FixedSingle, BackColor = Th.Fill, Font = Th.F(9.5f), Visible = false };
   fileBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditFileName(); } if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; fileBox.Visible = false; rowFile.Val.Visible = true; fileEdit.Text = "수정"; fileEdit.FitWidth(); } };
   rowFile = info.AddRow(new Row("파일 이름", 100)); rowFile.Controls.Add(fileBox); fileBox.BringToFront(); rowFile.Add(fileEdit);
   rowFile.Resize += (s, e) => { fileBox.Location = new Point(116, 12); fileBox.Width = Math.Max(200, rowFile.Width - 116 - fileEdit.Width - 40); };
   rowPrize = info.AddRow(new Row("경품", 100)); rowPrize.Add(new Btn("수정", Btn.K.Link, () => EditPrize()));
   rowComment = info.AddRow(new Row("댓글 확인", 100)); rowComment.Add(new Btn("수정", Btn.K.Link, () => EditComment()));
   info.Height = 146; t.Controls.Add(info, 0, 1);
   // 진행
   progRow.Dock = DockStyle.Fill; progRow.BackColor = Th.Bg; prog.Location = new Point(0, 12); prog.Width = 300; progText.AutoSize = true; progText.ForeColor = Th.Sub; progText.Location = new Point(312, 6); progText.BackColor = Th.Bg;
   progRow.Controls.Add(prog); progRow.Controls.Add(progText); t.Controls.Add(progRow, 0, 2);
   // 숫자 카드
   var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Th.Bg, Margin = new Padding(0, 14, 0, 4), Padding = Padding.Empty };
   for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
   tSp = new Tile("특별 후보", "특별"); tFc = new Tile("선착순", "선착순"); tEx = new Tile("제외", "제외"); tRv = new Tile("직접 검토", "검토");
   foreach (var tile in new[] { tSp, tFc, tEx, tRv }) { tile.Dock = DockStyle.Fill; tile.Margin = new Padding(0, 0, tile == tRv ? 0 : 12, 0); var tt = tile; tile.Click += (s, e) => { filter = filter == tt.Sec ? null : tt.Sec; foreach (var x in new[] { tSp, tFc, tEx, tRv }) { x.Selected = x.Sec == filter; x.Invalidate(); } FillGrid(); }; tiles.Controls.Add(tile); }
   t.Controls.Add(tiles, 0, 3);
   // 표
   var lc = new Box { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0), Padding = new Padding(8, 6, 8, 6) };
   grid.Dock = DockStyle.Fill;
   grid.Col("no", "No", 52, false, DataGridViewContentAlignment.MiddleCenter);
   grid.Col("nick", "닉네임", 150, true, DataGridViewContentAlignment.MiddleLeft);
   grid.Col("comm", "커뮤니티", 120, true, DataGridViewContentAlignment.MiddleLeft);
   grid.Col("views", "조회수", 80, false, DataGridViewContentAlignment.MiddleRight);
   grid.Col("q", "정성도", 70, false, DataGridViewContentAlignment.MiddleCenter);
   grid.Col("st", "상태", 230, true, DataGridViewContentAlignment.MiddleLeft);
   grid.Col("open", "", 44, false, DataGridViewContentAlignment.MiddleCenter);
   grid.CellPainting += GridPaint; grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenRow(e.RowIndex); };
   grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 6) OpenRow(e.RowIndex); };
   grid.CellMouseEnter += (s, e) => { grid.Cursor = e.ColumnIndex == 6 && e.RowIndex >= 0 ? Cursors.Hand : Cursors.Default; };
   lc.Controls.Add(grid);
   toast.FillColor = Th.Ink; toast.Radius = 12; toast.Size = new Size(560, 48); toast.Visible = false; toast.BackColor = Th.Ink;
   toastText.ForeColor = Color.White; toastText.BackColor = Th.Ink; toastText.AutoSize = false; toastText.AutoEllipsis = true; toastText.Location = new Point(16, 14); toastText.Size = new Size(360, 20); toast.Controls.Add(toastText);
   var tOpen = new Btn("열기", Btn.K.Gray, () => Th.Open(lastSaved)); var tFolder = new Btn("폴더", Btn.K.Gray, () => { try { Process.Start("explorer.exe", "/select,\"" + lastSaved + "\""); } catch { } });
   tOpen.Height = 30; tFolder.Height = 30; tOpen.Location = new Point(390, 9); tFolder.Location = new Point(390 + tOpen.Width + 8, 9); toast.Controls.Add(tOpen); toast.Controls.Add(tFolder);
   foreach (Control c in new Control[] { tOpen, tFolder }) c.BackColor = Th.Ink;
   pageMain.Controls.Add(toast); toast.BringToFront();
   pageMain.Resize += (s, e) => toast.Location = new Point((pageMain.Width - toast.Width) / 2, pageMain.Height - toast.Height - 84);
   t.Controls.Add(lc, 0, 4);
   // 아래
   var foot = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg, Margin = Padding.Empty };
   reviewBtn = new Btn("직접 검토", Btn.K.Gray, () => OpenReview()).Big(); reviewBtn.Location = new Point(0, 14);
   reviewNote.AutoSize = true; reviewNote.ForeColor = Th.Sub; reviewNote.BackColor = Th.Bg; reviewNote.Location = new Point(reviewBtn.Right + 14, 26);
   exportBtn = new Btn("엑셀 내보내기", Btn.K.Primary, () => Export()).Big(); exportBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
   foot.Controls.Add(reviewBtn); foot.Controls.Add(reviewNote); foot.Controls.Add(exportBtn);
   foot.Resize += (s, e) => exportBtn.Location = new Point(foot.Width - exportBtn.Width, 14);
   t.Controls.Add(foot, 0, 5);
  }
  void GridPaint(object sender, DataGridViewCellPaintingEventArgs e) {
   if (e.RowIndex < 0) return;
   var pa = grid.Rows[e.RowIndex].Tag as Participant; if (pa == null) return;
   string col = grid.Columns[e.ColumnIndex].Name;
   if (col != "nick" && col != "st" && col != "q" && col != "open") return;
   e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
   var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var b = e.CellBounds;
   if (col == "nick") {
    TextRenderer.DrawText(g, pa.Nick, Th.F(9.5f, true), new Rectangle(b.X + 6, b.Y + 6, b.Width - 10, 20), Th.Ink, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    TextRenderer.DrawText(g, pa.Id, Th.F(8.5f), new Rectangle(b.X + 6, b.Y + 25, b.Width - 10, 18), Th.Sub, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
   } else if (col == "q") {
    string q = Convert.ToString(e.Value); Color c = q == "상" ? Th.HighQ : q == "하" ? Color.FromArgb(160, 160, 168) : q == "-" ? Th.Sub : Th.Ink;
    TextRenderer.DrawText(g, q, Th.F(9.5f, q != "-"), b, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
   } else if (col == "open") {
    TextRenderer.DrawText(g, "↗", Th.F(10), b, Color.FromArgb(170, 170, 178), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
   } else {
    using (var br = new SolidBrush(Th.SecColor(pa.Section))) g.FillEllipse(br, b.X + 8, b.Y + b.Height / 2 - 4, 8, 8);
    string name = Th.SecName(pa.Section); var nsz = TextRenderer.MeasureText(g, name, Th.F(9.5f, true), Size.Empty, TextFormatFlags.NoPadding);
    TextRenderer.DrawText(g, name, Th.F(9.5f, true), new Point(b.X + 22, b.Y + (b.Height - nsz.Height) / 2), Th.Ink, TextFormatFlags.NoPadding);
    string why = Why(pa);
    if (why != "") TextRenderer.DrawText(g, "· " + why, Th.F(9), new Rectangle(b.X + 28 + nsz.Width, b.Y, b.Width - nsz.Width - 34, b.Height), Th.Sub, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
   }
   e.Handled = true;
  }
  static string Why(Participant pa) {
   var parts = new List<string>();
   if (pa.Section == "제외") parts.Add(pa.Reason);
   else if (pa.Section == "검토") {
    foreach (var p in pa.Posts.Where(x => x.State == PState.Review)) parts.Add(p.Kind == RKind.Copy ? "복붙의심" : (p.Community != "" ? p.Community + " 직접 확인" : "직접 확인"));
    if (pa.CommentPending) parts.Add("댓글 미확인");
   } else {
    int valid = pa.Posts.Count(p => p.State == PState.Ok); if (valid > 1) parts.Add("글 " + valid + "개 합산");
    int ex = pa.Posts.Count(p => p.State == PState.Excluded); if (ex > 0) parts.Add("글 " + ex + "건 뺌 (" + String.Join(", ", pa.Posts.Where(p => p.State == PState.Excluded).Select(p => p.Reason).Distinct()) + ")");
    if (pa.BlackInfo != "" && pa.Section == "특별") parts.Add("낙첨 시 선착순 불가");
   }
   return String.Join(" · ", parts.Distinct().Where(s => s != ""));
  }
  void OpenRow(int i) { var pa = grid.Rows[i].Tag as Participant; if (pa == null) return; var p = pa.Posts.FirstOrDefault(x => x.State == PState.Ok) ?? pa.Posts.FirstOrDefault(); if (p != null) Th.Open(p.FinalUrl != "" ? p.FinalUrl : p.Url); }

  // ── 불러오기와 확인
  public void LoadRaw(string path) {
   if (busy) return;
   string err; var list = RawReader.Read(path, out err);
   if (err != "") { MessageBox.Show(this, err, "추첨 도우미", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
   if (list.Count == 0) { MessageBox.Show(this, "응답이 없어요.", "추첨 도우미"); return; }
   rawPath = path; eventName = RawReader.EventName(path); baseOverride = null; Participants = list; Decisions.Clear(); comments = null; commentError = ""; filter = null;
   foreach (var x in new[] { tSp, tFc, tEx, tRv }) { x.Selected = false; x.Count = 0; x.Invalidate(); }
   title.Text = eventName; Show(pageMain); RefreshInfo(); grid.Rows.Clear();
   RunCheck();
  }
  public async void RunCheck() {
   if (busy || Participants.Count == 0) return;
   busy = true; SetBusy(true);
   var posts = Participants.SelectMany(p => p.Posts).ToList(); int total = posts.Count + (Set.CommentUrl != "" ? 1 : 0), done = 0;
   Action<string> report = (what) => { int d = Interlocked.Increment(ref done); BeginInvoke((Action)(() => { prog.Value = total == 0 ? 1 : (float)d / total; prog.Invalidate(); progText.Text = "게시글 확인 중 · " + d + " / " + total + (what != "" ? " · " + what : ""); })); };
   var set = Set; string cUrl = Set.CommentUrl;
   List<string> got = null; string cerr = "";
   await Task.Run(() => {
    Parallel.ForEach(posts, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p => { try { Fetcher.Fetch(p, set); } catch (Exception ex) { p.BaseState = PState.Review; p.BaseKind = RKind.Manual; p.ManualWhy = "자동 확인 실패 (" + ex.GetType().Name + ")"; } report(p.P.Nick); Thread.Sleep(120); });
    if (cUrl != "") { try { got = Cafe.CommentNicks(cUrl, out cerr); } catch (Exception ex) { cerr = ex.Message; } report("댓글"); }
   });
   comments = cUrl != "" && cerr == "" ? got : null; commentError = cerr;
   checkedAt = DateTime.Now; busy = false; SetBusy(false); Reanalyze();
  }
  public void LoadRawSync(string path) { string err; var list = RawReader.Read(path, out err); if (err != "") return; rawPath = path; eventName = RawReader.EventName(path); Participants = list; title.Text = eventName; Show(pageMain); RefreshInfo(); RunCheckSync(); }
  public void RunCheckSync() {
   foreach (var p in Participants.SelectMany(x => x.Posts)) { try { Fetcher.Fetch(p, Set); } catch { } }
   if (Set.CommentUrl != "") { string cerr; comments = Cafe.CommentNicks(Set.CommentUrl, out cerr); commentError = cerr; if (cerr != "") comments = null; }
   checkedAt = DateTime.Now; Reanalyze();
  }
  void SetBusy(bool on) {
   var t = (TableLayoutPanel)pageMain.Controls.OfType<TableLayoutPanel>().First(); t.RowStyles[2].Height = on ? 30 : 0;
   prog.Value = 0; progText.Text = on ? "게시글 확인 중…" : ""; recheck.Enabled = !on; exportBtn.Enabled = !on; reviewBtn.Enabled = !on;
   subtitle.Text = on ? "응답 " + Participants.Count + "명 · 게시글 " + Participants.Sum(p => p.Posts.Count) + "개 · 확인 중…" : subtitle.Text;
  }
  public void Reanalyze() {
   if (Participants.Count == 0) return;
   var sum = Engine.Analyze(Participants, Set, comments, Hist, Decisions, DateTime.Today);
   tSp.Count = sum.Special; tFc.Count = sum.First; tEx.Count = sum.Excluded; int rv = Engine.ReviewCount(Participants); tRv.Count = rv;
   foreach (var x in new[] { tSp, tFc, tEx, tRv }) x.Invalidate();
   subtitle.Text = "응답 " + sum.Responses + "명 · 게시글 " + sum.Posts + "개 · " + checkedAt.ToString("MM-dd HH:mm") + " 확인";
   reviewBtn.Badge = rv > 0 ? rv.ToString() : ""; reviewBtn.FitWidth(); reviewBtn.Invalidate(); reviewNote.Location = new Point(reviewBtn.Right + 14, 26);
   reviewNote.Text = rv > 0 ? "직접 검토를 끝내야 엑셀에 정확히 들어가요" : "검토할 게 없어요";
   RefreshInfo(); FillGrid();
  }
  void FillGrid() {
   int keep = grid.FirstDisplayedScrollingRowIndex;
   grid.SuspendLayout(); grid.Rows.Clear();
   foreach (var pa in Participants.OrderBy(p => p.No)) {
    if (filter != null && pa.Section != filter) continue;
    var valid = pa.Posts.Where(p => p.State == PState.Ok).ToList(); var shown = valid.Count > 0 ? valid : pa.Posts;
    string comm = shown.Count == 0 ? "-" : shown[0].Community + (shown.Count > 1 ? " 외 " + (shown.Count - 1) + "곳" : "");
    string views = valid.Count > 0 ? pa.Sum.ToString("#,##0") : (pa.Posts.Any(p => p.EffViews >= 0) ? pa.Posts.Max(p => p.EffViews).ToString("#,##0") : "-");
    string q = "-"; var qs = shown.Select(p => p.EffQuality).Where(x => x != "").ToList(); if (qs.Contains("상")) q = "상"; else if (qs.Contains("중")) q = "중"; else if (qs.Contains("하")) q = "하";
    int i = grid.Rows.Add(pa.No, pa.Nick, comm, views, q, "", "");
    grid.Rows[i].Tag = pa;
   }
   grid.ResumeLayout();
   if (keep >= 0 && keep < grid.Rows.Count) try { grid.FirstDisplayedScrollingRowIndex = keep; } catch { }
   grid.ClearSelection();
  }
  string BaseName() { return baseOverride ?? Exporter.BaseName(Set, eventName); }
  void RefreshInfo() {
   rowFile.Val.Text = BaseName() + "_" + DateTime.Today.ToString("yyMMdd") + ".xlsx";
   rowPrize.Val.Text = "특별 " + Set.SpecialCount + "명" + (Set.SpecialPrize != "" ? " " + Set.SpecialPrize : "") + "   ·   선착순 " + (Set.FirstCount > 0 ? Set.FirstCount + "명" : "인원 미정") + (Set.FirstPrize != "" ? " " + Set.FirstPrize : "");
   if (Set.CommentUrl == "") { rowComment.Val.Text = "확인 안 함 · 공식카페 이벤트 글 주소를 넣으면 댓글 단 사람인지 확인해요"; rowComment.Val.ForeColor = Th.Sub; }
   else { rowComment.Val.ForeColor = commentError != "" ? Th.Red : Th.Ink; rowComment.Val.Text = Set.CommentUrl.Replace("https://", "") + (commentError != "" ? " · " + commentError : comments != null ? " · 댓글 " + comments.Count + "개" : ""); }
  }
  void EditFileName() {
   if (!fileBox.Visible) { fileBox.Text = BaseName(); fileBox.Visible = true; rowFile.Val.Visible = false; fileEdit.Text = "완료"; fileEdit.FitWidth(); fileBox.Focus(); fileBox.SelectAll(); return; }
   string v = fileBox.Text.Trim(); if (v != "") { baseOverride = v; string pre = Exporter.PrefixFrom(v, eventName); Set.FilePrefix = pre; Set.Save(); if (Exporter.BaseName(Set, eventName) == v) baseOverride = null; }
   fileBox.Visible = false; rowFile.Val.Visible = true; fileEdit.Text = "수정"; fileEdit.FitWidth(); RefreshInfo();
  }
  void EditPrize() {
   var a = new Ask("경품", "엑셀 제목과 구역 이름에 들어가요. 다음 이벤트에도 그대로 남아요.");
   var sc = a.Text1("특별 인원", Set.SpecialCount.ToString()); var sp = a.Text1("특별 경품", Set.SpecialPrize);
   var fc = a.Text1("선착순 인원", Set.FirstCount > 0 ? Set.FirstCount.ToString() : ""); var fp = a.Text1("선착순 경품", Set.FirstPrize);
   if (!a.Run(this)) return;
   int n; if (Int32.TryParse(sc.Text.Trim(), out n)) Set.SpecialCount = n; Set.SpecialPrize = sp.Text.Trim();
   Set.FirstCount = Int32.TryParse(fc.Text.Trim(), out n) ? n : 0; Set.FirstPrize = fp.Text.Trim(); Set.Save(); RefreshInfo();
  }
  async void EditComment() {
   var a = new Ask("댓글 확인", "공식카페 이벤트 글 주소예요. 비우면 댓글 확인을 하지 않아요."); var u = a.Text1("글 주소", Set.CommentUrl);
   if (!a.Run(this)) return;
   Set.CommentUrl = u.Text.Trim(); Set.Save(); RemoveDecisions("cmt:");
   if (Participants.Count == 0) return;
   if (Set.CommentUrl == "") { comments = null; commentError = ""; Reanalyze(); return; }
   rowComment.Val.Text = "댓글 읽는 중…"; string url = Set.CommentUrl, cerr = ""; List<string> got = null;
   await Task.Run(() => { try { got = Cafe.CommentNicks(url, out cerr); } catch (Exception ex) { cerr = ex.Message; } });
   comments = cerr == "" ? got : null; commentError = cerr; Reanalyze();
  }
  void RemoveDecisions(string prefix) { foreach (var k in Decisions.Keys.Where(k => k.StartsWith(prefix)).ToList()) Decisions.Remove(k); }
  void OpenReview() {
   if (busy || Participants.Count == 0) return;
   using (var dim = new DimForm(this)) using (var sheet = new ReviewSheet(this)) {
    dim.Show(this);
    var area = RectangleToScreen(ClientRectangle);
    sheet.Location = new Point(area.X + 200 + (area.Width - 200 - sheet.Width) / 2, area.Y);
    sheet.ShowDialog(dim);
   }
   Activate();
  }
  void Export() {
   if (busy || Participants.Count == 0) return;
   int rv = Engine.ReviewCount(Participants);
   if (rv > 0 && MessageBox.Show(this, "직접 검토가 " + rv + "건 남았어요.\n남은 건 지금 값으로 넣고 특이사항에 '직접 검토 남음'을 적어요. 저장할까요?", "엑셀 내보내기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
   string folder = Set.SaveFolder != "" && Directory.Exists(Set.SaveFolder) ? Set.SaveFolder : Path.GetDirectoryName(rawPath);
   string path = Path.Combine(folder, Exporter.FileName(BaseName(), DateTime.Today));
   try { Exporter.Save(path, eventName, Participants, Set); }
   catch (IOException) { MessageBox.Show(this, "같은 이름의 파일이 엑셀에 열려 있어요. 닫고 다시 눌러 주세요.\n" + path, "엑셀 내보내기", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
   catch (Exception ex) { MessageBox.Show(this, "저장하지 못했어요.\n" + ex.Message, "엑셀 내보내기", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
   ShowToast("저장했어요 · " + Path.GetFileName(path), path);
  }
  void ShowToast(string text, string path) { lastSaved = path; toastText.Text = text; toast.Location = new Point((pageMain.Width - toast.Width) / 2, pageMain.Height - toast.Height - 84); toast.Visible = true; toast.BringToFront(); toastTimer.Stop(); toastTimer.Start(); }
  public string ExportTo(string path) { Exporter.Save(path, eventName, Participants, Set); return path; }

  // ── 당첨 이력
  void BuildHistory() {
   var head = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Th.Bg };
   head.Controls.Add(Title("당첨 이력")); histSub = Sub(""); head.Controls.Add(histSub);
   var tool = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Th.Bg };
   search.Size = new Size(240, 30); search.BorderStyle = BorderStyle.FixedSingle; search.BackColor = Color.White; search.Font = Th.F(10); search.Location = new Point(0, 4);
   search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, "닉네임·아이디 검색");
   search.TextChanged += (s, e) => RefreshHistory();
   histSeg.Location = new Point(252, 3); histSeg.Changed += (s, e) => RefreshHistory();
   var imp = new Btn("당첨자 리스트 불러오기", Btn.K.Gray, () => ImportList()); var exp = new Btn("월별 엑셀 저장", Btn.K.Primary, () => ExportHistory());
   imp.Anchor = exp.Anchor = AnchorStyles.Top | AnchorStyles.Right;
   tool.Controls.Add(search); tool.Controls.Add(histSeg); tool.Controls.Add(imp); tool.Controls.Add(exp);
   tool.Resize += (s, e) => { exp.Location = new Point(tool.Width - exp.Width, 2); imp.Location = new Point(exp.Left - imp.Width - 8, 2); };
   monthsScroll.Dock = DockStyle.Fill; monthsScroll.AutoScroll = true; monthsScroll.BackColor = Th.Bg;
   months.FlowDirection = FlowDirection.TopDown; months.WrapContents = false; months.AutoSize = true; months.AutoSizeMode = AutoSizeMode.GrowAndShrink; months.BackColor = Th.Bg; months.Location = new Point(0, 0);
   monthsScroll.Controls.Add(months); monthsScroll.Resize += (s, e) => { foreach (Control c in months.Controls) c.Width = MonthW; };
   pageHist.Controls.Add(monthsScroll); pageHist.Controls.Add(tool); pageHist.Controls.Add(head);
  }
  int MonthW { get { return Math.Max(400, monthsScroll.ClientSize.Width - 2); } }
  public void RefreshHistory() {
   var today = DateTime.Today;
   histSub.Text = "선착순 당첨자는 발표일부터 " + Set.BlackMonths + "개월 동안 선착순에서 빠져요. 지금 " + Hist.BlackCount(Set.BlackMonths, today) + "명";
   months.SuspendLayout(); months.Controls.Clear();
   string q = History.Key(search.Text); string kind = histSeg.Index == 1 ? "선착순" : histSeg.Index == 2 ? "특별" : null;
   var groups = Hist.Items.GroupBy(e => e.Ym).OrderByDescending(g => g.Key).ToList();
   if (groups.Count == 0) months.Controls.Add(new Label { Text = "아직 기록이 없어요. 추첨 후 노란색을 칠한 최종 당첨자 리스트를 '당첨자 리스트 불러오기'로 넣으세요.", AutoSize = true, ForeColor = Th.Sub, Margin = new Padding(2, 12, 0, 0), BackColor = Th.Bg });
   foreach (var g in groups) {
    var rows = g.Where(e => (kind == null || e.Kind == kind) && (q == "" || History.Key(e.Nick).Contains(q) || History.Key(e.Id).Contains(q))).ToList();
    if ((q != "" || kind != null) && rows.Count == 0) continue;
    months.Controls.Add(MonthCard(g.Key, g.ToList(), rows, q != "" || kind != null));
   }
   months.ResumeLayout();
  }
  Control MonthCard(string ym, List<HEntry> all, List<HEntry> rows, bool filtered) {
   var today = DateTime.Today; bool open = openMonths.Contains(ym) || filtered;
   var card = new Box { Width = MonthW, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(0) };
   var head = new Panel { Height = 50, BackColor = Th.Card, Cursor = Cursors.Hand, Location = new Point(6, 4) };
   string label = History.Label(new HEntry { Ym = ym });
   int fc = all.Count(x => x.Kind == "선착순"), sp = all.Count(x => x.Kind == "특별");
   var rounds = String.Join(" · ", all.Select(x => x.Round).Where(x => x != "").Distinct().OrderBy(x => x));
   var blackUntil = all.Where(x => x.Kind == "선착순").Select(x => History.Until(x, Set.BlackMonths)).Where(x => x > today).DefaultIfEmpty(DateTime.MinValue).Max();
   head.Controls.Add(new Label { Text = label, Font = Th.F(11, true), AutoSize = true, Location = new Point(12, 14), BackColor = Th.Card });
   head.Controls.Add(new Label { Text = (rounds != "" ? rounds + "    " : "") + "선착순 " + fc + " · 특별 " + sp, ForeColor = Th.Sub, AutoSize = true, Location = new Point(144, 17), BackColor = Th.Card });
   var st = new Label { Text = blackUntil > DateTime.MinValue ? blackUntil.ToString("M/d") + "까지 선착순 제외" : "모두 풀림 · 기록만 남음", ForeColor = blackUntil > DateTime.MinValue ? Th.Red : Th.Sub, Font = Th.F(9.5f, blackUntil > DateTime.MinValue), AutoSize = true, BackColor = Th.Card, Anchor = AnchorStyles.Top | AnchorStyles.Right };
   var chev = new Label { Text = open ? "⌄" : "›", ForeColor = Color.FromArgb(180, 180, 188), Font = Th.F(13), AutoSize = true, BackColor = Th.Card, Anchor = AnchorStyles.Top | AnchorStyles.Right };
   head.Controls.Add(st); head.Controls.Add(chev);
   card.Controls.Add(head);
   EventHandler toggle = (s, ev) => { if (openMonths.Contains(ym)) openMonths.Remove(ym); else openMonths.Add(ym); RefreshHistory(); };
   head.Click += toggle; foreach (Control c in head.Controls) c.Click += toggle;
   int y = 56;
   if (open) {
    var gv = new ListGrid { RowTemplate = { Height = 32 }, ScrollBars = ScrollBars.None, Location = new Point(12, y), ColumnHeadersHeight = 30 };
    gv.Col("round", "회차", 70, false, DataGridViewContentAlignment.MiddleLeft); gv.Col("kind", "구분", 70, false, DataGridViewContentAlignment.MiddleLeft);
    gv.Col("nick", "닉네임", 140, true, DataGridViewContentAlignment.MiddleLeft); gv.Col("id", "아이디", 140, true, DataGridViewContentAlignment.MiddleLeft);
    gv.Col("date", "발표일", 100, false, DataGridViewContentAlignment.MiddleLeft); gv.Col("until", "선착순 제외", 120, false, DataGridViewContentAlignment.MiddleLeft); gv.Col("del", "", 60, false, DataGridViewContentAlignment.MiddleCenter);
    foreach (var en in rows.OrderBy(x => x.Round).ThenBy(x => x.Kind == "특별" ? 0 : 1).ThenBy(x => x.Nick)) {
     var u = History.Until(en, Set.BlackMonths);
     int i = gv.Rows.Add(en.Round, en.Kind, en.Nick, en.Id, en.Date.ToString("yyyy-MM-dd"), en.Kind == "선착순" ? (u > today ? u.ToString("MM-dd") + "까지" : "풀림") : "없음", "삭제");
     gv.Rows[i].Tag = en;
     if (en.Kind == "선착순" && u > today) gv.Rows[i].Cells[5].Style.ForeColor = Th.Red; else gv.Rows[i].Cells[5].Style.ForeColor = Th.Sub;
     gv.Rows[i].Cells[6].Style.ForeColor = Th.Sub;
    }
    gv.CellClick += (s, ev) => { if (ev.RowIndex >= 0 && ev.ColumnIndex == 6) { var he = gv.Rows[ev.RowIndex].Tag as HEntry; if (he != null && MessageBox.Show(this, he.Nick + " (" + he.Id + ") 기록을 지울까요?", "당첨 이력", MessageBoxButtons.YesNo) == DialogResult.Yes) { Hist.Items.Remove(he); SaveHist(); } } };
    gv.CellMouseEnter += (s, ev) => gv.Cursor = ev.ColumnIndex == 6 && ev.RowIndex >= 0 ? Cursors.Hand : Cursors.Default;
    gv.Height = gv.ColumnHeadersHeight + gv.Rows.Count * 32 + 2; gv.Width = card.Width - 24; gv.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
    card.Controls.Add(gv); y += gv.Height + 6;
    var add = new Btn("+ 이 달에 추가", Btn.K.Link, () => AddEntry(ym)); add.Location = new Point(16, y); card.Controls.Add(add); y += add.Height + 10;
   }
   card.Height = y;
   head.Width = card.Width - 12; head.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
   chev.Location = new Point(head.Width - 28, 11); st.Location = new Point(head.Width - 54 - TextRenderer.MeasureText(st.Text, st.Font).Width, 17);
   return card;
  }
  void SaveHist() { try { Hist.Save(); } catch (Exception ex) { MessageBox.Show(this, "당첨 이력을 저장하지 못했어요.\n" + ex.Message, "당첨 이력"); } RefreshHistory(); if (Participants.Count > 0) Reanalyze(); }
  void AddEntry(string ym) {
   var a = new Ask("당첨 기록 추가", History.Label(new HEntry { Ym = ym }) + "에 넣어요.");
   var round = a.Text1("회차", Hist.Items.Where(e => e.Ym == ym).Select(e => e.Round).LastOrDefault() ?? "1차");
   var kind = a.Field("구분", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }); kind.Items.AddRange(new object[] { "선착순", "특별" }); kind.SelectedIndex = 0;
   var nick = a.Text1("닉네임", ""); var id = a.Text1("네이버 아이디", "");
   var date = a.Text1("발표일", (Hist.Items.Where(e => e.Ym == ym).Select(e => (DateTime?)e.Date).LastOrDefault() ?? DateTime.Today).ToString("yyyy-MM-dd"));
   if (!a.Run(this)) return;
   DateTime d; if (!DateTime.TryParse(date.Text.Trim(), out d)) { MessageBox.Show(this, "발표일을 2026-10-26처럼 넣어 주세요.", "당첨 이력"); return; }
   if (nick.Text.Trim() == "" && id.Text.Trim() == "") return;
   Hist.Items.Add(new HEntry { Ym = ym, Round = round.Text.Trim(), Kind = (string)kind.SelectedItem, Nick = nick.Text.Trim(), Id = id.Text.Trim(), Date = d.Date });
   openMonths.Add(ym); SaveHist();
  }
  void ImportList() {
   string path;
   using (var d = new OpenFileDialog { Filter = "엑셀 파일 (*.xlsx)|*.xlsx", Title = "노란색을 칠한 최종 당첨자 리스트 선택" }) { if (d.ShowDialog(this) != DialogResult.OK) return; path = d.FileName; }
   ImportResult res;
   try { res = ListImporter.Read(path); } catch (IOException) { MessageBox.Show(this, "파일을 읽지 못했어요. 엑셀에서 닫고 다시 해 주세요.", "당첨 이력"); return; } catch (Exception ex) { MessageBox.Show(this, "파일을 읽지 못했어요.\n" + ex.Message, "당첨 이력"); return; }
   int sp = res.Entries.Count(e => e.Kind == "특별"), fc = res.Entries.Count(e => e.Kind == "선착순");
   if (res.Entries.Count == 0) { MessageBox.Show(this, "당첨자를 찾지 못했어요. 추첨 도우미로 만든 당첨자 리스트가 맞는지 확인해 주세요.", "당첨 이력"); return; }
   var today = DateTime.Today; int year = today.Year; if (res.Month > today.Month + 2) year--;
   var a = new Ask("당첨자 리스트 불러오기", "특별 " + sp + "명(노란색) · 선착순 " + fc + "명을 이력에 넣어요." + (sp == 0 ? "\n노란색 칸이 없어요. 추첨 결과를 칠한 파일이 맞나요?" : ""));
   var ym = a.Text1("연월", res.Month > 0 ? year + "-" + res.Month.ToString("00") : today.ToString("yyyy-MM"));
   var round = a.Text1("회차", res.Round != "" ? res.Round : "1차");
   var date = a.Text1("발표일", today.ToString("yyyy-MM-dd"));
   if (!a.Run(this)) return;
   DateTime d2; if (!DateTime.TryParse(date.Text.Trim(), out d2)) { MessageBox.Show(this, "발표일을 2026-10-26처럼 넣어 주세요.", "당첨 이력"); return; }
   var m = System.Text.RegularExpressions.Regex.Match(ym.Text.Trim(), @"^(\d{4})\D+(\d{1,2})"); if (!m.Success) { MessageBox.Show(this, "연월을 2026-10처럼 넣어 주세요.", "당첨 이력"); return; }
   string key = m.Groups[1].Value + "-" + Int32.Parse(m.Groups[2].Value).ToString("00"); string rd = round.Text.Trim();
   var old = Hist.Items.Where(e => e.Ym == key && e.Round == rd).ToList();
   if (old.Count > 0) { if (MessageBox.Show(this, History.Label(new HEntry { Ym = key, Round = rd }) + " 기록 " + old.Count + "건이 이미 있어요. 새 파일로 바꿀까요?", "당첨 이력", MessageBoxButtons.YesNo) != DialogResult.Yes) return; foreach (var o in old) Hist.Items.Remove(o); }
   foreach (var e in res.Entries) { e.Ym = key; e.Round = rd; e.Date = d2.Date; Hist.Items.Add(e); }
   openMonths.Add(key); SaveHist();
  }
  void ExportHistory() {
   string folder = Set.SaveFolder != "" && Directory.Exists(Set.SaveFolder) ? Set.SaveFolder : (rawPath != "" ? Path.GetDirectoryName(rawPath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
   using (var d = new SaveFileDialog { Filter = "엑셀 파일 (*.xlsx)|*.xlsx", InitialDirectory = folder, FileName = "당첨 이력_월별 정리_" + DateTime.Today.ToString("yyMMdd") + ".xlsx" }) {
    if (d.ShowDialog(this) != DialogResult.OK) return;
    try { HistoryExport.Save(d.FileName, Hist, Set.BlackMonths, DateTime.Today); Th.Open(d.FileName); }
    catch (Exception ex) { MessageBox.Show(this, "저장하지 못했어요.\n" + ex.Message, "당첨 이력"); }
   }
  }

  // ── 설정
  Label linkVal; TextBox linkBox; Btn linkBtn;
  void BuildSettings() {
   var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Th.Bg };
   var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Th.Bg, Location = new Point(0, 0) };
   scroll.Controls.Add(col);
   var head = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Th.Bg }; head.Controls.Add(Title("설정")); head.Controls.Add(Sub("바꾸면 바로 저장되고, 이벤트 정리 결과에도 바로 반영돼요."));
   pageSet.Controls.Add(scroll); pageSet.Controls.Add(head);
   const int W = 760;
   Func<string, Control> cap = (t) => new Label { Text = t, ForeColor = Th.Sub, Font = Th.F(9, true), AutoSize = true, Margin = new Padding(4, col.Controls.Count == 0 ? 0 : 18, 0, 6), BackColor = Th.Bg };
   Func<string, Control> hint = (t) => new Label { Text = t, ForeColor = Th.Sub, Font = Th.F(9), AutoSize = true, MaximumSize = new Size(W, 0), Margin = new Padding(4, 6, 0, 0), BackColor = Th.Bg };
   // 정성도
   col.Controls.Add(cap("정성도"));
   var g1 = new Group { Width = W, Margin = Padding.Empty };
   var rLink = g1.AddRow(new Row("확인할 링크", 150));
   linkVal = rLink.Val; linkBox = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Th.Fill, Font = Th.F(9.5f), Visible = false, Location = new Point(166, 12), Width = 430 }; rLink.Controls.Add(linkBox); linkBox.BringToFront();
   linkBtn = new Btn("링크 수정", Btn.K.Link, () => EditLink()); rLink.Add(linkBtn);
   rLink.Add(new Btn("기본값", Btn.K.Plain, () => { Set.LinkPrefix = Settings.DefaultLink; Set.Save(); ShowLink(); Reanalyze(); }));
   linkBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditLink(); } };
   g1.AddRow(new Row("상", 150)).Val.Text = "링크 + 이미지, 또는 링크 + 표";
   g1.AddRow(new Row("중", 150)).Val.Text = "이미지, 표, 링크 중 하나";
   g1.AddRow(new Row("하", 150)).Val.Text = "글만 있음";
   col.Controls.Add(g1);
   col.Controls.Add(hint("본문에 확인할 링크로 시작하는 주소가 있으면 '링크 있음'이에요. 다른 광고주 이벤트에 쓸 때 링크만 바꾸면 돼요. 표는 본문에 들어간 표(<table>)를 찾아요."));
   // 참여 기준
   col.Controls.Add(cap("참여 기준"));
   var g2 = new Group { Width = W, Margin = Padding.Empty };
   NumRow(g2, "특별 미션 조회수 합", Set.SpecialViews, "이상", v => Set.SpecialViews = v);
   NumRow(g2, "글 최소 조회수", Set.MinViews, "이상", v => Set.MinViews = v);
   NumRow(g2, "카페 가입자 수", Set.MinMembers, "명 이하는 제외", v => Set.MinMembers = v);
   NumRow(g2, "선착순 제외 기간", Set.BlackMonths, "개월 (선착순 당첨 이력)", v => Set.BlackMonths = v);
   NumRow(g2, "복붙의심", Set.CopyPercent, "% 이상 같은 글", v => Set.CopyPercent = Math.Max(50, Math.Min(100, v)));
   col.Controls.Add(g2);
   col.Controls.Add(hint("같은 커뮤니티에 글을 2개 이상 냈으면 조회수가 많은 글 1개만 인정하고 나머지는 '커뮤니티 중복'으로 빼요."));
   // 사이트
   col.Controls.Add(cap("사이트"));
   var g3 = new Group { Width = W, Margin = Padding.Empty };
   var rEx = g3.AddRow(new Row("제외 사이트", 150)); rEx.Add(new Btn("편집", Btn.K.Link, () => EditSites(true, rEx)));
   var rMan = g3.AddRow(new Row("직접 확인 사이트", 150)); rMan.Add(new Btn("편집", Btn.K.Link, () => EditSites(false, rMan)));
   rEx.Val.Text = SitesText(Set.ExcludeSites); rMan.Val.Text = SitesText(Set.ManualSites);
   col.Controls.Add(g3);
   col.Controls.Add(hint("네이버 카페와 디시는 자동으로 읽어요. 그 밖의 사이트 글은 직접 검토로 보내요."));
   // 저장
   col.Controls.Add(cap("저장"));
   var g4 = new Group { Width = W, Margin = new Padding(0, 0, 0, 20) };
   var rFolder = g4.AddRow(new Row("엑셀 저장 폴더", 150));
   rFolder.Val.Text = Set.SaveFolder != "" ? Set.SaveFolder : "raw 파일과 같은 폴더";
   rFolder.Add(new Btn("변경", Btn.K.Link, () => { using (var d = new FolderBrowserDialog { SelectedPath = Set.SaveFolder }) if (d.ShowDialog(this) == DialogResult.OK) { Set.SaveFolder = d.SelectedPath; Set.Save(); rFolder.Val.Text = Set.SaveFolder; } }));
   rFolder.Add(new Btn("기본값", Btn.K.Plain, () => { Set.SaveFolder = ""; Set.Save(); rFolder.Val.Text = "raw 파일과 같은 폴더"; }));
   var rHist = g4.AddRow(new Row("당첨 이력 파일", 150)); rHist.Val.Text = Hist.Path; rHist.Add(new Btn("폴더 열기", Btn.K.Link, () => { try { Process.Start("explorer.exe", "/select,\"" + Hist.Path + "\""); } catch { } }));
   col.Controls.Add(g4);
   ShowLink();
  }
  void NumRow(Group g, string key, int value, string unit, Action<int> apply) {
   var r = g.AddRow(new Row(key, 150)); var nb = new NumBox(value, 80); nb.Margin = new Padding(0, 1, 6, 0);
   r.Tools.Controls.Add(nb); r.Tools.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = Th.Sub, Margin = new Padding(0, 6, 4, 0), BackColor = Th.Card });
   nb.Leave += (s, e) => { int v = nb.Value(value); if (v <= 0) { nb.Text = value.ToString(); return; } value = v; apply(v); Set.Save(); Reanalyze(); };
   nb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SelectNextControl(nb, true, true, true, true); } };
  }
  static string SitesText(string list) { return String.Join(", ", Settings.Sites(list).Select(s => s.Value.Replace("개인 채널(", "").TrimEnd(')')).Distinct()); }
  void EditSites(bool exclude, Row row) {
   var a = new Ask(exclude ? "제외 사이트" : "직접 확인 사이트", "한 줄에 하나씩 '주소|이름'으로 적어요. 예) cafe.daum.net|다음카페");
   var tb = a.Field("목록", new TextBox { Multiline = true, Height = 200, ScrollBars = ScrollBars.Vertical, Text = String.Join("\r\n", Settings.Sites(exclude ? Set.ExcludeSites : Set.ManualSites).Select(s => s.Key + "|" + s.Value)) });
   if (!a.Run(this)) return;
   string v = String.Join(";", tb.Lines.Select(l => l.Trim()).Where(l => l != ""));
   if (exclude) Set.ExcludeSites = v; else Set.ManualSites = v; Set.Save(); row.Val.Text = SitesText(v);
   if (Participants.Count > 0) MessageBox.Show(this, "사이트 목록은 '다시 확인'을 누르면 반영돼요.", "설정");
  }
  void ShowLink() { linkVal.Text = System.Text.RegularExpressions.Regex.Replace(Set.LinkPrefix, @"^https?://(www\.)?", ""); }
  void EditLink() {
   if (!linkBox.Visible) { linkBox.Text = Set.LinkPrefix; linkBox.Visible = true; linkVal.Visible = false; linkBtn.Text = "완료"; linkBtn.FitWidth(); linkBox.Focus(); linkBox.SelectAll(); return; }
   string v = linkBox.Text.Trim(); if (v != "") { Set.LinkPrefix = v; Set.Save(); }
   linkBox.Visible = false; linkVal.Visible = true; linkBtn.Text = "링크 수정"; linkBtn.FitWidth(); ShowLink(); Reanalyze();
  }

  // ── 화면 캡처 (개발 확인용)
  public void Shot(string dir) {
   Directory.CreateDirectory(dir);
   Action<string> snap = (name) => { Application.DoEvents(); using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(Path.Combine(dir, name), ImageFormat.Png); } };
   Show(pageMain); snap("1_main.png");
   using (var sheet = new ReviewSheet(this)) { sheet.StartPosition = FormStartPosition.Manual; sheet.Location = new Point(-5000, -5000); sheet.Show(); Application.DoEvents(); using (var bmp = new Bitmap(sheet.Width, sheet.Height)) { sheet.DrawToBitmap(bmp, new Rectangle(0, 0, sheet.Width, sheet.Height)); bmp.Save(Path.Combine(dir, "2_review.png"), ImageFormat.Png); } sheet.Close(); }
   Show(pageHist); snap("3_history.png");
   Show(pageSet); snap("4_settings.png");
   Show(pageEmpty); snap("0_empty.png");
  }
 }

 static class Program {
  [STAThread]
  static void Main(string[] args) {
   if (args.Length >= 2 && args[0] == "--make-icon") { Logo.SaveIco(args[1]); return; }
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   if (args.Length >= 3 && args[0] == "--selftest") { SelfTest(args[1], args[2], args.Length > 3 ? args[3] : ""); return; }
   if (args.Length >= 3 && args[0] == "--importtest") {
    var sb = new StringBuilder();
    try { var r = ListImporter.Read(args[1]); sb.AppendLine("title=" + r.Title + " month=" + r.Month + " round=" + r.Round); foreach (var e in r.Entries) sb.AppendLine(e.Kind + "\t" + e.Nick + "\t" + e.Id); } catch (Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
    File.WriteAllText(args[2], sb.ToString(), Encoding.UTF8); return;
   }
   if (args.Length >= 3 && args[0] == "--shot") {
    var f = new MainForm(null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
    if (args.Length > 3) f.Set.CommentUrl = args[3];
    f.Show(); f.LoadRawSync(args[1]); f.Shot(args[2]); f.Close(); return;
   }
   Application.Run(new MainForm(args.Length > 0 && File.Exists(args[0]) ? args[0] : null));
  }
  // 화면 없이 처음부터 끝까지 돌려 보기 (개발 확인용)
  static void SelfTest(string raw, string outXlsx, string commentUrl) {
   var sb = new StringBuilder();
   try {
    var set = Settings.Load(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "설정.ini")); set.Path = Path.Combine(Path.GetTempPath(), "raffle_selftest.ini");
    if (commentUrl != "") set.CommentUrl = commentUrl;
    string err; var ps = RawReader.Read(raw, out err); if (err != "") { sb.AppendLine("ERR " + err); File.WriteAllText(outXlsx + ".txt", sb.ToString(), Encoding.UTF8); return; }
    var sw = Stopwatch.StartNew();
    Parallel.ForEach(ps.SelectMany(p => p.Posts).ToList(), new ParallelOptions { MaxDegreeOfParallelism = 4 }, p => { try { Fetcher.Fetch(p, set); } catch (Exception ex) { p.BaseState = PState.Review; p.BaseKind = RKind.Manual; p.ManualWhy = "오류 " + ex.Message; } });
    List<string> nicks = null; string cerr = "";
    if (set.CommentUrl != "") nicks = Cafe.CommentNicks(set.CommentUrl, out cerr);
    var hist = new History();
    var sum = Engine.Analyze(ps, set, cerr == "" ? nicks : null, hist, new Dictionary<string, Decision>(), DateTime.Today);
    sb.AppendLine("time " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s  comments=" + (nicks == null ? "-" : nicks.Count.ToString()) + " " + cerr);
    sb.AppendLine("responses=" + sum.Responses + " posts=" + sum.Posts + " special=" + sum.Special + " first=" + sum.First + " excluded=" + sum.Excluded + " review=" + sum.Review + " reviewItems=" + Engine.ReviewCount(ps));
    sb.AppendLine("event=" + RawReader.EventName(raw) + " | base=" + Exporter.BaseName(set, RawReader.EventName(raw)));
    foreach (var pa in ps) {
     sb.AppendLine(pa.No + "\t" + pa.Section + "\t" + pa.Nick + "\t" + pa.Id + "\tsum=" + pa.Sum + "\tcmt=" + (pa.CommentMatched ? "Y" : "N:" + pa.CommentHint) + "\t" + pa.Reason + "\t" + pa.Note);
     foreach (var p in pa.Posts) sb.AppendLine("    " + p.State + "/" + p.Kind + "\t" + p.Community + "\tv=" + p.EffViews + "\tm=" + p.Members + "\tq=" + p.EffQuality + "\tlen=" + p.TextLen + "\tlink=" + (p.HasLink ? 1 : 0) + "\timg=" + (p.HasImage ? 1 : 0) + "\ttable=" + (p.HasTable ? 1 : 0) + "\t" + p.Reason + p.ManualWhy + (p.CopyOf != null ? "\tcopyOf=" + p.CopyOf.P.No + "(" + p.CopyPct + "%)" : "") + "\t" + p.Url);
    }
    Exporter.Save(outXlsx, RawReader.EventName(raw), ps, set);
    sb.AppendLine("saved " + outXlsx);
   } catch (Exception ex) { sb.AppendLine("EXCEPTION " + ex); }
   File.WriteAllText(outXlsx + ".txt", sb.ToString(), Encoding.UTF8);
  }
 }


}
