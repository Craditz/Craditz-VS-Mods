#nullable disable

using System;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipTrainingDummy : GuiDialog
{
    private const double DialogWidth = 520;
    private const double DialogHeight = 300;
    private const double Padding = 14;
    private const double ButtonHeight = 38;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private TrainingDummyStatePacket state;

    public GuiDialogFeralKinshipTrainingDummy(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        long targetEntityId)
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
        if (state != null)
        {
            UpdateDisplay();
        }
    }

    public void ApplyState(TrainingDummyStatePacket packet)
    {
        if (packet.TargetEntityId != targetEntityId)
        {
            return;
        }

        state = packet;
        if (SingleComposer != null)
        {
            UpdateDisplay();
        }
    }

    private void ComposeDialog()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight)
            .WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-training-dummy", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Training Dummy", () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddDynamicText(
            "Waiting for dummy data...",
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, 16, 490, 180),
            "report"
        );
        composer.AddButton(
            "Remove dummy",
            OnDelete,
            ElementBounds.Fixed(0, 220, 490, ButtonHeight),
            EnumButtonStyle.Normal,
            "delete"
        ).EndChildElements();

        SingleComposer = composer.Compose();
    }

    private bool OnDelete()
    {
        system.SendTrainingDummyAction(targetEntityId, TrainingDummyAction.Delete);
        TryClose();
        return true;
    }

    private void UpdateDisplay()
    {
        string lastHit = state.HitCount == 0
            ? "Last hit: No attacks recorded"
            : $"Last hit: {state.LastAttacker}\n"
                + $"Damage dealt: {state.LastDamage:0.###}\n"
                + $"Damage tier: {state.LastDamageTier}\n"
                + $"Damage type: {state.LastDamageType}";

        SingleComposer.GetDynamicText("report").SetNewText(
            $"Health: {state.CurrentHealth:0.###} / {state.MaxHealth:0.###}\n"
                + $"Hits recorded: {state.HitCount}\n\n"
                + lastHit,
            forceRedraw: true
        );
    }
}
