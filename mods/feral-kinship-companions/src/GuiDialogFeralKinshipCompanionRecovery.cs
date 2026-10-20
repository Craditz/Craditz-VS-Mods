#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;

namespace FeralKinshipCompanions;

/// <summary>
/// Creative/admin-only list of persistent companion records that can be
/// materialized again when their world entity was unloaded or deleted.
/// </summary>
public sealed class GuiDialogFeralKinshipCompanionRecovery : GuiDialog
{
    private const double DialogWidth = 990;
    private const double DialogHeight = 620;
    private const double Padding = 14;
    private const double ButtonHeight = 32;
    private const int PageSize = 8;

    private readonly FeralKinshipCompanionSystem system;
    private CompanionRecoveryStatePacket state;
    private int page;
    private string localMessage = string.Empty;
    private GuiDialogFeralKinshipRecoveryConfirmation statusDetails;

    public GuiDialogFeralKinshipCompanionRecovery(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system)
        : base(capi)
    {
        this.system = system;
    }

    public override string ToggleKeyCombinationCode => null;

    public override void OnGuiOpened()
    {
        ComposeDialog();
        base.OnGuiOpened();
        UpdateDisplay();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
    }

    public void ApplyState(CompanionRecoveryStatePacket packet)
    {
        state = packet;
        localMessage = packet.Message ?? string.Empty;
        int pageCount = GetPageCount();
        page = pageCount == 0 ? 0 : Math.Min(page, pageCount - 1);
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
            .CreateCompo("feralkinship-companion-recovery", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Companion — Recovery Ledger", () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddStaticText(
            FitLine("Preview native recovery. Missing or unverified originals stay protected; use Status details for full reports.", 710),
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, 12, 720, 22));
        composer.AddDynamicText(
            "Reading the pack ledger...",
            CairoFont.WhiteMediumText(),
            ElementBounds.Fixed(0, 40, 720, 28),
            "summary");
        composer.AddDynamicText(
            string.Empty,
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, 70, 720, 28),
            "message");

        for (int index = 0; index < PageSize; index++)
        {
            double y = 108 + index * 48;
            composer.AddDynamicText(
                string.Empty,
                CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(0, y, 480, 42),
                $"row-{index}");
            int slot = index;
            composer.AddButton(
                "Normal Rebuild",
                () => RecoverAt(slot),
                ElementBounds.Fixed(500, y + 2, 140, ButtonHeight),
                EnumButtonStyle.Normal,
                $"recover-{index}");
            if (system.CanUseEmergencyAdminClient()) {
                composer.AddButton("Emergency / details", () => EmergencyAt(slot, false), ElementBounds.Fixed(650, y + 2, 170, ButtonHeight), EnumButtonStyle.Normal, $"emergency-{index}");
                composer.AddButton("Undo / resume", () => EmergencyAt(slot, true), ElementBounds.Fixed(830, y + 2, 130, ButtonHeight), EnumButtonStyle.Normal, $"undo-emergency-{index}");
            }
        }

        composer.AddButton(
            "Previous",
            OnPreviousPage,
            ElementBounds.Fixed(0, 510, 120, ButtonHeight),
            EnumButtonStyle.Normal,
            "previous");
        composer.AddButton(
            "Next",
            OnNextPage,
            ElementBounds.Fixed(130, 510, 120, ButtonHeight),
            EnumButtonStyle.Normal,
            "next");
        composer.AddButton(
            "Refresh",
            OnRefresh,
            ElementBounds.Fixed(270, 510, 120, ButtonHeight),
            EnumButtonStyle.Normal,
            "refresh");
        composer.AddButton(
            "Recover all",
            OnRecoverAll,
            ElementBounds.Fixed(400, 510, 160, ButtonHeight),
            EnumButtonStyle.Normal,
            "recover-all");
        composer.AddButton(
            "Close",
            () => TryClose(),
            ElementBounds.Fixed(570, 510, 150, ButtonHeight),
            EnumButtonStyle.Normal,
            "close");
        composer.AddButton("Status details", ShowStatusDetails, ElementBounds.Fixed(740, 510, 220, ButtonHeight), EnumButtonStyle.Normal, "status-details");
        composer.EndChildElements();

        SingleComposer = composer.Compose();
    }

    private void UpdateDisplay()
    {
        if (SingleComposer == null)
        {
            return;
        }

        List<CompanionRecoveryEntryPacket> records = state?.Records ?? new List<CompanionRecoveryEntryPacket>();
        int pageCount = GetPageCount();
        int first = page * PageSize;
        SingleComposer.GetDynamicText("summary").SetNewText(
            state == null
                ? "Reading the pack ledger..."
                : records.Count == 0
                    ? "No active companion records found."
                    : $"{records.Count} companion record{(records.Count == 1 ? string.Empty : "s")} · page {page + 1}/{Math.Max(1, pageCount)}",
            forceRedraw: true);
        SingleComposer.GetDynamicText("message").SetNewText(
            FitLine(string.IsNullOrWhiteSpace(localMessage)
                ? "Select Normal Rebuild, or review the admin Emergency option and its persistent Undo checkpoint."
                : localMessage, 710),
            forceRedraw: true);

        for (int index = 0; index < PageSize; index++)
        {
            CompanionRecoveryEntryPacket entry = first + index < records.Count
                ? records[first + index]
                : null;
            SingleComposer.GetDynamicText($"row-{index}").SetNewText(
                entry == null ? string.Empty : FormatEntry(entry),
                forceRedraw: true);
            SingleComposer.GetButton($"recover-{index}").SetActive(entry?.CanRecover == true);
            if (system.CanUseEmergencyAdminClient()) {
                SingleComposer.GetButton($"emergency-{index}")?.SetActive(entry?.CanEmergencyRebuild == true);
                SingleComposer.GetButton($"undo-emergency-{index}")?.SetActive(entry?.CanEmergencyUndo == true);
            }
        }

        SingleComposer.GetButton("previous").SetActive(page > 0);
        SingleComposer.GetButton("next").SetActive(page + 1 < pageCount);
        SingleComposer.GetButton("recover-all").SetActive(
            records.Any(entry => entry.CanRecover));
    }

    private string FormatEntry(CompanionRecoveryEntryPacket entry)
    {
        string label = $"#{entry.Number} {Trim(entry.Name, 28)} · {Trim(entry.SpeciesDisplayName, 18)} · {entry.Status}";
        string detail = entry.EntityLoaded
            ? entry.CanNuclearRebuild
                ? "Currently present — normal Rebuild can replace it"
                : "Already present in the world"
            : entry.CanRecover
                ? $"Saved type: {Trim(entry.EntityCode, 46)}"
                : entry.CanNuclearRebuild
                    ? $"{Trim(entry.BlockedReason, 58)} — normal Rebuild available"
                    : Trim(entry.BlockedReason, 82);
        return $"{FitLine(label, 460)}\n{FitLine(detail, 460)}";
    }

    private string FitLine(string text, double width)
    {
        var lines = new TextDrawUtil().Lineize(CairoFont.WhiteSmallText(), text.Replace('\n', ' '), width * Vintagestory.API.Config.RuntimeEnv.GUIScale, EnumLinebreakBehavior.AfterCharacter);
        return lines.Length <= 1 ? text.Replace('\n', ' ') : lines[0].Text.TrimEnd() + "…";
    }
    private bool ShowStatusDetails()
    {
        statusDetails?.TryClose();
        var packet = new CompanionRecoveryStatePacket { PreviewTitle = "Recovery status details", Message = localMessage };
        foreach (var row in state?.Records ?? new List<CompanionRecoveryEntryPacket>())
            packet.PreviewEntries.Add(new CompanionRecoveryPreviewEntry { Name = row.Name, Decision = row.Status,
                Reason = "Species: " + row.SpeciesDisplayName + "\nSaved code: " + row.EntityCode + "\n" + row.BlockedReason });
        statusDetails = new GuiDialogFeralKinshipRecoveryConfirmation(capi, system, packet);
        statusDetails.TryOpen(); return true;
    }

    private bool EmergencyAt(int slot, bool undo)
    {
        var entry = GetVisibleEntry(slot);
        if (!system.CanUseEmergencyAdminClient() || entry == null || (undo ? !entry.CanEmergencyUndo : !entry.CanEmergencyRebuild)) return true;
        system.SendCompanionRecoveryAction(undo ? CompanionRecoveryRequestPacket.EmergencyUndo : CompanionRecoveryRequestPacket.EmergencyRebuild, entry.FoxId);
        return true;
    }

    private bool RecoverAt(int slot)
    {
        CompanionRecoveryEntryPacket entry = GetVisibleEntry(slot);
        if (entry == null || !entry.CanRecover)
        {
            return true;
        }

        localMessage = $"Requesting recovery for {entry.Name}...";
        UpdateDisplay();
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Recover, entry.FoxId);
        return true;
    }

    private bool NuclearAt(int slot)
    {
        CompanionRecoveryEntryPacket entry = GetVisibleEntry(slot);
        if (entry == null || !entry.CanNuclearRebuild) return true;
        localMessage = "Requesting a server recovery preview; no animal changes until confirmation.";
        UpdateDisplay();
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.NuclearRebuild, entry.FoxId);
        return true;
    }

    private bool OnPreviousPage()
    {
        if (page > 0)
        {
            page--;
            UpdateDisplay();
        }
        return true;
    }

    private bool OnNextPage()
    {
        if (page + 1 < GetPageCount())
        {
            page++;
            UpdateDisplay();
        }
        return true;
    }

    private bool OnRefresh()
    {
        localMessage = "Refreshing the pack ledger...";
        UpdateDisplay();
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Open);
        return true;
    }

    private bool OnRecoverAll()
    {
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.RecoverAll);
        return true;
    }

    private int GetPageCount()
    {
        int count = state?.Records?.Count ?? 0;
        return count == 0 ? 0 : (count + PageSize - 1) / PageSize;
    }

    private CompanionRecoveryEntryPacket GetVisibleEntry(int slot)
    {
        int index = page * PageSize + slot;
        return state?.Records != null && index >= 0 && index < state.Records.Count
            ? state.Records[index]
            : null;
    }

    private static string Trim(string value, int maximum)
    {
        value ??= string.Empty;
        return value.Length <= maximum ? value : value.Substring(0, maximum - 1) + "…";
    }
}
