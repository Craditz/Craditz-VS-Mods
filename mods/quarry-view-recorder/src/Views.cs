using Vintagestory.API.Common.Entities;
namespace QuarryViewRecorder;

public sealed class View
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Roll { get; set; }
    public int Dimension { get; set; }
    public static View From(EntityPos p) => new() { X=p.X,Y=p.Y,Z=p.Z,Pitch=p.Pitch,Yaw=p.Yaw,Roll=p.Roll,Dimension=p.Dimension };
}
public sealed class RecorderConfig
{
    public int IntervalMinutes { get; set; } = 10;
    public int SettleSeconds { get; set; } = 5;
    // The user's native .cam save export, divided by 100 / 1000 exactly as vanilla loads it.
    public View[] Views { get; set; } = {
        new() { X=511988.37,Y=122,Z=511939.50,Pitch=3.344f,Yaw=6.978f },
        new() { X=512003.35,Y=144.83,Z=511955.21,Pitch=4.697f,Yaw=4.701f },
        new() { X=512009.39,Y=118.11,Z=511935.64,Pitch=3.315f,Yaw=5.692f },
        new() { X=512012.93,Y=118.28,Z=511962.71,Pitch=3.623f,Yaw=4.010f }
    };
}
