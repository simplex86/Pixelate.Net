using System.Runtime.CompilerServices;

namespace Pixelate.Net;


/// <summary>
/// 基于颜色直方图的贪心合并量化器。
/// </summary>
/// <remarks>
/// 算法：
/// 1. 将 RGB 量化到 5 位/通道（32³ = 32768 bin）建直方图，统计频率与原始颜色累加。
/// 2. 按频率降序排列非空 bin。
/// 3. 贪心合并：高频 bin 先入调色板；低频 bin 若与已有调色板色加权距离平方 ≤ 阈值则合并，否则新建。
/// 4. 全像素通过 5 位量化索引映射到调色板。
///
/// 阈值含义：加权 RGB 距离平方（BT.601 权重）。
/// 阈值 30 ≈ 各通道差约 30，适合彩虹→7 色。
/// </remarks>
internal static class ColorMerger
{
    // 5 位/通道 = 32 级
    private const int Bits = 5;
    private const int Levels = 32;          // 1 << Bits
    private const int Shift = 3;            // 8 - Bits
    private const int Rm = 0xF8;            // 取高 5 位的掩码
    private const int BinCount = Levels * Levels * Levels; // 32768

    // 人眼各通道敏感度（BT.601）
    private const double Wr = 0.299, Wg = 0.587, Wb = 0.114;

    /// <summary>
    /// 对全图像素做基于阈值的颜色合并。
    /// </summary>
    /// <param name="rgba">RGBA 字节缓冲（每像素 4 字节）。</param>
    /// <param name="pixelCount">像素数。</param>
    /// <param name="threshold">合并阈值 [1, 100]，映射为距离平方阈值 = threshold²。</param>
    /// <returns>调色板（每色 3 字节 RGB）+ 每像素的调色板索引。</returns>
    public static (byte[] PaletteRgb, int[] Assignments) Merge(
        ReadOnlySpan<byte> rgba, int pixelCount, int threshold)
    {
        double distSqThreshold = (double)threshold * threshold;

        // 1. 统计直方图 + RGB 累加
        int[] hist = new int[BinCount];
        long[] sumR = new long[BinCount];
        long[] sumG = new long[BinCount];
        long[] sumB = new long[BinCount];

        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            int bin = BinIndex(r, g, b);
            hist[bin]++;
            sumR[bin] += r;
            sumG[bin] += g;
            sumB[bin] += b;
        }

        // 2. 收集非空 bin，按频率降序排列
        int nonEmpty = 0;
        for (int i = 0; i < BinCount; i++)
            if (hist[i] > 0) nonEmpty++;

        int[] bins = new int[nonEmpty];
        int bi = 0;
        for (int i = 0; i < BinCount; i++)
            if (hist[i] > 0) bins[bi++] = i;

        // 按频率降序：高频 bin 先入调色板
        Array.Sort(bins, (a, b2) => hist[b2].CompareTo(hist[a]));

        // 3. 贪心合并
        // 调色板用列表存储（RGB 交替）
        var palR = new List<byte>(64);
        var palG = new List<byte>(64);
        var palB = new List<byte>(64);
        int[] binToPal = new int[BinCount];

        for (int idx = 0; idx < bins.Length; idx++)
        {
            int bin = bins[idx];
            int count = hist[bin];
            byte r = (byte)(sumR[bin] / count);
            byte g = (byte)(sumG[bin] / count);
            byte b = (byte)(sumB[bin] / count);

            // 找最近已有调色板色
            int best = -1;
            double bestDist = double.MaxValue;
            for (int pi = 0; pi < palR.Count; pi++)
            {
                double d = WeightedDistSq(r, g, b, palR[pi], palG[pi], palB[pi]);
                if (d < bestDist) { bestDist = d; best = pi; }
            }

            if (best >= 0 && bestDist <= distSqThreshold)
            {
                binToPal[bin] = best;
            }
            else
            {
                binToPal[bin] = palR.Count;
                palR.Add(r);
                palG.Add(g);
                palB.Add(b);
            }
        }

        // 4. 全像素映射
        int[] assignments = new int[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            int bin = BinIndex(rgba[i], rgba[i + 1], rgba[i + 2]);
            assignments[p] = binToPal[bin];
        }

        // 5. 输出调色板
        byte[] palette = new byte[palR.Count * 3];
        for (int i = 0; i < palR.Count; i++)
        {
            palette[i * 3] = palR[i];
            palette[i * 3 + 1] = palG[i];
            palette[i * 3 + 2] = palB[i];
        }

        return (palette, assignments);
    }

    /// <summary>
    /// 根据图像颜色分布自动计算合并阈值。
    /// 算法：统计 5 位直方图非空 bin 间的最近邻加权距离，取 75 百分位作为阈值。
    /// 颜色分布越分散，阈值越大（合并越激进）；颜色集中则阈值小。
    /// </summary>
    public static int ComputeAutoThreshold(ReadOnlySpan<byte> rgba, int pixelCount)
    {
        // 1. 建直方图
        int[] hist = new int[BinCount];
        long[] sumR = new long[BinCount], sumG = new long[BinCount], sumB = new long[BinCount];

        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            byte r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            int bin = BinIndex(r, g, b);
            hist[bin]++;
            sumR[bin] += r;
            sumG[bin] += g;
            sumB[bin] += b;
        }

        // 2. 收集非空 bin 的平均色
        var binColors = new List<(byte r, byte g, byte b)>(256);
        for (int bin = 0; bin < BinCount; bin++)
        {
            if (hist[bin] > 0)
            {
                int c = hist[bin];
                binColors.Add(((byte)(sumR[bin] / c), (byte)(sumG[bin] / c), (byte)(sumB[bin] / c)));
            }
        }

        int n = binColors.Count;
        if (n <= 1) return 0;

        // 3. 采样以控制计算量：最多取 500 个 bin 做最近邻查询
        int sampleN = Math.Min(n, 500);
        int step = Math.Max(1, n / sampleN);

        var nearestDists = new List<double>(sampleN);
        for (int si = 0; si < n; si += step)
        {
            byte r1 = binColors[si].r, g1 = binColors[si].g, b1 = binColors[si].b;
            double nearest = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (j == si) continue;
                double d = WeightedDistSq(r1, g1, b1, binColors[j].r, binColors[j].g, binColors[j].b);
                if (d < nearest) nearest = d;
            }
            if (nearest < double.MaxValue) nearestDists.Add(nearest);
        }

        if (nearestDists.Count == 0) return 0;

        // 4. 取 75 百分位
        nearestDists.Sort();
        int p75idx = (int)(nearestDists.Count * 0.75);
        if (p75idx >= nearestDists.Count) p75idx = nearestDists.Count - 1;
        double distSq = nearestDists[p75idx];

        // 5. 转为线性阈值（threshold² 与 WeightedDistSq 比较）
        int threshold = (int)Math.Ceiling(Math.Sqrt(distSq));
        // 钳制到合理范围
        if (threshold < 1) threshold = 1;
        if (threshold > 100) threshold = 100;
        return threshold;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BinIndex(byte r, byte g, byte b)
    {
        // 高 5 位：r>>3 ∈ [0,31]，组合为 15 位索引
        return ((r >> Shift) << 10) | ((g >> Shift) << 5) | (b >> Shift);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double WeightedDistSq(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return Wr * dr * dr + Wg * dg * dg + Wb * db * db;
    }
}
