#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;

public sealed class GuiDialogFeralKinshipFoxDeveloper : GuiDialog
{
    private const double DialogWidth = 680;
    private const double DialogHeight = 780;
    private const double Padding = 16;
    private const double ButtonHeight = 30;
    private const int SelectionPageSize = 9;

    private readonly FeralKinshipCompanionSystem system;
    private readonly long targetEntityId;
    private readonly string targetFoxId;
    private FoxSocialStatePacket state;
    private CompanionRecoveryStatePacket recoveryState;
    private string selectedJob = string.Empty;
    private string selectedMood = string.Empty;
    private long stateReceivedAtMs;
    private long lastLocalRefreshAtMs;
    private bool confirmDeleteAll;
    private bool confirmRebuild;
    private bool? confirmScavengeReroll;
    private string localMessage = string.Empty;
    private string selectionMessage = string.Empty;
    private int selectionPage;

    private bool IsSelectionMode => targetEntityId == 0 && string.IsNullOrWhiteSpace(targetFoxId);

    private static readonly string[] JobValues =
    {
        "",
        FoxRequestType.Predator,
        FoxRequestType.StayClose,
        FoxRequestType.StayAway,
        FoxRequestType.Inside,
        FoxRequestType.NearOwnerStill,
        FoxRequestType.TravelDistance,
        FoxRequestType.HigherGround,
        FoxRequestType.NearWater,
        FoxRequestType.NearLight,
        FoxRequestType.NearHeat,
        FoxRequestType.Shelter,
        FoxRequestType.OutsideClear,
        FoxRequestType.NearTamedAnimal,
        FoxRequestType.LargeTree,
        FoxRequestType.CropField,
        FoxRequestType.Trader,
        FoxRequestType.MechanicalDevice,
        FoxRequestType.AnotherAnimal,
        FoxRequestType.RegularFood,
        FoxRequestType.LuxuryFood,
        FoxRequestType.NonFoodConsumed,
        FoxRequestType.NonFoodNearby,
        FoxRequestType.HealingItem,
        FoxRequestType.PlaceableNearby,
        FoxRequestType.OutsideUntilMorning,
        FoxRequestType.InsideUntilMorning
    };

    private static readonly string[] JobNames =
    {
        "Random (normal logic)",
        "Predator nearby",
        "Stay close",
        "Stay away",
        "Inside",
        "Near owner while owner is still",
        "Travel 16 blocks from request start",
        "Reach higher ground",
        "Move near water",
        "Near a light source",
        "Near a heat source",
        "Under shelter during rain/snow",
        "Outside in clear weather",
        "Near another tamed animal",
        "Visit a large tree",
        "Visit a crop field",
        "Visit a trader",
        "Visit a mechanical device",
        "Visit another animal",
        "[EXPERIMENTAL] Hand over regular food",
        "[EXPERIMENTAL] Hand over luxury food",
        "[EXPERIMENTAL] Hand over non-food item",
        "[EXPERIMENTAL] Show non-food item (kept)",
        "[EXPERIMENTAL] Hand over healing item",
        "[EXPERIMENTAL] Place exact block nearby",
        "[EXPERIMENTAL] Stay outside until morning",
        "[EXPERIMENTAL] Stay inside until morning"
    };

    private static readonly string[] MoodValues =
    {
        "", "calm", "content", "curious", "restless", "playful", "sleepy", "alert", "anxious",
        "resting", "rested", "social", "happy", "recovered", "refreshed", "relieved"
    };

    private static readonly string[] MoodNames =
    {
        "Normal random behavior", "Calm", "Content", "Curious", "Restless", "Playful", "Sleepy", "Alert", "Anxious",
        "Resting", "Rested", "Social", "Happy", "Recovered", "Refreshed", "Relieved"
    };

    public GuiDialogFeralKinshipFoxDeveloper(ICoreClientAPI capi, FeralKinshipCompanionSystem system, long targetEntityId)
        : this(capi, system, targetEntityId, string.Empty)
    {
    }

    public GuiDialogFeralKinshipFoxDeveloper(
        ICoreClientAPI capi,
        FeralKinshipCompanionSystem system,
        long targetEntityId,
        string targetFoxId)
        : base(capi)
    {
        this.system = system;
        this.targetEntityId = targetEntityId;
        this.targetFoxId = targetFoxId ?? string.Empty;
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

    public override void OnGuiClosed()
    {
        if (!IsSelectionMode)
        {
            system.SendFoxSocialAction(
                targetEntityId,
                FoxSocialRequestAction.CloseView,
                developerFoxId: targetFoxId);
        }
        base.OnGuiClosed();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        long nowMs = capi.World.ElapsedMilliseconds;
        if (state != null && nowMs - lastLocalRefreshAtMs >= 500)
        {
            lastLocalRefreshAtMs = nowMs;
            UpdateDisplay();
        }
    }

    public void ApplyState(FoxSocialStatePacket packet)
    {
        bool matchingStableId = !string.IsNullOrWhiteSpace(targetFoxId)
            && string.Equals(packet.FoxId, targetFoxId, StringComparison.Ordinal);
        if (!matchingStableId && packet.TargetEntityId != targetEntityId)
        {
            return;
        }

        state = packet;
        confirmDeleteAll = false;
        confirmRebuild = false;
        localMessage = string.Empty;
        stateReceivedAtMs = capi.World.ElapsedMilliseconds;
        if (SingleComposer != null)
        {
            UpdateDisplay();
        }
    }

    public void ApplyRecoveryState(CompanionRecoveryStatePacket packet)
    {
        if (!IsSelectionMode)
        {
            return;
        }

        recoveryState = packet;
        confirmScavengeReroll = null;
        selectionMessage = string.Empty;
        if (SingleComposer != null)
        {
            ComposeDialog();
        }
    }

    private void ComposeDialog()
    {
        ElementBounds contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, DialogHeight).WithFixedPadding(Padding);
        contentBounds.BothSizing = ElementSizing.Fixed;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        GuiComposer composer = capi.Gui
            .CreateCompo("feralkinship-fox-developer", dialogBounds)
            .AddShadedDialogBG(contentBounds)
            .AddDialogTitleBar("Feral Kinship Companion — Developer Ledger", () => TryClose())
            .BeginChildElements(contentBounds);

        if (IsSelectionMode)
        {
            ComposeSelectionDialog(composer);
            composer.EndChildElements();
            SingleComposer = composer.Compose();
            UpdateSelectionButtons();
            return;
        }

        CairoFont sectionFont = CairoFont.WhiteSmallishText().WithColor(GuiStyle.ActiveButtonTextColor);
        CairoFont dangerFont = CairoFont.WhiteSmallText().WithColor(GuiStyle.ErrorTextColor);

        composer.AddDynamicText("Loading selected Companion...", CairoFont.WhiteMediumText(), ElementBounds.Fixed(0, 12, 430, 28), "number");
        composer.AddSmallButton("Animal list", OnBackToSocial, ElementBounds.Fixed(476, 8, 164, ButtonHeight), EnumButtonStyle.Normal, "back");

        composer.AddInset(ElementBounds.Fixed(0, 44, 640, 114), 2, 0.72f);
        composer.AddDynamicText("Personality: Unassigned  •  Mood: Unassigned", CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 52, 616, 22), "personality");
        composer.AddDynamicText("Requests 0/0  •  Level 1  •  EXP 0/40  •  Points 0", CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 76, 616, 22), "counters");
        composer.AddDynamicText("Active request: None", CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 100, 616, 28), "active");
        composer.AddDynamicText("Next: Ready  •  Cancel: Ready  •  Last: None", CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 130, 616, 22), "cooldowns");

        composer.AddInset(ElementBounds.Fixed(0, 166, 640, 68), 2, 0.64f);
        composer.AddDynamicText("Waiting for the server...", CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 175, 616, 52), "message");

        composer.AddStaticText("REQUEST TESTING", sectionFont, ElementBounds.Fixed(0, 244, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 264, 640, 88), 2, 0.78f);
        composer.AddDropDown(JobValues, JobNames, 0, OnJobSelected, ElementBounds.Fixed(10, 272, 620, 28), CairoFont.WhiteSmallText(), "job");
        composer.AddSmallButton("Generate request", OnForceTask, ElementBounds.Fixed(10, 310, 196, ButtonHeight), EnumButtonStyle.Normal, "force-task")
            .AddSmallButton("Clear active request", OnCancelTask, ElementBounds.Fixed(216, 310, 196, ButtonHeight), EnumButtonStyle.Normal, "cancel-task")
            .AddSmallButton("Reset cooldowns", OnResetCooldowns, ElementBounds.Fixed(422, 310, 208, ButtonHeight), EnumButtonStyle.Normal, "reset-cooldowns");

        composer.AddStaticText("MOOD TESTING · FIVE MINUTES", sectionFont, ElementBounds.Fixed(0, 362, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 382, 640, 70), 2, 0.78f);
        composer.AddDropDown(MoodValues, MoodNames, 0, OnMoodSelected, ElementBounds.Fixed(10, 390, 414, 28), CairoFont.WhiteSmallText(), "mood");
        composer.AddSmallButton("Apply mood", OnForceMood, ElementBounds.Fixed(434, 388, 196, ButtonHeight), EnumButtonStyle.Normal, "force-mood");

        composer.AddStaticText("DIAGNOSTICS", sectionFont, ElementBounds.Fixed(0, 462, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 482, 640, 108), 2, 0.78f);
        composer.AddSmallButton("Companion point +1", OnAddFoxPoint, ElementBounds.Fixed(10, 490, 145, ButtonHeight), EnumButtonStyle.Normal, "fox-point-add")
            .AddSmallButton("Companion point −1", OnRemoveFoxPoint, ElementBounds.Fixed(165, 490, 145, ButtonHeight), EnumButtonStyle.Normal, "fox-point-remove")
            .AddSmallButton("Pack point +1", OnAddPackPoint, ElementBounds.Fixed(320, 490, 145, ButtonHeight), EnumButtonStyle.Normal, "pack-point-add")
            .AddSmallButton("Pack point −1", OnRemovePackPoint, ElementBounds.Fixed(475, 490, 155, ButtonHeight), EnumButtonStyle.Normal, "pack-point-remove");
        composer.AddSmallButton("Add 100 EXP", OnAddExperience, ElementBounds.Fixed(10, 526, 196, ButtonHeight), EnumButtonStyle.Normal, "exp-add")
            .AddSmallButton("Advance one level", OnAdvanceLevel, ElementBounds.Fixed(216, 526, 196, ButtonHeight), EnumButtonStyle.Normal, "exp-level")
            .AddSmallButton("Set health to 1", OnSetHealthOne, ElementBounds.Fixed(422, 526, 208, ButtonHeight), EnumButtonStyle.Normal, "health-one");
        composer.AddSmallButton("Tell a joke", OnTellBadJoke, ElementBounds.Fixed(10, 562, 196, 22), EnumButtonStyle.Small, "bad-fox-joke");

        composer.AddStaticText("RECOVERY", sectionFont, ElementBounds.Fixed(0, 600, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 620, 640, 48), 2, 0.78f);
        composer.AddSmallButton("Teleport existing animal", OnTeleportCompanion, ElementBounds.Fixed(10, 628, 300, ButtonHeight), EnumButtonStyle.Normal, "teleport-companion")
            .AddSmallButton("Rebuild from ledger record", OnRebuildCompanion, ElementBounds.Fixed(320, 628, 310, ButtonHeight), EnumButtonStyle.Normal, "rebuild-companion");

        composer.AddStaticText("DANGER ZONE", dangerFont, ElementBounds.Fixed(0, 678, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 700, 640, 50), 2, 0.58f);
        composer.AddButton(
            "Permanently delete every Companion record",
            OnDeleteAllCompanions,
            ElementBounds.Fixed(10, 709, 620, ButtonHeight),
            CairoFont.SmallButtonText().WithColor(GuiStyle.ErrorTextColor),
            EnumButtonStyle.Normal,
            "delete-all-companions");
        composer.EndChildElements();

        SingleComposer = composer.Compose();
        UpdateButtonStates();
    }

    private void ComposeSelectionDialog(GuiComposer composer)
    {
        CairoFont sectionFont = CairoFont.WhiteSmallishText().WithColor(GuiStyle.ActiveButtonTextColor);
        composer.AddStaticText("Select an animal", CairoFont.WhiteMediumText(), ElementBounds.Fixed(0, 16, 640, 28));
        composer.AddStaticText(
            "Choose from your persistent pack ledger. Distance does not limit selection; the server checks the saved location after you choose.",
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, 50, 640, 40));

        List<CompanionRecoveryEntryPacket> allRecords = recoveryState?.Records?
            .Where(record => !string.IsNullOrWhiteSpace(record.FoxId))
            .OrderByDescending(record => record.EntityLoaded)
            .ThenBy(record => record.Number)
            .ToList()
            ?? new List<CompanionRecoveryEntryPacket>();
        int pageCount = Math.Max(1, (int)Math.Ceiling(allRecords.Count / (double)SelectionPageSize));
        selectionPage = Math.Clamp(selectionPage, 0, pageCount - 1);
        List<CompanionRecoveryEntryPacket> records = allRecords
            .Skip(selectionPage * SelectionPageSize)
            .Take(SelectionPageSize)
            .ToList();

        composer.AddStaticText(
            recoveryState == null
                ? "PACK LEDGER · LOADING"
                : $"PACK LEDGER · {allRecords.Count} ACTIVE · PAGE {selectionPage + 1}/{pageCount}",
            sectionFont,
            ElementBounds.Fixed(0, 96, 640, 22));
        composer.AddInset(ElementBounds.Fixed(0, 116, 640, 364), 2, 0.72f);

        int y = 124;
        if (records.Count == 0)
        {
            composer.AddStaticText(
                recoveryState == null
                    ? "Reading your Companion ledger records from the server..."
                    : "No active Companion records were found.",
                CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(14, y + 12, 612, 42));
        }
        else
        {
            for (int index = 0; index < records.Count; index++)
            {
                CompanionRecoveryEntryPacket record = records[index];
                string loaded = record.EntityLoaded ? "IN WORLD" : "SAVED";
                string species = string.IsNullOrWhiteSpace(record.SpeciesDisplayName)
                    ? "Companion"
                    : record.SpeciesDisplayName;
                string label = $"#{record.Number}  {Trim(record.Name, 28)}   •   {Trim(species, 18)}   •   {loaded}";
                string foxId = record.FoxId;
                long entityId = record.EntityId;
                int slot = index;
                composer.AddSmallButton(
                    label,
                    () => OnSelectRecord(foxId, entityId),
                    ElementBounds.Fixed(10, y, 620, 32),
                    EnumButtonStyle.Normal,
                    $"select-animal-{selectionPage}-{slot}");
                y += 38;
            }
        }

        composer.AddSmallButton("Previous", OnPreviousSelectionPage, ElementBounds.Fixed(0, 494, 136, ButtonHeight), EnumButtonStyle.Normal, "previous-selection")
            .AddSmallButton("Next", OnNextSelectionPage, ElementBounds.Fixed(146, 494, 136, ButtonHeight), EnumButtonStyle.Normal, "next-selection")
            .AddSmallButton("Refresh", OnRefreshSelection, ElementBounds.Fixed(292, 494, 166, ButtonHeight), EnumButtonStyle.Normal, "refresh-selection")
            .AddSmallButton("Close", TryClose, ElementBounds.Fixed(468, 494, 172, ButtonHeight), EnumButtonStyle.Normal, "close-selection");

        composer.AddInset(ElementBounds.Fixed(0, 540, 640, 54), 2, 0.64f);
        composer.AddStaticText(
            !string.IsNullOrWhiteSpace(selectionMessage)
                ? Trim(selectionMessage, 100)
                : string.IsNullOrWhiteSpace(recoveryState?.Message)
                ? "Saved entries open immediately. Live-only tools unlock if the original entity is found."
                : Trim(recoveryState.Message, 100),
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(12, 550, 616, 38));

        composer.AddStaticText("SCAVENGE SITE TESTING", sectionFont, ElementBounds.Fixed(0, 610, 640, 22));
        composer.AddStaticText(
            "Replaces every remembered site and its search progress. Active Scout or Scavenge parties block rerolls.",
            CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 635, 640, 38));
        composer.AddSmallButton(
                confirmScavengeReroll == false ? "CONFIRM unknown reroll" : "Reroll 3 unknown sites",
                () => OnRerollScavengeSites(false),
                ElementBounds.Fixed(0, 687, 310, ButtonHeight), EnumButtonStyle.Normal,
                "reroll-sites-unknown")
            .AddSmallButton(
                confirmScavengeReroll == true ? "CONFIRM surveyed reroll" : "Reroll 3 fully scouted",
                () => OnRerollScavengeSites(true),
                ElementBounds.Fixed(320, 687, 320, ButtonHeight), EnumButtonStyle.Normal,
                "reroll-sites-surveyed");
    }

    private bool OnSelectRecord(string foxId, long entityId)
    {
        if (system.TrySelectFoxDeveloperRecord(foxId, entityId))
        {
            return true;
        }

        capi.ShowChatMessage("That Companion record is no longer available. Refresh the selection list.");
        return true;
    }

    private bool OnRefreshSelection()
    {
        recoveryState = null;
        confirmScavengeReroll = null;
        selectionMessage = string.Empty;
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Open);
        ComposeDialog();
        return true;
    }

    private bool OnRerollScavengeSites(bool surveyed)
    {
        if (recoveryState == null) return true;
        if (confirmScavengeReroll != surveyed)
        {
            confirmScavengeReroll = surveyed;
            selectionMessage = "This replaces all remembered sites and their search progress. Click the same button again to confirm.";
            ComposeDialog();
            return true;
        }

        confirmScavengeReroll = null;
        selectionMessage = "Generating three replacement sites...";
        system.SendCompanionRecoveryAction(surveyed
            ? CompanionRecoveryRequestPacket.RerollScavengeSitesSurveyed
            : CompanionRecoveryRequestPacket.RerollScavengeSites);
        ComposeDialog();
        return true;
    }

    private bool OnPreviousSelectionPage()
    {
        if (selectionPage > 0)
        {
            selectionPage--;
            ComposeDialog();
        }
        return true;
    }

    private bool OnNextSelectionPage()
    {
        int count = recoveryState?.Records?.Count(record => !string.IsNullOrWhiteSpace(record.FoxId)) ?? 0;
        if ((selectionPage + 1) * SelectionPageSize < count)
        {
            selectionPage++;
            ComposeDialog();
        }
        return true;
    }

    private bool OnForceTask()
    {
        SendDeveloperAction(FoxSocialRequestAction.GenerateDeveloper, selectedJob);
        return true;
    }

    private bool OnCancelTask()
    {
        SendDeveloperAction(FoxSocialRequestAction.CancelDeveloper);
        return true;
    }

    private bool OnResetCooldowns()
    {
        SendDeveloperAction(FoxSocialRequestAction.ResetCooldownsDeveloper);
        return true;
    }

    private bool OnForceMood()
    {
        SendDeveloperAction(FoxSocialRequestAction.SetMoodDeveloper, selectedMood);
        return true;
    }

    private bool OnAddFoxPoint()
    {
        SendDeveloperAction(FoxSocialRequestAction.AddFoxPointDeveloper);
        return true;
    }

    private bool OnRemoveFoxPoint()
    {
        SendDeveloperAction(FoxSocialRequestAction.RemoveFoxPointDeveloper);
        return true;
    }

    private bool OnAddPackPoint()
    {
        SendDeveloperAction(FoxSocialRequestAction.AddPackPointDeveloper);
        return true;
    }

    private bool OnRemovePackPoint()
    {
        SendDeveloperAction(FoxSocialRequestAction.RemovePackPointDeveloper);
        return true;
    }

    private bool OnSetHealthOne()
    {
        SendDeveloperAction(FoxSocialRequestAction.SetHealthOneDeveloper);
        return true;
    }

    private bool OnAddExperience()
    {
        SendDeveloperAction(FoxSocialRequestAction.AddExperienceDeveloper);
        return true;
    }

    private bool OnAdvanceLevel()
    {
        SendDeveloperAction(FoxSocialRequestAction.AdvanceLevelDeveloper);
        return true;
    }

    private bool OnRebuildCompanion()
    {
        string foxId = GetSelectedFoxId();
        if (string.IsNullOrWhiteSpace(foxId))
        {
            SetLocalMessage("The selected Companion has no stable ledger ID.");
            return true;
        }

        if (!confirmRebuild)
        {
            confirmRebuild = true;
            confirmDeleteAll = false;
            SetLocalMessage("Rebuild replaces this world entity from its saved ledger record. Click Rebuild again to confirm.");
            return true;
        }

        confirmRebuild = false;
        SetLocalMessage("Rebuilding the selected Companion from its ledger record...");
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.NuclearRebuild, foxId);
        return true;
    }

    private bool OnTeleportCompanion()
    {
        string foxId = GetSelectedFoxId();
        if (string.IsNullOrWhiteSpace(foxId))
        {
            SetLocalMessage("The selected Companion has no stable ledger ID.");
            return true;
        }

        confirmRebuild = false;
        confirmDeleteAll = false;
        SetLocalMessage("Loading the original entity and requesting a safe teleport...");
        system.SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Teleport, foxId);
        return true;
    }

    private bool OnTellBadJoke()
    {
        SendDeveloperAction(FoxSocialRequestAction.TellBadJokeDeveloper);
        return true;
    }

    private bool OnDeleteAllCompanions()
    {
        if (state == null)
        {
            capi.ShowChatMessage("The selected Companion is still loading. Try again in a moment.");
            return true;
        }

        int count = Math.Max(0, state?.OwnedCompanionCount ?? 0);
        if (count == 0)
        {
            localMessage = "You have no companion records to delete.";
            UpdateDisplay();
            return true;
        }

        if (!confirmDeleteAll)
        {
            confirmDeleteAll = true;
            confirmRebuild = false;
            localMessage = $"Delete all {count} companions permanently, with no recovery. Click again to confirm.";
            UpdateDisplay();
            return true;
        }

        confirmDeleteAll = false;
        SendDeveloperAction(FoxSocialRequestAction.DeleteAllCompanionsDeveloper);
        TryClose();
        return true;
    }

    private bool OnBackToSocial()
    {
        system.TryOpenFoxDeveloperGui();
        return true;
    }

    private void SendDeveloperAction(int action, string requestedJob = "")
    {
        if (state == null)
        {
            SetLocalMessage("Waiting for the selected Companion's ledger record...");
            return;
        }

        if (!state.DeveloperEntityLoaded
            && action != FoxSocialRequestAction.DeleteAllCompanionsDeveloper)
        {
            SetLocalMessage("That action needs the original world entity. Use Teleport to load and move it, or Rebuild to replace it from the record.");
            return;
        }

        confirmRebuild = false;
        confirmDeleteAll = false;
        system.SendFoxSocialAction(
            targetEntityId,
            action,
            requestedJob,
            developerFoxId: targetFoxId);
    }

    private void OnJobSelected(string value, bool selected)
    {
        if (selected)
        {
            selectedJob = value;
        }
    }

    private void OnMoodSelected(string value, bool selected)
    {
        if (selected)
        {
            selectedMood = value;
        }
    }

    private void UpdateDisplay()
    {
        FoxSocialStatePacket current = state;
        if (current == null || SingleComposer == null)
        {
            return;
        }

        UpdateButtonStates();
        float elapsedSeconds = Math.Max(0f, (capi.World.ElapsedMilliseconds - stateReceivedAtMs) / 1000f);
        float requestCooldown = Math.Max(0f, current.RequestCooldownSeconds - elapsedSeconds);
        float cancelCooldown = Math.Max(0f, current.CancelCooldownSeconds - elapsedSeconds);
        bool hasActiveRequest = !string.Equals(current.ActiveRequest, "None", StringComparison.Ordinal);
        string species = string.IsNullOrWhiteSpace(current.SpeciesDisplayName) ? "Companion" : current.SpeciesDisplayName;
        string name = string.IsNullOrWhiteSpace(current.Name) ? species : current.Name;
        string residency = current.DeveloperEntityLoaded ? "IN WORLD" : "SAVED RECORD";
        SingleComposer.GetDynamicText("number").SetNewText(
            $"{Trim(name, 30)}  ·  {species} #{current.Number}  ·  {residency}",
            forceRedraw: true);
        SingleComposer.GetDynamicText("personality").SetNewText(
            $"Personality: {current.Personality}  •  Mood: {current.Mood}",
            forceRedraw: true
        );
        SingleComposer.GetDynamicText("counters").SetNewText(
            $"Requests {current.RequestsGenerated}/{current.RequestsCompleted}  •  Level {current.Level}  •  EXP {current.CurrentLevelExperience}/{current.RequiredLevelExperience}  •  Points {current.Points}",
            forceRedraw: true
        );
        SingleComposer.GetDynamicText("active").SetNewText(
            $"Active: {Trim(current.ActiveRequest, 46)}  •  {FormatProgress(current, elapsedSeconds)}",
            forceRedraw: true
        );
        SingleComposer.GetDynamicText("cooldowns").SetNewText(
            $"Next: {FormatCooldown(requestCooldown)}  •  Cancel: {FormatCooldown(cancelCooldown)}  •  Last: {Trim(current.LastCompleted, 32)}",
            forceRedraw: true
        );
        SingleComposer.GetDynamicText("message").SetNewText(
            Trim(string.IsNullOrWhiteSpace(localMessage) ? current.Message : localMessage, 105),
            forceRedraw: true);
        if (current.DeveloperEntityLoaded)
        {
            SingleComposer.GetButton("force-task").SetActive(!hasActiveRequest);
            SingleComposer.GetButton("cancel-task").SetActive(hasActiveRequest);
        }
    }

    private void UpdateButtonStates()
    {
        if (SingleComposer == null)
        {
            return;
        }

        bool liveReady = state?.DeveloperEntityLoaded == true;
        string[] liveButtonIds =
        {
            "force-task", "cancel-task", "reset-cooldowns", "force-mood",
            "fox-point-add", "fox-point-remove", "pack-point-add", "pack-point-remove",
            "exp-add", "exp-level", "health-one", "bad-fox-joke"
        };
        foreach (string buttonId in liveButtonIds)
        {
            SingleComposer.GetButton(buttonId).SetActive(liveReady);
        }

        bool hasRecord = !string.IsNullOrWhiteSpace(GetSelectedFoxId());
        SingleComposer.GetButton("teleport-companion").SetActive(hasRecord);
        SingleComposer.GetButton("rebuild-companion").SetActive(hasRecord);
        SingleComposer.GetButton("delete-all-companions").SetActive((state?.OwnedCompanionCount ?? 0) > 0);
        SingleComposer.GetButton("back").SetActive(true);
    }

    private void UpdateSelectionButtons()
    {
        if (SingleComposer == null || !IsSelectionMode)
        {
            return;
        }

        int count = recoveryState?.Records?.Count(record => !string.IsNullOrWhiteSpace(record.FoxId)) ?? 0;
        SingleComposer.GetButton("previous-selection").SetActive(selectionPage > 0);
        SingleComposer.GetButton("next-selection").SetActive((selectionPage + 1) * SelectionPageSize < count);
        SingleComposer.GetButton("refresh-selection").SetActive(recoveryState != null);
        SingleComposer.GetButton("reroll-sites-unknown").SetActive(recoveryState != null);
        SingleComposer.GetButton("reroll-sites-surveyed").SetActive(recoveryState != null);
    }

    private string GetSelectedFoxId()
    {
        return !string.IsNullOrWhiteSpace(state?.FoxId)
            ? state.FoxId
            : targetFoxId;
    }

    private void SetLocalMessage(string message)
    {
        localMessage = message ?? string.Empty;
        if (state != null)
        {
            UpdateDisplay();
            return;
        }

        SingleComposer?.GetDynamicText("message")?.SetNewText(
            Trim(localMessage, 105),
            forceRedraw: true);
    }

    private static string FormatCooldown(float seconds)
    {
        if (seconds <= 0.01f)
        {
            return "Ready";
        }

        int totalSeconds = Math.Max(1, (int)Math.Ceiling(seconds));
        return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
    }

    private static string FormatProgress(FoxSocialStatePacket current, float elapsedSeconds)
    {
        float displayedProgress = current.ProgressSeconds > 0f
            ? Math.Min(current.DurationSeconds, current.ProgressSeconds + elapsedSeconds)
            : 0f;
        return current.DurationSeconds > 0f
            ? $"Progress: {Math.Clamp(displayedProgress, 0f, current.DurationSeconds):0} / {current.DurationSeconds:0} seconds"
            : "Progress: Completes when condition is met";
    }

    private static string Trim(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength
            ? value
            : value.Substring(0, Math.Max(0, maxLength - 1)) + "…";
    }
}
