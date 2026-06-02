namespace AvifEncoder
{
    /// <summary>
    /// Journal 事件类型与 Schema 版本常量。
    /// Journal 是系统的唯一权威状态源（Event Sourcing）。
    /// CSV 仅作为最终导出文件，不参与状态恢复。
    /// </summary>
    public static class JournalEventTypes
    {
        /// <summary>当前 Journal Schema 版本。格式变更时递增，Resume 时根据版本选择解析器。</summary>
        public const int CurrentSchemaVersion = 2;

        // ── 事件类型 ──

        /// <summary>编码成功且全部指标就绪，文件处理完毕。</summary>
        public const string Success = "success";

        /// <summary>高级指标（SSIMULACRA2/Butteraugli/GMSD/XPSNR）计算完成。</summary>
        public const string Metrics = "metrics";

        /// <summary>处理开始标记（用于断点续传的中间状态判断）。</summary>
        public const string Start = "start";
    }
}
