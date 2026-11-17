using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;

public sealed class UtilityConsumptionAnalyzer(IUtilitySpikeModel model, UtilityInsightOptions options)
{
    public const string Version = "utility-daily-iid-mad-v1";
    public string Signature => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Version, options.MinimumHistory, options.Confidence,
            options.MediumDeviation, options.HighDeviation, options.MinimumRelativeChange, options.MinimumElectricityChange, options.MinimumWaterChange })))[..16];
    public UtilityAnalysis Analyze(UtilityConsumptionWindow window)
    {
        var current = window.Points.SingleOrDefault(p => p.Month == window.Month);
        // Every recipient of this period must also be entitled to each reference period.
        // Newly addressed households do not inherit the previous household's history.
        var visible = window.Points.Where(p => p.Month <= window.Month &&
            (current is not { IsFinal: true } || current.Recipients.All(p.Recipients.Contains))).OrderBy(p => p.Month).ToArray();
        return new(Signal("ELECTRICITY", "kWh", true), Signal("WATER", "m³", false),
            visible.Select(x => new UtilityHistoryPoint(x.Month, x.Electricity, x.Water, x.ElectricityComparable, x.WaterComparable)).ToArray());

        UtilitySignal Signal(string utility, string unit, bool electricity)
        {
            decimal? Amount(UtilityConsumptionPoint p) => electricity ? p.Electricity : p.Water;
            bool Comparable(UtilityConsumptionPoint p) => electricity ? p.ElectricityComparable : p.WaterComparable;
            var prior = visible.Where(p => p.Month < window.Month && Amount(p).HasValue && Comparable(p)).TakeLast(24).ToArray();
            var amount = current is null ? null : Amount(current);
            UtilitySignal Missing(string reason) => new(utility, unit, "INSUFFICIENT", "UNKNOWN", amount, null, null, null, null, prior.Length, Version, reason);
            if (amount is null) return Missing("Chưa có lượng tiêu thụ cho kỳ đã chọn.");
            if (!Comparable(current!)) return Missing("Chỉ số chưa nối liên tục từ kỳ liền trước; cần kiểm tra thời gian ghi công tơ.");
            if (prior.Length < options.MinimumHistory) return Missing($"Cần ít nhất {options.MinimumHistory} kỳ tham chiếu hợp lệ; hiện có {prior.Length} kỳ. Kỳ thiếu không được tính là tiêu thụ bằng không.");
            var daily = prior.Select(p => Amount(p)!.Value / DateTime.DaysInMonth(p.Month.Year, p.Month.Month)).ToArray();
            var median = Median(daily);
            var days = DateTime.DaysInMonth(window.Month.Year, window.Month.Month);
            var reference = median * days;
            var actualDaily = amount.Value / days;
            var mad = Median(daily.Select(x => Math.Abs(x - median)).ToArray());
            var scale = Math.Max(1.4826m * mad, Math.Max(median * .1m, electricity ? .1m : .01m));
            var deviation = Math.Abs(actualDaily - median) / scale;
            var change = reference == 0 ? (decimal?)null : (amount - reference) / reference * 100;
            var method = Version;
            (bool Spike, double PValue)? prediction = null;
            try { prediction = model.Evaluate(daily.Select(x => (float)x).ToArray(), (float)actualDaily, options.Confidence); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DllNotFoundException or TypeInitializationException)
            { method = "utility-daily-mad-v1-fallback"; }
            var meaningful = Math.Abs(amount.Value - reference) >= Math.Max(reference * options.MinimumRelativeChange,
                electricity ? options.MinimumElectricityChange : options.MinimumWaterChange);
            var abnormal = meaningful && (deviation >= options.MediumDeviation || prediction is { Spike: true } && deviation >= 2);
            var direction = amount > reference ? "HIGHER" : amount < reference ? "LOWER" : "STABLE";
            var severity = !abnormal ? "NORMAL" : direction == "HIGHER" && deviation >= options.HighDeviation &&
                (reference == 0 || amount >= reference * 1.5m) ? "HIGH" : "WATCH";
            var reason = severity == "NORMAL" ? "Mức sử dụng nằm trong biến động thông thường của dữ liệu tham chiếu."
                : direction == "LOWER" ? "Tiêu thụ giảm đáng kể. Cần xác minh thời gian có người ở và độ chính xác của chỉ số."
                : "Tiêu thụ tăng đáng kể. Cần xác minh chỉ số, thay đổi sinh hoạt và tình trạng thiết bị; đây chưa phải kết luận có sự cố.";
            return new(utility, unit, severity, direction, amount, decimal.Round(reference, 3), change is null ? null : decimal.Round(change.Value, 1),
                decimal.Round(deviation, 2), prediction is { } p && double.IsFinite(p.PValue) ? p.PValue : null,
                prior.Length, method, reason + " Đã quy đổi theo số ngày trong tháng; chưa điều chỉnh mùa vụ hoặc số người ở.");
        }
    }
    private static decimal Median(decimal[] values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
