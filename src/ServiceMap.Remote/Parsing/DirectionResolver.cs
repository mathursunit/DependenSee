using ServiceMap.Core.Models;
using ServiceMap.Core.Net;

namespace ServiceMap.Remote.Parsing;

/// <summary>
/// Classifies each sampled socket as Listen / Inbound / Outbound using the
/// shared <see cref="DirectionClassifier"/> rule, so remote scans and the local
/// sampler agree. Remote scans are single-shot, so the listen set comes from
/// the batch itself.
/// </summary>
public static class DirectionResolver
{
    public static void Assign(IEnumerable<ConnectionSample> samples) =>
        DirectionClassifier.AssignBatch(samples);
}
