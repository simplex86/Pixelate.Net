namespace Pixelate.Net
{
    /// <summary>
    /// 分块取色模式。
    /// </summary>
    public enum ProcessMode
    {
        /// <summary>真实：块内像素平均 RGB，过渡柔和，适合照片。</summary>
        Realistic,

        /// <summary>卡通：块内出现最多的颜色（众数），边缘锐利，适合卡通/插画。</summary>
        Cartoon
    }

    /// <summary>
    /// 分割主方向。
    /// </summary>
    public enum SplitDirection
    {
        /// <summary>自动：由调用方根据图像宽高比与底板拼接模式解析为水平或竖直。</summary>
        Auto,

        /// <summary>水平：像素尺寸由图像宽度和分割数计算。</summary>
        Horizontal,

        /// <summary>竖直：像素尺寸由图像高度和分割数计算。</summary>
        Vertical
    }

    /// <summary>
    /// 像素化参数。
    /// </summary>
    public sealed class PixelateOptions
    {
        /// <summary>
        /// 分割数量（主方向上的像素数）。取值范围 [1, ∞)。
        /// </summary>
        public int Splits { get; set; } = 52;

        /// <summary>
        /// 分割主方向。Auto 由调用方解析为 Horizontal 或 Vertical；
        /// 算法层将 Auto 视为 Horizontal 兜底。
        /// </summary>
        public SplitDirection SplitDirection { get; set; } = SplitDirection.Auto;

        /// <summary>
        /// 颜色合并阈值 [0, 100]，由前端自动计算并设置（自由色模式下生效）。
        /// 内部映射为加权 RGB 距离平方阈值 = threshold²。
        /// </summary>
        public int ColorMergeThreshold { get; set; } = 30;

        /// <summary>分块取色模式。</summary>
        public ProcessMode Mode { get; set; } = ProcessMode.Realistic;

        /// <summary>
        /// 拼豆品牌色卡。None = 自由色（用 ColorMergeThreshold 任意聚类）；
        /// 其他值 = 把每个色块映射到该品牌官方色卡的最近色号，此时 ColorMergeThreshold 被忽略。
        /// </summary>
        public BeadBrand Brand { get; set; } = BeadBrand.None;

        /// <summary>
        /// 输出像素画的最大边长约束。0 表示不限制；&gt;0 时算法自动增大像素块尺寸，
        /// 保证输出宽度和高度均不超过此值（用于单板模式下确保像素画不超出底板）。
        /// 在此前提下尽可能保留较高的分辨率。
        /// </summary>
        public int MaxOutputDimension { get; set; } = 0;
    }
}
