using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace TabletModeSwitcher;

/// <summary>
/// 生成应用程序图标
/// </summary>
public static class IconGenerator
{
    /// <summary>
    /// 生成并保存图标文件
    /// </summary>
    public static void GenerateIconFile(string outputPath)
    {
        // 创建多尺寸图标
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var images = new List<Bitmap>();

        foreach (var size in sizes)
        {
            images.Add(CreateTabletIcon(size));
        }

        SaveAsIcon(images, outputPath);

        foreach (var img in images)
        {
            img.Dispose();
        }
    }

    /// <summary>
    /// 创建平板模式切换器图标
    /// 设计：平板 + 可拆卸键盘底座，Surface 风格
    /// </summary>
    private static Bitmap CreateTabletIcon(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float scale = size / 64f;
        float padding = 4 * scale;

        // 颜色方案 - Surface 风格
        var tabletColor = Color.FromArgb(45, 45, 48);        // 深灰色平板
        var screenColor = Color.FromArgb(0, 120, 212);       // Windows 蓝色屏幕
        var keyboardColor = Color.FromArgb(100, 100, 105);   // 键盘底座
        var accentColor = Color.FromArgb(0, 153, 255);       // 高亮色
        var keyColor = Color.FromArgb(180, 180, 185);        // 键帽颜色

        // === 绘制平板部分 ===
        float tabletX = padding;
        float tabletY = padding;
        float tabletWidth = size - padding * 2;
        float tabletHeight = (size - padding * 2) * 0.6f;

        // 平板外壳
        using (var tabletBrush = new SolidBrush(tabletColor))
        {
            var tabletRect = new RectangleF(tabletX, tabletY, tabletWidth, tabletHeight);
            float cornerRadius = 4 * scale;
            using var path = CreateRoundedRectangle(tabletRect, cornerRadius);
            g.FillPath(tabletBrush, path);
        }

        // 屏幕（内部蓝色区域）
        float screenPadding = 3 * scale;
        using (var screenBrush = new SolidBrush(screenColor))
        {
            var screenRect = new RectangleF(
                tabletX + screenPadding,
                tabletY + screenPadding,
                tabletWidth - screenPadding * 2,
                tabletHeight - screenPadding * 2);
            float screenRadius = 2 * scale;
            using var path = CreateRoundedRectangle(screenRect, screenRadius);
            g.FillPath(screenBrush, path);
        }

        // 屏幕上的 Windows 图标（简化版）
        if (size >= 32)
        {
            float winSize = 8 * scale;
            float winX = tabletX + (tabletWidth - winSize) / 2;
            float winY = tabletY + (tabletHeight - winSize) / 2;
            float gap = 1 * scale;
            float blockSize = (winSize - gap) / 2;

            using var winBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            // 四个方块
            g.FillRectangle(winBrush, winX, winY, blockSize, blockSize);
            g.FillRectangle(winBrush, winX + blockSize + gap, winY, blockSize, blockSize);
            g.FillRectangle(winBrush, winX, winY + blockSize + gap, blockSize, blockSize);
            g.FillRectangle(winBrush, winX + blockSize + gap, winY + blockSize + gap, blockSize, blockSize);
        }

        // === 绘制键盘底座 ===
        float keyboardY = tabletY + tabletHeight + 2 * scale;
        float keyboardHeight = (size - padding * 2) * 0.28f;

        // 键盘底座主体
        using (var kbBrush = new SolidBrush(keyboardColor))
        {
            var kbRect = new RectangleF(tabletX, keyboardY, tabletWidth, keyboardHeight);
            float kbRadius = 3 * scale;
            using var path = CreateRoundedRectangle(kbRect, kbRadius);
            g.FillPath(kbBrush, path);
        }

        // 绘制键盘按键
        if (size >= 24)
        {
            float keyPadding = 3 * scale;
            float keyAreaWidth = tabletWidth - keyPadding * 2;
            float keyAreaHeight = keyboardHeight - keyPadding * 2;
            float keyStartX = tabletX + keyPadding;
            float keyStartY = keyboardY + keyPadding;

            int cols = size >= 48 ? 8 : (size >= 32 ? 6 : 4);
            int rows = size >= 32 ? 2 : 1;

            float keyWidth = (keyAreaWidth - (cols - 1) * scale) / cols;
            float keyHeight = (keyAreaHeight - (rows - 1) * scale) / rows;

            using var keyBrush = new SolidBrush(keyColor);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    float kx = keyStartX + col * (keyWidth + scale);
                    float ky = keyStartY + row * (keyHeight + scale);
                    g.FillRectangle(keyBrush, kx, ky, keyWidth, keyHeight);
                }
            }
        }

        // === 绘制连接指示器（小三角形表示可拆卸） ===
        if (size >= 32)
        {
            float indicatorSize = 4 * scale;
            float indicatorX = tabletX + tabletWidth / 2;
            float indicatorY = tabletY + tabletHeight + 1 * scale;

            using var indicatorBrush = new SolidBrush(accentColor);
            var points = new PointF[]
            {
                new(indicatorX - indicatorSize / 2, indicatorY),
                new(indicatorX + indicatorSize / 2, indicatorY),
                new(indicatorX, indicatorY + indicatorSize * 0.6f)
            };
            g.FillPolygon(indicatorBrush, points);
        }

        return bitmap;
    }

    /// <summary>
    /// 创建圆角矩形路径
    /// </summary>
    private static GraphicsPath CreateRoundedRectangle(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;

        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// 保存为 ICO 文件
    /// </summary>
    private static void SaveAsIcon(List<Bitmap> images, string outputPath)
    {
        using var stream = new FileStream(outputPath, FileMode.Create);
        using var writer = new BinaryWriter(stream);

        // ICO 文件头
        writer.Write((short)0);           // 保留，必须为 0
        writer.Write((short)1);           // 类型：1 = ICO
        writer.Write((short)images.Count); // 图像数量

        // 计算数据偏移
        int dataOffset = 6 + images.Count * 16; // 头部 + 目录

        var imageDataList = new List<byte[]>();

        // 写入目录条目
        foreach (var img in images)
        {
            using var pngStream = new MemoryStream();
            img.Save(pngStream, ImageFormat.Png);
            var pngData = pngStream.ToArray();
            imageDataList.Add(pngData);

            writer.Write((byte)(img.Width >= 256 ? 0 : img.Width));   // 宽度
            writer.Write((byte)(img.Height >= 256 ? 0 : img.Height)); // 高度
            writer.Write((byte)0);         // 调色板颜色数
            writer.Write((byte)0);         // 保留
            writer.Write((short)1);        // 色彩平面数
            writer.Write((short)32);       // 位深度
            writer.Write(pngData.Length);  // 图像数据大小
            writer.Write(dataOffset);      // 数据偏移

            dataOffset += pngData.Length;
        }

        // 写入图像数据
        foreach (var data in imageDataList)
        {
            writer.Write(data);
        }
    }

    /// <summary>
    /// 从图标文件加载图标
    /// </summary>
    public static Icon LoadIconFromFile(string path, int size)
    {
        using var icon = new Icon(path);
        return new Icon(icon, size, size);
    }
}
