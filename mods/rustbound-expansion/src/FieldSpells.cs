using System.Text.Json;
using rustboundmagic.src.system.enums;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace RustboundExpansion;

internal static class FieldCatalog
{
    internal static readonly SpellDefinition[] All=Read();
    private static SpellDefinition[] Read()
    {
        using var stream=typeof(FieldCatalog).Assembly.GetManifestResourceStream("RustboundExpansion.field-spell-definitions.json")!;
        return JsonSerializer.Deserialize<SpellDefinition[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
}

public sealed class FieldSpell : ExpansionSpell
{
    internal readonly SpellDefinition Definition;
    internal FieldSpell(SpellDefinition d):base(d.Id,d.School,d.Essence)
    {
        Definition=d;Tier=d.Tier;ManaCost=d.Mana;CastTime=d.Cast;Duration=d.Duration;Range=d.Range;Damage=d.Damage;Element=d.Element;
        BuffType=d.Kind=="wallop"?EnumBuffType.none:EnumBuffType.buff;
        SoundAssetLocationSpellCastingSuccessful=new("rustboundmagic:sounds/effects/castcomplete-"+(d.Damage>0?"evocation":"buff")+".ogg");
    }
    public override void OnCastStop(float seconds,ItemSlot slot,EntityAgent caster,BlockSelection block,EntitySelection entity,
        bool scroll,bool magnification,bool duplication,float remaining,bool infinite=false)
    {
        if(caster is not EntityPlayer p || p.World.Side!=EnumAppSide.Server || remaining!=0 || !p.Alive || p.ShouldDespawn
            || ControlEffects.Banished(p) || ExpansionRuntime.Active(p,"stasis"))return;
        var runtime=ExpansionRuntime.For(p.World);if(runtime==null)return;
        if(Definition.Kind=="breadcrumbs" && ExpansionRuntime.Active(p,Id)) {runtime.ClearField(p,Id);return;}
        if(!scroll && p.WatchedAttributes.GetInt("entitybehavior-resource-currentmana_rm")<GetModifiedManaCost(p,Id))
        {NotifyFailure(p);return;}
        var target=Definition.Kind=="wallop"?RayTraceDestinationSelectedEntityAgent(p,Range):null;
        if(!runtime.CastField(this,p,target,GetModifiedDamage(p,Id),GetModifiedDuration(p,Id))) {NotifyFailure(p);return;}
        Complete(p,scroll,false);
    }
}

// No collectible, block edits, ignition or damage logic lives in this visual helper.
internal static class TrailVisual
{
    internal static void Emit(IWorldAccessor world,Vec3d point,bool cinder)
    {
        if(cinder)
        {
            int ember=ColorUtil.ToRgba(255,255,65,12),flame=ColorUtil.ToRgba(235,255,155,35);
            world.SpawnParticles(new SimpleParticleProperties(4,6,ember,point.AddCopy(-.4,.025,-.4),point.AddCopy(.4,.08,.4),
                new Vec3f(-.025f,.04f,-.025f),new Vec3f(.025f,.13f,.025f),.8f,0,.18f,.3f,EnumParticleModel.Cube)
            {WithTerrainCollision=false,LightEmission=int.MaxValue,OpacityEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-1.1f)});
            world.SpawnParticles(new SimpleParticleProperties(3,5,flame,point.AddCopy(-.35,.05,-.35),point.AddCopy(.35,.12,.35),
                new Vec3f(-.04f,.35f,-.04f),new Vec3f(.04f,.75f,.04f),.7f,0,.65f,1.15f,EnumParticleModel.Quad)
            // Particle color and native light use opposite channel layouts.
            // Neutral full-bright light preserves orange instead of tinting it green.
            {WithTerrainCollision=false,LightEmission=int.MaxValue,OpacityEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-1.3f),
                SizeEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-.15f)});
            return;
        }
        int color=cinder?Vfx.Color("fire"):ColorUtil.ToRgba(245,80,220,255);
        world.SpawnParticles(new SimpleParticleProperties(cinder?3:1,cinder?5:1,color,
            point.AddCopy(cinder?-.4:-.08,.025,cinder?-.4:-.08),point.AddCopy(cinder?.4:.08,.055,cinder?.4:.08),
            new Vec3f(0,.015f,0),new Vec3f(0,.045f,0),.85f,0,cinder?.13f:.2f,cinder?.24f:.25f,EnumParticleModel.Cube)
        {WithTerrainCollision=false,LightEmission=color,OpacityEvolve=EvolvingNatFloat.create(EnumTransformFunction.LINEAR,-1.1f)});
    }
    internal static Vec3d? Surface(IWorldAccessor world,Vec3d feet,int dimension)
    {
        int x=(int)Math.Floor(feet.X),z=(int)Math.Floor(feet.Z);
        double offset=(double)dimension*BlockPos.DimensionBoundary,localY=feet.Y-offset;
        // Use real collision tops, so slabs work and airborne movement leaves no floating trail.
        for(int y=(int)Math.Floor(localY+.1);y>=Math.Floor(localY-.6);y--)
        {
            var pos=new BlockPos(x,y,z,dimension);
            var boxes=world.BlockAccessor.GetBlock(pos)?.GetCollisionBoxes(world.BlockAccessor,pos);
            if(boxes==null)continue;
            foreach(var box in boxes.OrderByDescending(b=>b.Y2))
                if(feet.X>=x+box.X1 && feet.X<=x+box.X2 && feet.Z>=z+box.Z1 && feet.Z<=z+box.Z2
                    && y+box.Y2<=localY+.12 && y+box.Y2>=localY-.55)
                    return new(feet.X,y+box.Y2+offset,feet.Z);
        }
        return null;
    }
}

internal sealed record TrailPoint(Vec3d Position,long End);
internal sealed class TrailPath(int limit,long lifetime,bool retainUntilClear=false)
{
    internal readonly List<TrailPoint> Points=[];
    private Vec3d? last;
    internal void Sample(IWorldAccessor world,Vec3d feet,int dimension,long now)
    {
        if(!retainUntilClear)Points.RemoveAll(p=>now>=p.End);
        var point=TrailVisual.Surface(world,feet,dimension);if(point==null)return;
        if(last!=null && point.SquareDistanceTo(last)<.45*.45)return;
        // Record actual positions only: never draw across a teleport or invent points through walls.
        last=point.Clone();Points.Add(new(point,retainUntilClear?long.MaxValue:now+lifetime));
        if(!retainUntilClear && Points.Count>limit)Points.RemoveRange(0,Points.Count-limit);
    }
    internal void Clear(){Points.Clear();last=null;}
}

// A private, client-local cosmetic trail. Native buff replication is the only network state.
public sealed partial class BreadcrumbSystem : ModSystem,IRenderer
{
    private ICoreClientAPI? client;
    private long listener;
    private int dimension=int.MinValue;
    // Sampling is limited to twice a second; retain every point for this buff's lifetime.
    internal readonly TrailPath Path=new(0,0,retainUntilClear:true);
    public override void StartClientSide(ICoreClientAPI api)
    {
        client=api;listener=api.Event.RegisterGameTickListener(_=>Tick(),500);api.Event.LeaveWorld+=Clear;
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"rbx-breadcrumb-bread");
    }
    private void Tick()
    {
        if(client!=null)Update(client.World,client.World.Player?.Entity);
    }
    internal void Update(IWorldAccessor world,EntityPlayer? p)
    {
        if(p==null || !p.Alive || p.ShouldDespawn || !ExpansionRuntime.Active(p,"rbxbreadcrumbs1"))
        {Clear();return;}
        string variety=p.WatchedAttributes.GetString(BreadcrumbBread.VarietyKey,"");
        int seed=p.WatchedAttributes.GetInt(BreadcrumbBread.SeedKey);
        if(!BreadcrumbBread.Types.Contains(variety)){Clear();return;}
        if(p.Pos.Dimension!=dimension || variety!=BreadType || seed!=BreadSeed)
        {Clear();dimension=p.Pos.Dimension;BreadType=variety;BreadSeed=seed;}
        if(ControlEffects.Banished(p) || ExpansionRuntime.Active(p,"stasis"))return;
        var position=p.Pos.XYZ;
        Path.Sample(world,position,p.Pos.Dimension,world.ElapsedMilliseconds);
        // Keep all recorded points; only a nearby subset needs drawing.
        Visible.Clear();
        Visible.AddRange(Enumerable.Range(0,Path.Points.Count).Where(i=>Path.Points[i].Position.SquareDistanceTo(position)<48*48)
            .OrderBy(i=>Path.Points[i].Position.SquareDistanceTo(position)).Take(96));
    }
    private void Clear(){Path.Clear();dimension=int.MinValue;ClearBread();}
    public override void Dispose()
    {
        if(client!=null)
        {client.Event.UnregisterGameTickListener(listener);client.Event.LeaveWorld-=Clear;client.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);}
        Clear();
    }
}
