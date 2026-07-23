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

        /// <summary>
        /// 根据分割数与分割方向计算输出尺寸。
        /// Horizontal：像素尺寸由宽度和分割数计算；Vertical：由高度和分割数计算。
        /// 使用浮点像素尺寸，主方向输出恰好为 splits 个像素。
        /// </summary>
        public static (int Width, int Height) GetOutputSize(int width, int height, int splits, SplitDirection direction)
        {
            if (splits < 1) throw new ArgumentOutOfRangeException(nameof(splits));
            var dir = direction == SplitDirection.Auto ? SplitDirection.Horizontal : direction;
            double ps = dir == SplitDirection.Vertical
                ? Math.Max(1.0, (double)height / splits)
                : Math.Max(1.0, (double)width / splits);
            int outW = (int)Math.Ceiling(width / ps);
            int outH = (int)Math.Ceiling(height / ps);
            return (outW, outH);
        }

        /// <summary>
        /// 根据分割数、分割方向与最大边长约束计算输出尺寸。
        /// maxOutputDimension &gt; 0 时，自动增大像素块尺寸以保证宽高均不超过该值。
        /// 使用浮点像素尺寸，在满足约束前提下主方向尽可能接近 splits 个像素。
        /// </summary>
        public static (int Width, int Height) GetOutputSize(int width, int height, int splits, SplitDirection direction, int maxOutputDimension)
        {
            if (splits < 1) throw new ArgumentOutOfRangeException(nameof(splits));
            var dir = direction == SplitDirection.Auto ? SplitDirection.Horizontal : direction;
            double ps = dir == SplitDirection.Vertical
                ? Math.Max(1.0, (double)height / splits)
                : Math.Max(1.0, (double)width / splits);
            ps = ApplyMaxOutputDimension(width, height, ps, maxOutputDimension);
            int outW = (int)Math.Ceiling(width / ps);
            int outH = (int)Math.Ceiling(height / ps);
            return (outW, outH);
        }

        /// <summary>
        /// 若设置了最大输出边长，确保 ps 足够大以使输出宽高均不超过此值。
        /// 在满足约束的前提下尽可能使用较小的 ps（较高分辨率）。
        /// </summary>
        private static double ApplyMaxOutputDimension(int width, int height, double ps, int maxOutputDimension)
        {
            if (maxOutputDimension <= 0) return ps;
            double minPsW = Math.Max(1.0, (double)width / maxOutputDimension);
            double minPsH = Math.Max(1.0, (double)height / maxOutputDimension);
            double minPs = Math.Max(minPsW, minPsH);
            return Math.Max(ps, minPs);
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
            if (options.Splits < 1) throw new ArgumentOutOfRangeException(nameof(options), "Splits 必须 >= 1");
            int expected = width * height * 4;
            if (sourceRgba.Length < expected)
                throw new ArgumentException("源缓冲区过小，与指定的宽高不匹配。", nameof(sourceRgba));

            // 像素块大小由分割数和分割方向推导；Auto 兜底为 Horizontal。
            // 使用浮点 ps 使主方向输出恰好为 splits 个像素（非均匀块大小）。
            var dir = options.SplitDirection == SplitDirection.Auto ? SplitDirection.Horizontal : options.SplitDirection;
            double ps = dir == SplitDirection.Vertical
                ? Math.Max(1.0, (double)height / options.Splits)
                : Math.Max(1.0, (double)width / options.Splits);
            // 若设置了最大输出边长，确保 ps 足够大以使输出宽高均不超过此值。
            ps = ApplyMaxOutputDimension(width, height, ps, options.MaxOutputDimension);
            int outW = (int)Math.Ceiling(width / ps);
            int outH = (int)Math.Ceiling(height / ps);
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
            int alphaThreshold = options.AlphaThreshold;
            if (options.Mode == ProcessMode.Cartoon)
                PixelateCartoon(sourceRgba, width, height, ps, outW, outH, palette, assignments, output, alphaThreshold);
            else
                PixelateRealistic(sourceRgba, width, height, ps, outW, outH, palette, output, alphaThreshold);

            return output;
        }

        /// <summary>
        /// 真实模式：块内平均 RGB。若有调色板，平均后映射到最近调色板色。
        /// 过渡柔和，适合照片。
        /// </summary>
        private static void PixelateRealistic(
            ReadOnlySpan<byte> src, int width, int height, double ps, int outW, int outH,
            byte[]? palette, byte[] dst, int alphaThreshold)
        {
            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = (int)(oy * ps);
                int y1 = Math.Min((int)((oy + 1) * ps), height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = (int)(ox * ps);
                    int x1 = Math.Min((int)((ox + 1) * ps), width);

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
                    dst[di + 3] = BinaryAlpha(a, count, alphaThreshold);
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
            ReadOnlySpan<byte> src, int width, int height, double ps, int outW, int outH,
            byte[]? palette, int[]? assignments, byte[] dst, int alphaThreshold)
        {
            if (palette != null && assignments != null)
            {
                CartoonWithPalette(src, width, height, ps, outW, outH, palette, assignments, dst, alphaThreshold);
            }
            else if (palette != null)
            {
                CartoonWithBeadPalette(src, width, height, ps, outW, outH, palette, dst, alphaThreshold);
            }
            else
            {
                CartoonNoPalette(src, width, height, ps, outW, outH, dst, alphaThreshold);
            }
        }

        /// <summary>有调色板：块内簇 ID 众数。</summary>
        private static void CartoonWithPalette(
            ReadOnlySpan<byte> src, int width, int height, double ps, int outW, int outH,
            byte[] palette, int[] assignments, byte[] dst, int alphaThreshold)
        {
            int palCount = palette.Length / 3;
            int[] blockCounts = new int[palCount];

            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = (int)(oy * ps);
                int y1 = Math.Min((int)((oy + 1) * ps), height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = (int)(ox * ps);
                    int x1 = Math.Min((int)((ox + 1) * ps), width);

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
                    dst[di + 3] = BinaryAlpha(aSum, aCount, alphaThreshold);
                }
            }
        }

        /// <summary>
        /// 品牌色卡（palette 非 null、assignments 为 null）：块内品牌色号众数。
        /// 先一次性构建 32768 项 5 位量化 bin → 品牌色索引查找表，再每块统计品牌色号频率取众数。
        /// </summary>
        private static void CartoonWithBeadPalette(
            ReadOnlySpan<byte> src, int width, int height, double ps, int outW, int outH,
            byte[] palette, byte[] dst, int alphaThreshold)
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
                int y0 = (int)(oy * ps);
                int y1 = Math.Min((int)((oy + 1) * ps), height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = (int)(ox * ps);
                    int x1 = Math.Min((int)((ox + 1) * ps), width);

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
                    dst[di + 3] = BinaryAlpha(aSum, aCount, alphaThreshold);
                }
            }
        }

        /// <summary>无调色板：块内 5 位量化色众数，输出该 bin 的平均原始色。</summary>
        private static void CartoonNoPalette(
            ReadOnlySpan<byte> src, int width, int height, double ps, int outW, int outH, byte[] dst, int alphaThreshold)
        {
            const int BinCount = 32 * 32 * 32;
            int[] hist = new int[BinCount];
            long[] sumR = new long[BinCount];
            long[] sumG = new long[BinCount];
            long[] sumB = new long[BinCount];

            for (int oy = 0; oy < outH; oy++)
            {
                int y0 = (int)(oy * ps);
                int y1 = Math.Min((int)((oy + 1) * ps), height);
                for (int ox = 0; ox < outW; ox++)
                {
                    int x0 = (int)(ox * ps);
                    int x1 = Math.Min((int)((ox + 1) * ps), width);

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
                    dst[di + 3] = BinaryAlpha(aSum, aCount, alphaThreshold);

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

        /// <summary>
        /// 像素画 Alpha 二值化：块内平均 alpha &lt; 阈值 → 0（透明），否则 → 255（不透明）。
        /// count 为 0 时视为透明。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte BinaryAlpha(long sum, int count, int threshold)
        {
            if (count <= 0) return 0;
            return (sum / count) < threshold ? (byte)0 : (byte)255;
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

        /// <summary>
        /// 剔除像素画背景中的孤立噪点。
        /// 噪点定义：与背景色不同、相邻（8 连通）且总数不超过 <paramref name="maxNoiseSize"/> 个像素的连通块，
        /// 且其周围非透明像素均为背景色（即孤立出现在背景中）。
        /// 替换策略：用噪点周围背景像素的平均 RGB 颜色替换噪点像素。
        /// </summary>
        /// <param name="rgba">像素画 RGBA 数据（每像素 4 字节，行优先）。</param>
        /// <param name="width">像素画宽度。</param>
        /// <param name="height">像素画高度。</param>
        /// <param name="maxNoiseSize">噪点连通块的最大像素数（默认 3）。</param>
        /// <returns>剔除噪点后的 RGBA 数据（新数组）。</returns>
        public static byte[] RemoveNoise(ReadOnlySpan<byte> rgba, int width, int height, int maxNoiseSize = 3)
        {
            if (width <= 0 || height <= 0) return rgba.ToArray();
            int pixelCount = width * height;
            byte[] output = rgba.ToArray();

            // 1. 检测背景色：统计非透明像素中最常见的颜色。
            (byte bgR, byte bgG, byte bgB) = DetectBackgroundColor(rgba, pixelCount);

            // 2. 标记每个像素：是否为背景色（非透明且颜色 == 背景色）。
            bool[] isBackground = new bool[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                if (rgba[idx + 3] == 0) continue; // 透明像素不算背景
                if (rgba[idx] == bgR && rgba[idx + 1] == bgG && rgba[idx + 2] == bgB)
                    isBackground[i] = true;
            }

            // 3. 连通域分析：找出所有非背景、非透明像素的 8 连通块。
            bool[] visited = new bool[pixelCount];
            // 8 邻域偏移（dx, dy）
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            for (int start = 0; start < pixelCount; start++)
            {
                if (visited[start] || isBackground[start]) continue;
                if (rgba[start * 4 + 3] == 0) { visited[start] = true; continue; }

                // BFS 收集连通块
                var component = new List<int>(16) { start };
                visited[start] = true;
                int head = 0;
                while (head < component.Count)
                {
                    int p = component[head++];
                    int px = p % width;
                    int py = p / width;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = px + dx[k];
                        int ny = py + dy[k];
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int np = ny * width + nx;
                        if (visited[np] || isBackground[np]) continue;
                        if (rgba[np * 4 + 3] == 0) { visited[np] = true; continue; }
                        visited[np] = true;
                        component.Add(np);
                    }
                }

                // 4. 仅处理小连通块（噪点候选）。
                if (component.Count > maxNoiseSize) continue;

                // 5. 孤立性判定：收集连通块周围的非透明像素，检查是否全为背景色。
                //    透明像素不计入判定（既不算背景也不算非背景）。
                var surroundSet = new HashSet<int>();
                bool isIsolated = true;
                foreach (int p in component)
                {
                    int px = p % width;
                    int py = p / width;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = px + dx[k];
                        int ny = py + dy[k];
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int np = ny * width + nx;
                        if (component.Contains(np)) continue;
                        if (rgba[np * 4 + 3] == 0) continue; // 透明像素跳过
                        if (!isBackground[np])
                        {
                            // 周围存在非背景、非透明像素 → 不是孤立噪点
                            isIsolated = false;
                            break;
                        }
                        surroundSet.Add(np);
                    }
                    if (!isIsolated) break;
                }

                // 不孤立，或周围没有可用背景像素（如全被透明包围），跳过。
                if (!isIsolated || surroundSet.Count == 0) continue;

                // 6. 计算周围背景像素的平均 RGB，替换噪点。
                long sumR = 0, sumG = 0, sumB = 0;
                foreach (int np in surroundSet)
                {
                    sumR += rgba[np * 4];
                    sumG += rgba[np * 4 + 1];
                    sumB += rgba[np * 4 + 2];
                }
                byte avgR = (byte)(sumR / surroundSet.Count);
                byte avgG = (byte)(sumG / surroundSet.Count);
                byte avgB = (byte)(sumB / surroundSet.Count);

                foreach (int p in component)
                {
                    int idx = p * 4;
                    output[idx] = avgR;
                    output[idx + 1] = avgG;
                    output[idx + 2] = avgB;
                    // alpha 保持不变（255）
                }
            }

            return output;
        }

        /// <summary>
        /// 检测背景色：统计非透明像素中最常见的 RGB 颜色。
        /// 若全透明则返回黑色作为兜底。
        /// </summary>
        private static (byte r, byte g, byte b) DetectBackgroundColor(ReadOnlySpan<byte> rgba, int pixelCount)
        {
            var counts = new Dictionary<uint, int>();
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * 4;
                if (rgba[idx + 3] == 0) continue; // 跳过透明像素
                uint key = ((uint)rgba[idx] << 16) | ((uint)rgba[idx + 1] << 8) | rgba[idx + 2];
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            if (counts.Count == 0) return (0, 0, 0);

            uint bestKey = 0;
            int bestCount = 0;
            foreach (var (key, count) in counts)
            {
                if (count > bestCount) { bestCount = count; bestKey = key; }
            }

            return ((byte)(bestKey >> 16), (byte)(bestKey >> 8), (byte)bestKey);
        }
    }
}
