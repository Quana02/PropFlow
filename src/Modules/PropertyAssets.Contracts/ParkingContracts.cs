namespace PropFlow.Modules.PropertyAssets.Contracts;

// User-approved FE-04 scope extension: parking diagrams and manual space management, without vehicles.
// Optional clockwise convex outline in local 0..1 coordinates. Old rectangular drafts remain readable.
public sealed record ParkingPoint(double X, double Y);
public sealed record ParkingShape(Guid Id, string Kind, string Label, double X, double Y, double Width, double Height, double Rotation = 0, ParkingPoint[]? Outline = null, ParkingPoint[][]? Contours = null, bool NeedsReview = false, ParkingPoint[]? ControlPoints = null);
public sealed record ParkingScale(ParkingPoint Start, ParkingPoint End, double Length, string Unit, bool Confirmed = true);
public sealed record ParkingDigitizationInfo(int SourceWidth, int SourceHeight, int Threshold, int Candidates, double RetainedInkRatio);
public sealed record ParkingGeometryIssue(string Code, string Message, Guid[] ShapeIds);
// Measurements describe the recognition snapshot, never an accuracy percentage or approval.
public sealed record ParkingRecognitionReview(int CheckedObjects, int SourceCheckedObjects, int RefinedObjects, int UnreadRegions, ParkingGeometryIssue[] Issues, bool Stale = false);
public sealed record ParkingDrawing(int Width, int Height, ParkingShape[] Shapes, ParkingScale? Scale = null, ParkingDigitizationInfo? Digitization = null, bool InfiniteCanvas = false, ParkingRecognitionReview? RecognitionReview = null)
{
    // Manual diagrams start in centimetres: 100 coordinate steps represent one metre.
    public static ParkingDrawing Empty => new(1600, 1000, [], new(new(0,0),new(100,0),1,"m"), InfiniteCanvas:true);
}
public sealed record ParkingSourceView(Guid Id, string FileName, string ContentType, int Page);
public sealed record ParkingSpaceView(Guid Id, string Status);
public sealed record ParkingActionView(Guid Id, string Action, Guid ActorId, DateTimeOffset At, Guid? SpaceId, string Note);
public sealed record ParkingAreaSummary(Guid Id, string Name, bool Published, int Total, int Available, int InUse, int Maintenance);
public sealed record ParkingAreaView(Guid Id, string Name, Guid Version, ParkingDrawing Draft, ParkingDrawing? Published,
    ParkingSourceView? DraftSource, ParkingSourceView? PublishedSource, ParkingSpaceView[] Spaces, DateTimeOffset? PublishedAt);
public sealed record CreateParkingArea(string Name);
public sealed record SaveParkingDrawing(Guid ExpectedVersion, string Name, ParkingDrawing Drawing);
public sealed record PublishParkingDrawing(Guid ExpectedVersion);
public sealed record RecognizeParkingDrawing(Guid ExpectedVersion);
public sealed record ChangeParkingSpace(Guid ExpectedVersion, string Status, string Note);
public sealed record ParkingImportResult(ParkingAreaView Area, bool Recognized, string Message);
