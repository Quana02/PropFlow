using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Detects a strongly contradicted bay axis without changing source geometry.</summary>
internal static class ParkingBayEdgeAlignment
{
    internal static void ValidateOrientation(ParkingShape shape,ParkingGeometryResult source,CancellationToken ct)
    {
        if(shape.Kind!="SPACE"||shape.Outline is not null||Math.Max(shape.Width,shape.Height)/Math.Min(shape.Width,shape.Height)<1.25)return;
        ct.ThrowIfCancellationRequested();
        var alternative=shape with{Rotation=(shape.Rotation+90)%360,NeedsReview=true};
        try{ParkingDrawingRules.Validate(new(source.Drawing.Width,source.Drawing.Height,[alternative]));}catch(ParkingFailure){return;}
        var current=Evidence(shape,source);var rotated=Evidence(alternative,source);
        if(rotated.Average>=.78&&rotated.LongEdges>=.72&&rotated.Average-current.Average>=.20)
            throw new ParkingFailure(400,"parking_ai_orientation_invalid",$"Hướng ô {shape.Label} chưa khớp nét bản gốc. Đọc lại bốn góc ô này; không sao chép hướng của dãy khác.");
    }
    internal static (double Average,double LongEdges) Evidence(ParkingShape shape,ParkingGeometryResult source,int maximumSamples=160)
    {
        var angle=shape.Rotation*Math.PI/180;var c=Math.Cos(angle);var s=Math.Sin(angle);
        var corners=(shape.Outline??[new(0,0),new(1,0),new(1,1),new(0,1)]).Select(p=>new ParkingPoint(
            (shape.X+shape.Width/2+(p.X-.5)*shape.Width*c-(p.Y-.5)*shape.Height*s)*source.PixelWidth/source.Drawing.Width,
            (shape.Y+shape.Height/2+(p.X-.5)*shape.Width*s+(p.Y-.5)*shape.Height*c)*source.PixelHeight/source.Drawing.Height)).ToArray();
        var scores=new List<(double Length,double Score)>();
        var radius=Math.Clamp((int)Math.Round(Math.Min(shape.Width*source.PixelWidth/source.Drawing.Width,shape.Height*source.PixelHeight/source.Drawing.Height)*.025),1,3);
        for(var edge=0;edge<4;edge++)
        {
            var a=corners[edge];var b=corners[(edge+1)%4];var length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));var samples=Math.Clamp((int)Math.Ceiling(length),Math.Min(16,maximumSamples),maximumSamples);var hits=0;
            for(var i=0;i<samples;i++)
            {
                // Avoid shared corners, which can support either of two perpendicular hypotheses.
                var t=.08+.84*(i+.5)/samples;var x=(int)Math.Round(a.X+(b.X-a.X)*t);var y=(int)Math.Round(a.Y+(b.Y-a.Y)*t);var found=false;
                for(var yy=Math.Max(0,y-radius);yy<=Math.Min(source.PixelHeight-1,y+radius)&&!found;yy++)
                    for(var xx=Math.Max(0,x-radius);xx<=Math.Min(source.PixelWidth-1,x+radius);xx++)if(source.Ink[yy*source.PixelWidth+xx]!=0){found=true;break;}
                if(found)hits++;
            }
            scores.Add((length,hits/(double)samples));
        }
        return(scores.Average(v=>v.Score),scores.OrderByDescending(v=>v.Length).Take(2).Min(v=>v.Score));
    }
}
