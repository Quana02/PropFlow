using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;

public sealed class MlNetUtilitySpikeModel : IUtilitySpikeModel
{
    public (bool Spike, double PValue) Evaluate(IReadOnlyList<float> history, float current, double confidence)
    {
        var context = new MLContext(seed: 42);
        var data = context.Data.LoadFromEnumerable(history.Append(current).Select(x => new Input { Value = x }));
        var pipeline = context.Transforms.DetectIidSpike(nameof(Output.Scores), nameof(Input.Value), confidence, history.Count, AnomalySide.TwoSided);
        var fitted = pipeline.Fit(context.Data.LoadFromEnumerable(Array.Empty<Input>()));
        var last = context.Data.CreateEnumerable<Output>(fitted.Transform(data), reuseRowObject: false).Last();
        if (last.Scores.Length != 3 || !double.IsFinite(last.Scores[2])) throw new InvalidOperationException("Invalid anomaly prediction.");
        return (last.Scores[0] != 0, last.Scores[2]);
    }
    private sealed class Input { public float Value { get; set; } }
    private sealed class Output { [VectorType(3)] public double[] Scores { get; set; } = []; }
}
