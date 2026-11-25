using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static class ParkingSceneMerge
{
    public static ParkingDrawing Merge(ParkingGeometryResult source,ParkingDrawing? overview,IReadOnlyList<(ParkingSceneRegion Region,ParkingDrawing? Drawing)> details)
    {
        var frame=overview??source.Drawing;var inspected=ParkingSourceGeometry.Inspect(frame,source);
        var questionable=(inspected.RecognitionReview?.Issues??[]).SelectMany(i=>i.ShapeIds).ToHashSet();
        var protectedIds=frame.Shapes.Where(s=>s.Kind=="SPACE"&&!questionable.Contains(s.Id)).Select(s=>s.Id).ToHashSet();
        var result=frame.Shapes.ToList();var rejected=details.Count(d=>d.Drawing is null);var mergeIssues=new List<ParkingGeometryIssue>();
        foreach(var detail in details.Where(d=>d.Drawing is not null).OrderBy(d=>d.Region.Index))
        {
            var removed=result.Where(s=>detail.Region.RepairIds is {} repair?repair.Contains(s.Id):s.Kind=="SPACE"&&!protectedIds.Contains(s.Id)&&detail.Region.Owns(s,frame)).ToArray();
            var candidates=detail.Drawing!.Shapes.Where(s=>s.Kind!="SPACE"||detail.Region.Owns(s,frame)).ToArray();
            if(removed.Any(s=>s.Kind=="SPACE")&&!candidates.Any(s=>s.Kind=="SPACE")){rejected++;continue;}
            var proposal=result.Except(removed).ToList();var changed=new HashSet<Guid>();var collision=false;var unverified=false;
            foreach(var shape in candidates)
            {
                if(detail.Region.RepairIds is not null&&shape.Kind=="SPACE"&&(detail.Drawing.RecognitionReview?.Issues??[]).Any(i=>i.ShapeIds.Contains(shape.Id)&&i.Code is "source_edges" or "orientation" or "nested_outline")){unverified=true;continue;}
                var existing=proposal.FindIndex(old=>SameObject(old,shape));
                if(existing>=0)
                {
                    if(protectedIds.Contains(proposal[existing].Id)||shape.Kind!="SPACE")continue;
                    var replacement=shape with{Id=proposal[existing].Id,Label=proposal[existing].Label};proposal[existing]=replacement;changed.Add(replacement.Id);continue;
                }
                // A crop cannot replace a source-supported neighbouring bay by drawing on top of it.
                if(shape.Kind=="SPACE"&&proposal.Where(s=>protectedIds.Contains(s.Id)).Any(old=>ParkingDrawingRules.SpaceOverlaps(frame with{Shapes=[old,shape]}).Any())){collision=true;continue;}
                var matching=removed.Where(s=>s.Kind==shape.Kind).OrderBy(s=>Math.Pow(s.X+s.Width/2-shape.X-shape.Width/2,2)+Math.Pow(s.Y+s.Height/2-shape.Y-shape.Height/2,2)).FirstOrDefault(s=>!changed.Contains(s.Id)&&SameObject(s,shape));
                var added=matching is null?shape:shape with{Id=matching.Id,Label=matching.Label};
                if(added.Kind=="SPACE"&&proposal.Any(s=>s.Kind=="SPACE"&&s.Label.Equals(added.Label,StringComparison.OrdinalIgnoreCase)))
                {
                    var next=1;string label;do{label=$"P-{next++:000}";}while(proposal.Concat(candidates).Any(s=>s.Kind=="SPACE"&&s.Label.Equals(label,StringComparison.OrdinalIgnoreCase)));
                    added=added with{Label=label};
                }
                proposal.Add(added);changed.Add(added.Id);
            }
            if(removed.Any(s=>s.Kind=="SPACE")&&!proposal.Any(s=>s.Kind=="SPACE"&&changed.Contains(s.Id))){rejected++;continue;}
            var bad=ParkingDrawingRules.SpaceOverlaps(frame with{Shapes=proposal.ToArray()}).Where(p=>changed.Contains(p.First.Id)||changed.Contains(p.Second.Id)).ToArray();
            if(bad.Length>0){rejected++;continue;}
            try{ParkingDrawingRules.Validate(frame with{Shapes=proposal.ToArray(),RecognitionReview=null});}
            catch(ParkingFailure){rejected++;continue;}
            if(collision){rejected++;mergeIssues.Add(new("region_conflict","Một đề xuất trong vùng chồng lên ô đã bám nét nguồn; đã giữ ô cũ để đối chiếu.",[]));}
            else if(unverified){rejected++;mergeIssues.Add(new("region_source","Một ô mới trong vùng chưa bám nét nguồn; không thêm vào phần đã đúng.",[]));}
            result=proposal;
        }
        var labels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var number=1;
        var shapes=result.Select(s=>
        {
            if(s.Kind!="SPACE")return s;
            var label=s.Label;if(label.Length==0||!labels.Add(label)){do{label=$"P-{number++:000}";}while(!labels.Add(label));}
            return s with{Label=label,NeedsReview=true};
        }).ToArray();
        var scale=overview?.Scale??details.Select(d=>d.Drawing?.Scale).FirstOrDefault(s=>s is not null);
        var checkedDrawing=ParkingSourceGeometry.Inspect(ParkingDrawingRules.Validate(frame with{Shapes=shapes,Scale=scale,RecognitionReview=null,Digitization=frame.Digitization is {} info?info with{Candidates=shapes.Count(s=>s.Kind=="SPACE")}:null}),source);
        return checkedDrawing with{RecognitionReview=checkedDrawing.RecognitionReview! with{UnreadRegions=Math.Min(3,rejected),Issues=checkedDrawing.RecognitionReview.Issues.Concat(mergeIssues).Take(ParkingDrawingRules.MaxObjects).ToArray()}};
    }
    private static bool SameObject(ParkingShape a,ParkingShape b)
    {
        if(a.Kind!=b.Kind)return false;
        var distance=Math.Sqrt(Math.Pow(a.X+a.Width/2-b.X-b.Width/2,2)+Math.Pow(a.Y+a.Height/2-b.Y-b.Height/2,2));
        var areaRatio=a.Width*a.Height/(b.Width*b.Height);
        if(areaRatio is >.45 and <2.2&&distance<Math.Min(Math.Min(a.Width,a.Height),Math.Min(b.Width,b.Height))*.35)return a.Kind=="SPACE"||a.Label==b.Label;
        if(a.Kind=="SPACE"||a.Label.Length==0||a.Label!=b.Label||a.ControlPoints is not null||b.ControlPoints is not null)return false;
        // Do not add a cropped fragment of a named region already represented by a larger overview block.
        return b.X>=a.X-a.Width*.03&&b.Y>=a.Y-a.Height*.03&&b.X+b.Width<=a.X+a.Width*1.03&&b.Y+b.Height<=a.Y+a.Height*1.03;
    }
}
