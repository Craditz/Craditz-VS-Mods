using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RustboundExpansion;

internal sealed class HeatItemDialog : GuiDialog
{
    internal const string Heading = "SELECT HEATABLE ITEM";
    internal const int Columns = 7, PerPage = 28;
    private readonly HeatOffer offer;
    private readonly Action<HeatChoice> send;
    private readonly long end;
    private int page;
    private bool waiting, serverClose;
    private List<SkillItem> items = [];
    internal string Token => offer.Token;
    public override string ToggleKeyCombinationCode => null!;
    public override bool UnregisterOnClose => true;

    internal HeatItemDialog(ICoreClientAPI api, HeatOffer offer, Action<HeatChoice> send) : base(api)
    {
        this.offer = offer; this.send = send;
        end = api.World.ElapsedMilliseconds + HeatItemSystem.LifetimeMs;
        Compose();
    }
    private void Compose()
    {
        SingleComposer?.Dispose();
        foreach (var item in items) item.Dispose();
        items = [];
        int first = page * PerPage, count = Math.Min(PerPage,offer.Items.Length-first);
        for (int local=0;local<count;local++)
        {
            var entry=offer.Items[first+local];
            var stack=Resolve(entry); var slot=new DummySlot(stack);
            items.Add(new SkillItem {
                Code=new("rustboundexpansion:heat-choice-"+(first+local)),
                Name=stack?.GetName() ?? "Heatable item",
                Description=Details(entry),
                RenderHandler=(_,dt,x,y) => {
                    if (!slot.Empty) capi.Render.RenderItemstackToGui(slot,x+GuiElement.scaled(24),y+GuiElement.scaled(24),100,
                        (float)GuiElement.scaled(36),-1,dt,true,false,true);
                }
            });
        }
        int rows=Math.Max(1,(count+Columns-1)/Columns);
        double gridHeight=rows*(GuiElementPassiveItemSlot.unscaledSlotSize+GuiElementItemSlotGrid.unscaledSlotPadding);
        const double width=510;
        var bg=ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing=ElementSizing.FitToChildren;
        var composer=capi.Gui.CreateCompo("rbx-heat-picker",ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bg,true)
            .AddDialogTitleBar(Heading,()=>TryClose())
            .BeginChildElements(bg)
            .AddStaticText("Select an item to heat. Items stay in your inventory.",
                CairoFont.WhiteSmallishText(),ElementBounds.Fixed(0,35,width,44))
            .AddSkillItemGrid(items,Columns,rows,index=>Choose(first+index),ElementBounds.Fixed(0,89,width,gridHeight),"choices")
            .AddDynamicText(items[0].Name,CairoFont.WhiteSmallishText(),ElementBounds.Fixed(0,100+gridHeight,width,30),"name")
            .AddDynamicText(items[0].Description,CairoFont.WhiteDetailText(),ElementBounds.Fixed(0,132+gridHeight,width,52),"details")
            .AddStaticText($"{offer.TotalHeat:0.##}°C shared across the stack. {offer.Mana} mana on success. Cancel is free.",
                CairoFont.WhiteDetailText(),ElementBounds.Fixed(0,190+gridHeight,width,36))
            .AddStaticText($"Page {page+1}/{(offer.Items.Length+PerPage-1)/PerPage}",CairoFont.WhiteDetailText(),ElementBounds.Fixed(190,239+gridHeight,160,30));
        if (page>0) composer.AddSmallButton("Previous",()=>{page--;Compose();return true;},ElementBounds.Fixed(0,231+gridHeight,90,28));
        if ((page+1)*PerPage<offer.Items.Length) composer.AddSmallButton("Next",()=>{page++;Compose();return true;},ElementBounds.Fixed(94,231+gridHeight,66,28));
        composer.AddSmallButton("Cancel",()=>{TryClose();return true;},ElementBounds.Fixed(410,231+gridHeight,100,28));
        SingleComposer=composer.EndChildElements().Compose();
        SingleComposer.GetSkillItemGrid("choices").OnSlotOver=index=>{
            if (index<0 || index>=items.Count) return;
            SingleComposer.GetDynamicText("name").SetNewText(items[index].Name);
            SingleComposer.GetDynamicText("details").SetNewText(items[index].Description);
        };
    }
    internal static string Details(HeatCandidate entry) =>
        $"×{entry.Count}    {entry.Current:0.##}°C → {entry.Result:0.##}°C\n+{entry.Result-entry.Current:0.##}°C each. Maximum {entry.Cap:0.##}°C.";
    private ItemStack? Resolve(HeatCandidate entry)
    {
        var inventory=capi.World.Player.InventoryManager.GetInventory(entry.Inventory);
        var slot=inventory!=null && entry.Slot>=0 && entry.Slot<inventory.Count ? inventory[entry.Slot] : null;
        if (slot?.Itemstack is {} actual && actual.Id==entry.ItemId && (actual.Class==EnumItemClass.Block)==entry.Block)
        {var copy=actual.Clone();copy.StackSize=entry.Count;return copy;}
        if (entry.Block) return capi.World.GetBlock(entry.ItemId) is {} block ? new ItemStack(block,entry.Count) : null;
        return capi.World.GetItem(entry.ItemId) is {} item ? new ItemStack(item,entry.Count) : null;
    }
    private void Choose(int index)
    {
        if (waiting || index<0 || index>=offer.Items.Length) return;
        waiting=true;send(new(){Token=offer.Token,Index=index});
    }
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode==(int)GlKeys.Escape) {args.Handled=true;TryClose();return;}
        base.OnKeyDown(args);
    }
    public override void OnGuiClosed()
    {
        if (!serverClose) send(new(){Token=offer.Token,Cancel=true});
        base.OnGuiClosed();
    }
    internal void CloseFromServer() {serverClose=true;TryClose();}
    public override void OnRenderGUI(float dt)
    {
        if (capi.World.ElapsedMilliseconds>=end) {TryClose();return;}
        base.OnRenderGUI(dt);
    }
    public override void Dispose() {foreach(var item in items)item.Dispose();items.Clear();base.Dispose();}
}
