namespace TaoMaster.Core.Models;

public sealed record ResolvedExecutableCandidate(
    string ExecutableName,
    string CandidatePath,
    string OriginalPathSegment,
    string ExpandedPathSegment,
    EnvironmentPathScope Scope,
    int SegmentIndex);
