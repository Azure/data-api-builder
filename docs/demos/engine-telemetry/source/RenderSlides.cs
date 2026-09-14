// Standalone documentation renderer for Windows / .NET Framework 4.x.
// References: System.Drawing.dll, System.Web.Extensions.dll.
// The single argument is the absolute engine-telemetry demo directory.
// Writes only generated PNGs. No audio, telemetry, or cloud service calls.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public sealed class Deck
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Slide> Slides { get; set; }
}

public sealed class Slide
{
    public int Number { get; set; }
    public string Chapter { get; set; }
    public string Layout { get; set; }
    public string Title { get; set; }
    public string Subtitle { get; set; }
    public List<Card> Cards { get; set; }
    public string Takeaway { get; set; }
    public List<Lane> Lanes { get; set; }
    public string[] Nodes { get; set; }
    public string[] Headers { get; set; }
    public float[] ColumnWidths { get; set; }
    public string[][] Rows { get; set; }
    public bool CodeFirstColumn { get; set; }
    public string Detail { get; set; }
}

public sealed class Card
{
    public string Label { get; set; }
    public string Title { get; set; }
    public string[] Points { get; set; }
    public string Note { get; set; }
}

public sealed class Lane
{
    public string Label { get; set; }
    public string[] Nodes { get; set; }
}

public static class RenderSlides
{
    private const string Background = "#101828";
    private const string SurfaceColor = "#1B283C";
    private const string Border = "#30425B";
    private const string Foreground = "#F4F7FC";
    private const string Muted = "#B6C6DA";
    private const string Accent = "#63E0CB";
    private const string Amber = "#FFD19A";
    private const string Banner = "#183B40";
    private static readonly List<string> Errors = new List<string>();
    private static Graphics graphics;
    private static string slideNumber;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) { throw new ArgumentException("Pass the demo directory as the only argument."); }
            Render(Path.GetFullPath(args[0]));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static Color ColorOf(string hex) { return ColorTranslator.FromHtml(hex); }

    private static void Initialize(Bitmap bitmap)
    {
        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(ColorOf(Background));
    }

    private static float TextHeight(string text, float width, float size, string face)
    {
        using (Font font = new Font(face, size, FontStyle.Regular, GraphicsUnit.Pixel))
        using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            return graphics.MeasureString(text, font, new SizeF(width, 10000), format).Height;
        }
    }

    private static float Text(string text, float x, float y, float width, float height,
        float size = 34, string color = Foreground, string face = "Segoe UI",
        StringAlignment alignment = StringAlignment.Near)
    {
        using (Font font = new Font(face, size, FontStyle.Regular, GraphicsUnit.Pixel))
        using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
        using (SolidBrush brush = new SolidBrush(ColorOf(color)))
        {
            format.Alignment = alignment;
            format.Trimming = StringTrimming.None;
            SizeF measured = graphics.MeasureString(text, font, new SizeF(width, 10000), format);
            if (measured.Height > height + 1 || measured.Width > width + 1)
            {
                Errors.Add(string.Format("Slide {0}, ({1:0}, {2:0}), needs {3:0}px height / has {4:0}px: {5}",
                    slideNumber, x, y, measured.Height, height, text.Replace('\n', ' ')));
            }
            graphics.DrawString(text, font, brush, new RectangleF(x, y, width, Math.Max(height, 1)), format);
            return measured.Height;
        }
    }

    private static void Surface(float x, float y, float width, float height,
        string fill = SurfaceColor, float radius = 18, string border = Border)
    {
        using (GraphicsPath path = new GraphicsPath())
        using (SolidBrush brush = new SolidBrush(ColorOf(fill)))
        using (Pen pen = new Pen(ColorOf(border), 1))
        {
            float diameter = 2 * radius;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
            graphics.DrawPath(pen, path);
        }
    }

    private static void Line(float x1, float y1, float x2, float y2, string color = Border, float width = 2)
    {
        using (Pen pen = new Pen(ColorOf(color), width)) { graphics.DrawLine(pen, x1, y1, x2, y2); }
    }

    private static void Arrow(float x1, float x2, float y)
    {
        Line(x1, y, x2, y, Accent, 3);
        Line(x2 - 10, y - 8, x2, y, Accent, 3);
        Line(x2 - 10, y + 8, x2, y, Accent, 3);
    }

    private static void DrawCard(Card card, float x, float y, float width, float height,
        float titleSize = 38, float bodySize = 32, bool compact = false)
    {
        Surface(x, y, width, height);
        float pad = compact ? 22 : 28;
        float innerWidth = width - 2 * pad;
        float cursor = y + pad;
        cursor += Text(card.Label, x + pad, cursor, innerWidth, 34,
            compact ? 21 : 22, Accent, "Segoe UI Semibold");
        cursor += compact ? 8 : 10;
        cursor += Text(card.Title, x + pad, cursor, innerWidth, 112, titleSize, Foreground, "Segoe UI Semibold");
        cursor += compact ? 10 : 14;

        float limit = y + height - pad;
        float noteHeight = 0;
        float noteTop = 0;
        if (!string.IsNullOrEmpty(card.Note))
        {
            noteHeight = TextHeight(card.Note, innerWidth, 26, "Segoe UI");
            noteTop = y + height - pad - noteHeight;
            limit = noteTop - 32;
        }
        foreach (string point in card.Points)
        {
            cursor += Text(point, x + pad, cursor, innerWidth, limit - cursor, bodySize, Muted);
            cursor += 8;
        }
        if (noteHeight > 0)
        {
            Line(x + pad, noteTop - 18, x + width - pad, noteTop - 18);
            Text(card.Note, x + pad, noteTop, innerWidth, noteHeight + 1, 26, Amber);
        }
    }

    private static void Nodes(string[] nodes, float y, float height = 142)
    {
        const float gap = 40;
        float width = (1728 - (nodes.Length - 1) * gap) / nodes.Length;
        for (int i = 0; i < nodes.Length; i++)
        {
            float x = 96 + i * (width + gap);
            Surface(x, y, width, height);
            Text(nodes[i], x + 18, y + 23, width - 36, height - 32, 34, Foreground, "Segoe UI Semibold", StringAlignment.Center);
            if (i < nodes.Length - 1) { Arrow(x + width + 6, x + width + gap - 7, y + height / 2); }
        }
    }

    private static void Table(Slide slide, float top)
    {
        const float headerHeight = 66;
        float rowHeight = slide.Rows.Length == 3 ? 112 : 92;
        float height = headerHeight + slide.Rows.Length * rowHeight;
        Surface(96, top, 1728, height);
        float x = 96;
        for (int column = 0; column < slide.Headers.Length; column++)
        {
            Text(slide.Headers[column], x + 24, top + 20, slide.ColumnWidths[column] - 48, 36, 23, Accent, "Segoe UI Semibold");
            x += slide.ColumnWidths[column];
        }
        for (int row = 0; row < slide.Rows.Length; row++)
        {
            float y = top + headerHeight + row * rowHeight;
            Line(96, y, 1824, y);
            x = 96;
            for (int column = 0; column < slide.Headers.Length; column++)
            {
                bool code = column == 0 && slide.CodeFirstColumn;
                string color = code ? Accent : (column == slide.Headers.Length - 1 ? Amber : Foreground);
                Text(slide.Rows[row][column], x + 24, y + 26, slide.ColumnWidths[column] - 48, rowHeight - 30,
                    code ? 31 : 32, color, code ? "Consolas" : "Segoe UI");
                x += slide.ColumnWidths[column];
            }
        }
        Text(slide.Detail, 96, top + height + 24, 1728, 54, 28, Muted);
    }

    private static void Content(Slide slide, float top)
    {
        const float bottom = 874;
        switch (slide.Layout)
        {
            case "hero":
                for (int i = 0; i < 3; i++) { DrawCard(slide.Cards[i], 96 + i * 584, 570, 560, 270, 44, 34); }
                break;
            case "columns":
                for (int i = 0; i < 2; i++) { DrawCard(slide.Cards[i], 96 + i * 876, top, 852, bottom - top, 42, 33); }
                break;
            case "three":
                for (int i = 0; i < 3; i++) { DrawCard(slide.Cards[i], 96 + i * 584, top, 560, bottom - top, 38, 32); }
                break;
            case "four":
                float height = (bottom - top - 24) / 2;
                for (int i = 0; i < 4; i++)
                {
                    DrawCard(slide.Cards[i], 96 + i % 2 * 876, top + i / 2 * (height + 24), 852, height, 34, 30, true);
                }
                break;
            case "table":
                Table(slide, top);
                break;
            case "lanes":
                for (int i = 0; i < 2; i++)
                {
                    float y = top + i * 222;
                    Text(slide.Lanes[i].Label, 100, y, 1724, 36, 24, Accent, "Segoe UI Semibold");
                    Nodes(slide.Lanes[i].Nodes, y + 48, 136);
                }
                break;
            case "flow_cards":
                Nodes(slide.Nodes, top, 146);
                for (int i = 0; i < 3; i++) { DrawCard(slide.Cards[i], 96 + i * 584, top + 182, 560, 278, 34, 31); }
                break;
            case "stages":
                float[] widths = { 620, 392, 620 };
                float x = 96;
                for (int i = 0; i < 3; i++)
                {
                    DrawCard(slide.Cards[i], x, top, widths[i], 340, 38, 31);
                    if (i < 2) { Arrow(x + widths[i] + 7, x + widths[i] + 39, top + 170); }
                    x += widths[i] + 48;
                }
                Text(slide.Detail, 100, top + 370, 1724, 74, 30, Muted);
                break;
            default:
                throw new InvalidOperationException("Unknown slide layout: " + slide.Layout);
        }
    }

    private static void Render(string root)
    {
        Deck deck = new JavaScriptSerializer().Deserialize<Deck>(
            File.ReadAllText(Path.Combine(root, "source", "slide-content.json"), Encoding.UTF8));
        if (deck.Width != 1920 || deck.Height != 1080 || deck.Slides.Count != 12)
        {
            throw new InvalidOperationException("Expected exactly twelve 1920 x 1080 slides.");
        }
        string output = Path.Combine(root, "slides");
        Directory.CreateDirectory(output);
        for (int index = 0; index < deck.Slides.Count; index++)
        {
            Slide slide = deck.Slides[index];
            slideNumber = slide.Number.ToString("D2");
            if (slide.Number != index + 1) { throw new InvalidOperationException("Slide numbers must be consecutive from 1 to 12."); }
            string script = Path.Combine(root, "scripts", "text-to-speech-script" + slideNumber + ".txt");
            if (!File.Exists(script)) { throw new FileNotFoundException("Missing narration script: " + script); }

            using (Bitmap bitmap = new Bitmap(deck.Width, deck.Height))
            {
                Initialize(bitmap);
                using (graphics)
                {
                    Text("DAB / ENGINE TELEMETRY", 96, 46, 900, 40, 26, Accent, "Segoe UI Semibold");
                    Text(slide.Chapter, 936, 47, 888, 40, 24, Muted, "Segoe UI Semibold", StringAlignment.Far);
                    Line(96, 108, 1824, 108);
                    float titleSize = slide.Layout == "hero" ? 92 : 78;
                    float titleHeight = Text(slide.Title, 96, 150, 1728, 280, titleSize, Foreground, "Segoe UI Semibold");
                    float subtitleY = 150 + titleHeight + 18;
                    float subtitleHeight = Text(slide.Subtitle, 100, subtitleY, 1724, 100, 35, Muted);
                    float top = Math.Max(414, subtitleY + subtitleHeight + 40);
                    Content(slide, top);
                    Surface(96, 922, 1728, 72, Banner, 12, Banner);
                    Text(slide.Takeaway, 122, 938, 1676, 44, 30, Accent, "Segoe UI Semibold");
                    Text("DESIGN DISCUSSION / NO ENGINE FEATURE IMPLEMENTED / 09 SEP 2026", 96, 1021, 1570, 32, 22, Muted);
                    Text(slideNumber + " / 12", 1696, 1016, 128, 42, 28, Foreground, "Segoe UI Semibold", StringAlignment.Far);
                    Line(0, 1077, 1920f * (index + 1) / 12, 1077, Accent, 6);
                    bitmap.Save(Path.Combine(output, "slide" + slideNumber + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("Rendered slide" + slideNumber + ".png");
        }
        if (Errors.Count > 0) { throw new InvalidOperationException(string.Join(Environment.NewLine, Errors.ToArray())); }
        Overview(deck, root, output);
        Console.WriteLine("All twelve slides passed text-fit checks. No audio or video was generated.");
    }

    private static void Overview(Deck deck, string root, string output)
    {
        using (Bitmap bitmap = new Bitmap(1536, 1388))
        {
            Initialize(bitmap);
            using (graphics)
            {
                slideNumber = "overview";
                Text("ENGINE TELEMETRY / 12-SLIDE WALKTHROUGH", 24, 24, 1488, 48, 34, Accent, "Segoe UI Semibold");
                for (int index = 0; index < deck.Slides.Count; index++)
                {
                    string suffix = (index + 1).ToString("D2");
                    using (Image image = Image.FromFile(Path.Combine(output, "slide" + suffix + ".png")))
                    {
                        int x = 24 + index % 3 * 504;
                        int y = 90 + index / 3 * 320;
                        graphics.DrawImage(image, new Rectangle(x, y, 480, 270));
                        Text(suffix + " / " + deck.Slides[index].Chapter, x, y + 280, 480, 35, 18, Muted, "Segoe UI Semibold");
                    }
                }
                bitmap.Save(Path.Combine(root, "slide-overview.png"), ImageFormat.Png);
            }
        }
        if (Errors.Count > 0) { throw new InvalidOperationException(string.Join(Environment.NewLine, Errors.ToArray())); }
        Console.WriteLine("Rendered slide-overview.png");
    }
}
