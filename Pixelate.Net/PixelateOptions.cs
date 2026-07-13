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
    /// 像素化参数。
    /// </summary>
    public sealed class PixelateOptions
    {
        /// <summary>
        /// 横向分割数量（输出宽度的豆数）。纵向按原图比例自动计算。
        /// pixelSize = ceil(width / HorizontalSplits)。取值范围 [1, width]。
        /// </summary>
        public int HorizontalSplits { get; set; } = 50;

        /// <summary>
        /// 颜色合并阈值 [0, 100]。
        /// 0 = 不合并，直接块取色；
        /// 越大颜色越少（彩虹 → 7 色约需 30）。
        /// 前端负责自动阈值的计算和设置；算法层仅使用此值。
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
    }
}
