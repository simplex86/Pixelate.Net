using System.Runtime.CompilerServices;

namespace Pixelate.Net
{
    /// <summary>
    /// 像素化算法入口。该类不依赖任何图像库，仅处理 RGBA 字节缓冲，
    /// 便于在不同前端（Avalonia / WinUI / 命令行）中复用。
    /// </summary>
    public static class ImagePixelator
    {
        // 人眼各通道敏感度（BT.601），用于 Realistic 模式映射到最近调色板色。
        private const double Wr = 0.299, Wg = 0.587, Wb = 0.114;

        // 4x4 Bayer 有序抖动矩阵（值 0-15，按蓝噪声分布，周期 4 块）
        private static readonly byte[,] Bayer4x4 = {
            {  0,  8,  2, 10 },
            { 12,  4, 14,  6 },
            {  3, 11,  1,  9 },
            { 15,  7, 13,  5 }
        };
        private const float BayerStrength = 32f; // 抖动强度：偏移范围 ±16

        /// <summary>
        /// 根据 HorizontalSplits 计算输出尺寸。
        /// </summary>
        public static (int Width, int Height) GetOutputSize(int width, int height, int horizontalSplits)
        {
            if (horizontalSplits < 1) throw new ArgumentOutOfRangeException(nameof(horizontalSplits));
            int ps = Math.Max(1, (int)Math.Ceiling((double)width / horizontalSplits));
            int outW = (width + ps - 1) / ps;
            int outH = (height + ps - 1) / ps;
            return (outW, outH);
        }

        /// <summary>
        /// 根据图像颜色分布自动计算颜色合并阈值。
        /// 供前端在加载图像后获取默认阈值、或重置阈值时使用。
        /// </summary>
        public static int ComputeAutoThreshold(ReadOnlySpan<byte> sourceRgba, int width, int height)
        {
            int pixelCount = width * height;
            return ColorMerger.ComputeAutoThreshold(sourceRgba, pixelCount);
        }

        /// <summary>
        /// 对给定的 RGBA 像素数据进行像素化。
        /// </summary>
        /// <param name="sourceRgba">源像素，行优先，每像素 4 字节（R, G, B, A），非预乘。</param>
        /// <param name="width">源图像宽度。</param>
        /// <param name="height">源图像高度。</param>
        /// <param name="options">像素化参数。</param>
        /// <returns>像素化后的 RGBA 数据（每像素 4 字节，行优先，行步长 = 输出宽度 × 4）。</returns>
        public static byte[] Pixelate(ReadOnlySpan<byte> sourceRgba, int width, int height, PixelateOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (options.HorizontalSplits < 1) throw new ArgumentOutOfRangeException(nameof(options), "HorizontalSplits 必须 >= 1");
            int expected = width * height * 4;
            if (sourceRgba.Length < expected)
                throw new ArgumentException("源缓冲区过小，与指定的宽高不匹配。", nameof(sourceRgba));

            // 像素块大小由横向分割数推导
            int ps = Math.Max(1, (int)Math.Ceiling((double)width / options.HorizontalSplits));
            int outW = (width + ps - 1) / ps;
            int outH = (height + ps - 1) / ps;
            byte[] output = new byte[outW * outH * 4];

            // 颜色合并（算法层不自动计算阈值，由前端设置）
            byte[]? palette = null;
            int[]? assignments = null;
            int pixelCount = width * height;

            if (options.Brand != BeadBrand.None)
            {
                // 品牌色卡模式：调色板固定为该品牌官方色，跳过 ColorMerger 任意聚类。
                palette = BeadPalettes.GetRgbBytes(options.Brand);
                // assignments 留 null，由 Cartoon 路径按需构建。
            }
            else
            {
                int effectiveThreshold = options.ColorMergeThreshold;
                if (effectiveThreshold > 0)
                {
                    (palette, assignments) = ColorMerger.Merge(sourceRgba, pixelCount, effectiveThreshold);
                }
            }

            // 分块取色
            if (options.Dither && palette != null)
            {
                PixelateOrdered(sourceRgba, width, height, ps, outW, outH, palette, output);
            }
            else if (options.Mode == ProcessMode.Cartoon)
                PixelateCartoon(sourceRgba, width, height, ps, outW, outH, palette, assignments, output);
            else
                PixelateRealistic(sourceRgba, width, height, ps, outW, outH, palette, output);

            return output;
        }

        /// <summary>
        /// 真实模式：块内平均 RGB。若有调色板，平均后映射到最近调色板色。
        /// 过渡柔和，适合照片。
        /// </summary>
        private static void PixelateRealistic(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH,
            byte[]? palette, byte[] dst)
        {
            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = oy * ps;
                int y1 = Math.Min(y0 + ps, height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = ox * ps;
                    int x1 = Math.Min(x0 + ps, width);

                    long r = 0, g = 0, b = 0, a = 0;
                    int count = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * width * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            r += src[i];
                            g += src[i + 1];
                            b += src[i + 2];
                            a += src[i + 3];
                            count++;
                        }
                    }

                    byte avgR = (byte)(r / count);
                    byte avgG = (byte)(g / count);
                    byte avgB = (byte)(b / count);

                    int di = (oy * outW + ox) * 4;
                    if (palette != null)
                    {
                        // 映射到最近调色板色
                        int best = NearestPaletteIndex(avgR, avgG, avgB, palette);
                        dst[di] = palette[best * 3];
                        dst[di + 1] = palette[best * 3 + 1];
                        dst[di + 2] = palette[best * 3 + 2];
                    }
                    else
                    {
                        dst[di] = avgR;
                        dst[di + 1] = avgG;
                        dst[di + 2] = avgB;
                    }
                    dst[di + 3] = (byte)(a / count);
                }
            }
        }

        /// <summary>
        /// 有序抖动（Bayer 4x4 矩阵）：块内平均色 + 位置相关阈值偏移，再映射到最近调色板色。
        /// 图案规律、可预测，适合拼豆装配。无误差扩散，无方向性偏差。
        /// Cartoon 与 Realistic 模式在抖动下统一为"平均色 + 偏移 → 最近调色板色"，
        /// 候选范围为整个调色板（不做 top-3 限制）。
        /// </summary>
        private static void PixelateOrdered(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH,
            byte[] palette, byte[] dst)
        {
            const int bayerSize = 4;
            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = oy * ps;
                int y1 = Math.Min(y0 + ps, height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = ox * ps;
                    int x1 = Math.Min(x0 + ps, width);

                    long r = 0, g = 0, b = 0, a = 0;
                    int count = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * width * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            r += src[i];
                            g += src[i + 1];
                            b += src[i + 2];
                            a += src[i + 3];
                            count++;
                        }
                    }

                    float avgR = r / (float)count;
                    float avgG = g / (float)count;
                    float avgB = b / (float)count;

                    // Bayer 阈值偏移：归一化到 [-0.5, 0.5) 后乘以强度
                    float threshold = (Bayer4x4[oy % bayerSize, ox % bayerSize] + 0.5f) / (bayerSize * bayerSize) - 0.5f;
                    float offset = threshold * BayerStrength;

                    // 应用偏移并钳制到 [0, 255]
                    byte pr = (byte)Math.Clamp(Math.Round(avgR + offset), 0, 255);
                    byte pg = (byte)Math.Clamp(Math.Round(avgG + offset), 0, 255);
                    byte pb = (byte)Math.Clamp(Math.Round(avgB + offset), 0, 255);

                    int best = NearestPaletteIndex(pr, pg, pb, palette);

                    int di = (oy * outW + ox) * 4;
                    dst[di] = palette[best * 3];
                    dst[di + 1] = palette[best * 3 + 1];
                    dst[di + 2] = palette[best * 3 + 2];
                    dst[di + 3] = (byte)(a / count);
                }
            }
        }

        /// <summary>
        /// 卡通模式：块内取众数（出现最多的颜色）。
        /// 若有调色板：统计块内簇 ID 众数，输出对应调色板色。
        /// 若无调色板：统计块内 5 位量化色众数，输出该色的平均原始色。
        /// 边缘锐利，适合卡通/插画。
        /// </summary>
        private static void PixelateCartoon(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH,
            byte[]? palette, int[]? assignments, byte[] dst)
        {
            if (palette != null && assignments != null)
            {
                CartoonWithPalette(src, width, height, ps, outW, outH, palette, assignments, dst);
            }
            else if (palette != null)
            {
                CartoonWithBeadPalette(src, width, height, ps, outW, outH, palette, dst);
            }
            else
            {
                CartoonNoPalette(src, width, height, ps, outW, outH, dst);
            }
        }

        /// <summary>有调色板：块内簇 ID 众数。</summary>
        private static void CartoonWithPalette(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH,
            byte[] palette, int[] assignments, byte[] dst)
        {
            int palCount = palette.Length / 3;
            int[] blockCounts = new int[palCount];

            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = oy * ps;
                int y1 = Math.Min(y0 + ps, height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = ox * ps;
                    int x1 = Math.Min(x0 + ps, width);

                    Array.Clear(blockCounts, 0, palCount);
                    long aSum = 0;
                    int aCount = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int rowBase = y * width;
                        for (int x = x0; x < x1; x++)
                        {
                            int p = rowBase + x;
                            blockCounts[assignments[p]]++;
                            aSum += src[p * 4 + 3];
                            aCount++;
                        }
                    }

                    int bestCluster = 0;
                    int bestCount = -1;
                    for (int c = 0; c < palCount; c++)
                    {
                        if (blockCounts[c] > bestCount)
                        {
                            bestCount = blockCounts[c];
                            bestCluster = c;
                        }
                    }

                    int di = (oy * outW + ox) * 4;
                    dst[di] = palette[bestCluster * 3];
                    dst[di + 1] = palette[bestCluster * 3 + 1];
                    dst[di + 2] = palette[bestCluster * 3 + 2];
                    dst[di + 3] = (byte)(aSum / aCount);
                }
            }
        }

        /// <summary>
        /// 品牌色卡（palette 非 null、assignments 为 null）：块内品牌色号众数。
        /// 先一次性构建 32768 项 5 位量化 bin → 品牌色索引查找表，再每块统计品牌色号频率取众数。
        /// </summary>
        private static void CartoonWithBeadPalette(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH,
            byte[] palette, byte[] dst)
        {
            int palCount = palette.Length / 3;

            // 1. 构建 bin → 品牌色索引查找表（一次性，O(32768 × palCount)）
            int[] binLookup = new int[32 * 32 * 32];
            for (int bin = 0; bin < binLookup.Length; bin++)
            {
                // 由 bin 索引还原中心代表色：高 5 位 + 4（bin 中心）
                byte r = (byte)(((bin >> 10) & 0x1F) << 3 | 0x04);
                byte g = (byte)(((bin >> 5) & 0x1F) << 3 | 0x04);
                byte b = (byte)((bin & 0x1F) << 3 | 0x04);
                binLookup[bin] = BeadPalettes.NearestIndex(r, g, b, palette);
            }

            int[] blockCounts = new int[palCount];

            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = oy * ps;
                int y1 = Math.Min(y0 + ps, height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = ox * ps;
                    int x1 = Math.Min(x0 + ps, width);

                    Array.Clear(blockCounts, 0, palCount);
                    long aSum = 0;
                    int aCount = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * width * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            byte r = src[i], g = src[i + 1], b = src[i + 2];
                            int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                            blockCounts[binLookup[bin]]++;
                            aSum += src[i + 3];
                            aCount++;
                        }
                    }

                    int bestCluster = 0;
                    int bestCount = -1;
                    for (int c = 0; c < palCount; c++)
                    {
                        if (blockCounts[c] > bestCount)
                        {
                            bestCount = blockCounts[c];
                            bestCluster = c;
                        }
                    }

                    int di = (oy * outW + ox) * 4;
                    dst[di] = palette[bestCluster * 3];
                    dst[di + 1] = palette[bestCluster * 3 + 1];
                    dst[di + 2] = palette[bestCluster * 3 + 2];
                    dst[di + 3] = (byte)(aSum / aCount);
                }
            }
        }

        /// <summary>无调色板：块内 5 位量化色众数，输出该 bin 的平均原始色。</summary>
        private static void CartoonNoPalette(
            ReadOnlySpan<byte> src, int width, int height, int ps, int outW, int outH, byte[] dst)
        {
            const int BinCount = 32 * 32 * 32;
            int[] hist = new int[BinCount];
            long[] sumR = new long[BinCount];
            long[] sumG = new long[BinCount];
            long[] sumB = new long[BinCount];

            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = oy * ps;
                int y1 = Math.Min(y0 + ps, height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = ox * ps;
                    int x1 = Math.Min(x0 + ps, width);

                    // 统计块内 5 位量化色频率
                    long aSum = 0;
                    int aCount = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * width * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x * 4;
                            byte r = src[i], g = src[i + 1], b = src[i + 2];
                            int bin = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                            hist[bin]++;
                            sumR[bin] += r;
                            sumG[bin] += g;
                            sumB[bin] += b;
                            aSum += src[i + 3];
                            aCount++;
                        }
                    }

                    // 找频率最高的 bin
                    int bestBin = 0;
                    int bestCount = -1;
                    for (int bi = 0; bi < BinCount; bi++)
                    {
                        if (hist[bi] > bestCount)
                        {
                            bestCount = hist[bi];
                            bestBin = bi;
                        }
                    }

                    int di = (oy * outW + ox) * 4;
                    if (bestCount > 0)
                    {
                        dst[di] = (byte)(sumR[bestBin] / bestCount);
                        dst[di + 1] = (byte)(sumG[bestBin] / bestCount);
                        dst[di + 2] = (byte)(sumB[bestBin] / bestCount);
                    }
                    dst[di + 3] = (byte)(aSum / aCount);

                    // 清零用过的 bin 供下块复用
                    ClearUsedBins(hist, sumR, sumG, sumB, y0, y1, x0, x1, width, src);
                }
            }
        }

        /// <summary>清零上一块用到的 bin，避免每块全量清零 32768 项。</summary>
        private static void ClearUsedBins(
            int[] hist, long[] sumR, long[] sumG, long[] sumB,
            int y0, int y1, int x0, int x1, int width, ReadOnlySpan<byte> src)
        {
            for (int y = y0; y < y1; y++)
            {
                int row = y * width * 4;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x * 4;
                    int bin = ((src[i] >> 3) << 10) | ((src[i + 1] >> 3) << 5) | (src[i + 2] >> 3);
                    hist[bin] = 0;
                    sumR[bin] = 0;
                    sumG[bin] = 0;
                    sumB[bin] = 0;
                }
            }
        }

        /// <summary>在调色板中找加权距离最近的色索引。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NearestPaletteIndex(byte r, byte g, byte b, byte[] palette)
        {
            int palCount = palette.Length / 3;
            int best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < palCount; i++)
            {
                double dr = r - palette[i * 3];
                double dg = g - palette[i * 3 + 1];
                double db = b - palette[i * 3 + 2];
                double d = Wr * dr * dr + Wg * dg * dg + Wb * db * db;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
