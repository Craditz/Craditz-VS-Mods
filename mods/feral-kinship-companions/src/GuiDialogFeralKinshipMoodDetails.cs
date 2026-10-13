#nullable enable
using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
namespace FeralKinshipCompanions;

// Read-only snapshot, drawn by the same skin system as the rest of Companions.
public sealed class GuiDialogFeralKinshipMoodDetails : GuiDialog
{
    private readonly GuiElementCompanionMoodDetails surface;
    private readonly FeralKinshipCompanionSystem system;
    private readonly string foxId;
    private string requestId = string.Empty;
    private float responseWait;
    private bool waiting;
    public GuiDialogFeralKinshipMoodDetails(ICoreClientAPI api, FeralKinshipCompanionSystem system,
        FoxPackMemberPacket member, Action appearanceChanged) : base(api)
    {
        this.system = system;
        foxId = member.FoxId;
        string name = string.IsNullOrWhiteSpace(member.Name) ? "Companion" : member.Name;
        surface = new GuiElementCompanionMoodDetails(api, ElementBounds.Fixed(0, 0, 760, 540), name,
            new List<string> { "Checking current needs..." }, () => TryClose(), appearanceChanged);
    }
    private void Receive(CompanionWellbeingReadResponse response)
    {
        if (!IsOpened() || response.FoxId != foxId || response.RequestId != requestId) return;
        waiting = false;
        surface.ApplyFreshState(response);
    }
    public override void OnRenderGUI(float dt)
    {
        base.OnRenderGUI(dt);
        if (waiting && (responseWait += dt) >= 10f)
        {
            waiting = false;
            surface.ApplyFreshState(new CompanionWellbeingReadResponse { Message = "The current-needs check timed out. Close and reopen to try again." });
        }
    }
    public override void OnGuiClosed()
    {
        system.WellbeingReadReceived -= Receive;
        base.OnGuiClosed();
    }
    public override double DrawOrder => 0.21;
    public override double InputOrder => 0.4;
    public override string ToggleKeyCombinationCode => null!;
    public override void OnGuiOpened()
    {
        var content = ElementBounds.Fixed(0, 0, 760, 540).WithFixedPadding(0);
        content.BothSizing = ElementSizing.Fixed;
        SingleComposer = capi.Gui.CreateCompo("feralkinship-mood-details",
            ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(content)
            .AddDialogTitleBar("Mood details", () => TryClose())
            .BeginChildElements(content)
            .AddInteractiveElement(surface, "mood-surface")
            .EndChildElements().Compose();
        base.OnGuiOpened();
        requestId = Guid.NewGuid().ToString("N");
        responseWait = 0; waiting = true;
        system.WellbeingReadReceived += Receive;
        system.RequestWellbeingRead(foxId, requestId);
    }
}

internal sealed class GuiElementCompanionMoodDetails : GuiElementFeralKinshipScaledSurface
{
    protected override double DesignWidth => 760;
    protected override double DesignHeight => 540;
    private readonly string name;
    private readonly Action close;
    private readonly Action appearanceChanged;
    private CompanionWellbeingState? state;
    private string mood = "";
    private bool responseReceived;
    private string message = "Checking current needs...";
    private readonly List<CareGroup> groups = new();
    private int selectedGroup = -1;
    private int texture;
    private int themeRevision = -1;
    private double textScale = -1;
    private sealed record CareRow(string Need, string Detail, float? Effect);
    private sealed record CareGroup(string Title, string Hint, List<CareRow> Rows);
    public GuiElementCompanionMoodDetails(ICoreClientAPI api, ElementBounds bounds,
        string name, List<string> lines, Action close, Action appearanceChanged) : base(api, bounds)
    { this.name=name; this.close=close; this.appearanceChanged=appearanceChanged; }
    public void ApplyFreshState(CompanionWellbeingReadResponse response)
    {
        SetFreshState(response);
        Redraw();
    }
    // Shared by the live UI and offline layout verification; no GL work here.
    internal void SetFreshState(CompanionWellbeingReadResponse response)
    {
        responseReceived = true;
        groups.Clear(); state = response.Live && response.Wellbeing?.HasObservation == true ? response.Wellbeing.Copy() : null;
        mood=response.Mood; message=response.Message;
        if (state != null)
        {
            var w=state;
            groups.Add(new("Essentials", "Food, health and a place to call home", new(){
                new("Food", $"{w.Food} / {(w.FoodEnabled ? $"{w.FoodLevel*100:0}% full" : "hunger disabled")}",w.FoodContribution),
                new("Health",w.Health,w.HealthContribution),
                new("Housing",w.Home == "No home" ? "No home" : w.Home == "Home not currently verifiable" ? "Unknown; previous value held" : w.Bed,w.HousingContribution)}));
            groups.Add(new("Comfort & food", "Small comforts make a difference", new(){
                new("Bed comfort",w.BedComfort,w.BedComfortContribution),
                new("Favorite",string.IsNullOrEmpty(w.FavoriteFoodCode)?"No suitable favorite":w.FavoriteFoodName,null),
                new("Favorite meal",w.FavoriteFoodSeconds>0?"Enjoyed recently":"No recent favorite meal",w.FavoriteFoodContribution)}));
            groups.Add(new("Surroundings", "Feeling safe and staying sheltered",new(){
                new("Safety",w.Safety,w.SafetyContribution), new("Shelter",w.Shelter,w.ShelterContribution)}));
            groups.Add(new("Company & play", "Familiar faces and something to do",new(){
                new("Company",w.Company,w.CompanyContribution),new("Own kind",w.KinContribution>0?"Same-kind companion nearby":"No extra benefit",w.KinContribution),
                new("Activity",w.Enrichment,w.EnrichmentContribution)}));
            groups.Add(new("Work & rest", "A manageable workload",new(){
                new("Workload",w.Workload,w.WorkloadContribution),new("Exertion",$"{w.WorkBurden:0.#} / 100",null),
                new("Recent trips",w.TripsSinceRest.ToString(),null)}));
            groups.Add(new("Recent moments", "Good experiences and hard journeys",new(){
                new("Expedition",w.ExpeditionFeeling,w.ExpeditionFeelingContribution),
                new("Requests",w.RequestFeelingSeconds>0?"Request fulfilled":"No recent completion",w.RequestFeelingContribution),
                new("Progress",w.ProgressFeelingSeconds>0?$"Proud of reaching level {w.LastWellbeingLevel}":"No recent milestone",w.ProgressFeelingContribution)}));
        }
        selectedGroup=-1;
    }
    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic) => Redraw();
    public override void RenderInteractiveElements(float dt)
    { if(themeRevision!=FoxGuiTheme.ThemeRevision||textScale!=FeralKinshipCompanionUiSettings.TextScale)Redraw(); if(texture>0)Render2DTexture(texture,Bounds); }
    public override void Dispose(){if(texture>0)api.Render.GLDeleteTexture(texture);texture=0;base.Dispose();}
    private static string Points(float value)=>value.ToString("+0.#;-0.#;0");
    private static float Total(CareGroup group){float total=0;foreach(var row in group.Rows)total+=row.Effect??0;return total;}
    private static double[] EffectColor(float? value)=>value<0?FoxGuiTheme.Danger:value>0?FoxGuiTheme.Success:FoxGuiTheme.Muted;
    internal string ScoreText => state==null?"-":state.Value.ToString("0.#");
    private void Redraw()
    {
        Bounds.CalcWorldBounds();
        using ImageSurface image=new(Format.Argb32,Math.Max(1,Bounds.OuterWidthInt),Math.Max(1,Bounds.OuterHeightInt));
        using Context ctx=new(image);ScaleCanvas(ctx);
        FoxGuiTheme.DrawJournal(ctx,760,540,FoxGuiSurfaceKind.Companion);FoxGuiTheme.DrawWindowControls(ctx,api,760);
        DrawContent(ctx);
        generateTexture(image,ref texture);themeRevision=FoxGuiTheme.ThemeRevision;textScale=FeralKinshipCompanionUiSettings.TextScale;
    }
    internal void DrawContent(Context ctx)
    {
        Text(ctx,"Mood details",22,28,20,FoxGuiTheme.Text,400);
        Text(ctx,name,30,80,28,FoxGuiTheme.Accent,400);
        Text(ctx,"Mood: "+(string.IsNullOrEmpty(mood)?(responseReceived?"unavailable":"checking"):mood),31,105,14,FoxGuiTheme.Muted,420);
        FoxGuiTheme.DrawPanel(ctx,530,54,200,78,true);
        Text(ctx,"Wellbeing",547,76,12,FoxGuiTheme.Muted,165);
        Text(ctx,ScoreText,547,113,30,state?.Value<0?FoxGuiTheme.Danger:FoxGuiTheme.Accent,165);
        FoxGuiTheme.DrawSectionRule(ctx,30,135,700);
        if(state==null) Text(ctx,string.IsNullOrWhiteSpace(message)?"Current needs unavailable":message,30,185,16,FoxGuiTheme.Muted,700);
        else if(selectedGroup<0) DrawDashboard(ctx);
        else DrawDetails(ctx);
        FoxGuiTheme.DrawSectionRule(ctx,30,444,700);
        string readiness=state==null?(responseReceived?"Current needs unavailable":"Waiting for current needs"):!state.HasEstablishedHome?"Before your first Pack Cart: wellbeing stays at 50":CompanionWellbeingRecovery.Label(CompanionWellbeingRecovery.Update(state));
        Text(ctx,readiness,30,467,15,FoxGuiTheme.Text,700);
        Text(ctx,"Care cards",30,512,14,FoxGuiTheme.Muted,230);
        Button(ctx,"Close",600,490,130,true);
    }
    private void DrawDashboard(Context ctx)
    {
        for(int i=0;i<groups.Count && i<6;i++)
        {
            double x=30+(i%2)*358,y=150+(i/2)*96;var group=groups[i];
            FoxGuiTheme.DrawPanel(ctx,x,y,342,84);
            Text(ctx,group.Title,x+14,y+25,17,FoxGuiTheme.Text,245);
            Text(ctx,Points(Total(group)),x+272,y+25,18,EffectColor(Total(group)),56);
            var focus=group.Rows.Find(r=>r.Effect<0)??group.Rows.Find(r=>r.Effect>0)??group.Rows[0];
            string detail=group.Title=="Comfort & food"?"Favorite: "+(string.IsNullOrWhiteSpace(state?.FavoriteFoodName)?"No suitable favorite":state.FavoriteFoodName):focus.Need+": "+focus.Detail;
            Text(ctx,detail,x+14,y+49,13,FoxGuiTheme.Muted,315);
            Text(ctx,"View details  >",x+14,y+70,12,FoxGuiTheme.Accent,315);
        }
    }
    private void DrawDetails(Context ctx)
    {
        Button(ctx,"< All needs",30,150,170,true);
        Text(ctx,groups[selectedGroup].Title,220,173,19,FoxGuiTheme.Accent,500);
        var rows=groups[selectedGroup].Rows;
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];double y=216+i*60;
            FoxGuiTheme.DrawPanel(ctx,30,y-22,700,54);
            Text(ctx,row.Need,42,y,14,FoxGuiTheme.Text,145);
            Text(ctx,row.Detail,200,y,14,FoxGuiTheme.Muted,425);
            Text(ctx,row.Effect.HasValue?Points(row.Effect.Value):"-",652,y,15,EffectColor(row.Effect),66);
        }
        Text(ctx,GroupNote(selectedGroup),42,427,12,FoxGuiTheme.Muted,680);
    }
    private string GroupNote(int group)=>group switch
    {
        0=>state?.Home=="No home"?"No home replaces the bed penalty; it does not stack with it.":"Housing covers the basic need. Fancy-bed comfort is a separate bonus.",
        1=>"Reach the bed to earn comfort. Two in-game days without a visit removes it.",
        2=>"Nearby threats and wet weather count only while observed.",
        3=>"Personality affects how much company and activity they need.",
        4=>"Low wellbeing limits work; suitable food duties remain available.",
        _=>"Recent feelings fade during active observation. Repeats refresh, not stack."
    };
    internal static void Text(Context ctx,string text,double x,double y,double size,double[] color,double width)
    {
        using CairoFont font=CairoFont.WhiteSmallText().WithFontSize((float)FeralKinshipCompanionUiSettings.ScaleFont(size));font.Color=color;
        double requested=FeralKinshipCompanionUiSettings.ScaleFont(size);
        double height=CompanionCareCardLayout.LineHeight(size);
        font.WithFontSize((float)Math.Min(requested,height));font.SetupContext(ctx);
        var ext=ctx.TextExtents(text);
        double fit=Math.Min(1d,Math.Min(width/Math.Max(1,ext.Width),height/Math.Max(1,ext.Height)));
        if(fit<1d){font.WithFontSize((float)(Math.Min(requested,height)*fit));font.SetupContext(ctx);}
        ctx.MoveTo(x,y);ctx.ShowText(text);
    }
    private static void Button(Context ctx,string label,double x,double y,double width,bool enabled)
    {FoxGuiTheme.DrawButtonSurface(ctx,x,y,width,32,enabled);Text(ctx,label,x+12,y+22,14,enabled?FoxGuiTheme.Text:FoxGuiTheme.Muted,width-24);}
    public override void OnMouseDownOnElement(ICoreClientAPI api,MouseEvent args)
    {
        args.Handled=true;if(args.Button!=EnumMouseButton.Left)return;
        double x=CanvasMouseX(args.X),y=CanvasMouseY(args.Y);
        if(HandleFontScaleClick(x,y)){appearanceChanged();Redraw();return;}
        if(x>=725&&y<=38 || x>=600&&x<=730&&y>=490&&y<=522){close();return;}
        if(state==null)return;
        if(selectedGroup>=0 && x>=30&&x<=200&&y>=150&&y<=182)selectedGroup=-1;
        else if(selectedGroup<0)
        {
            int choice=CompanionCareCardLayout.GroupAt(x,y);
            if(choice>=0&&choice<groups.Count)selectedGroup=choice;
        }
        Redraw();
    }
}
