using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    // Loading a distant saved column is only discovery, not usable recovery.
    // Transfer the same native object into a player-loaded chunk before success.
    // Never pin remote columns forever or unload columns owned by other systems.
    private void BringRestoredOriginalToOwner(IServerPlayer owner, FoxPackRecordV2 r, Entity original, string key)
    {
        bool finished = false;
        void Finish(string message)
        {
            if (finished) return;
            finished = true;
            archiveOriginalLookups.Remove(key);
            LogRecoveryAttempt(r.FoxId, "user-restore", message);
            SendPackState(owner, message);
        }
        bool Authorized() => serverApi != null && owner.ConnectionState == EnumClientState.Playing
            && owner.Entity != null && packRepository?.TryGetRecord(r.FoxId, out var current) == true
            && ReferenceEquals(current, r) && r.OwnerUid == owner.PlayerUID
            && GetCompanionOwnerUid(original) == owner.PlayerUID;

        if (!Authorized()) { Finish("Animal restoration stopped: owner or player availability changed. Original and cargo preserved."); return; }
        if (original.Pos.Dimension != owner.Entity.Pos.Dimension) { Finish("Animal restoration refused: the original is in another dimension. Visit that dimension before restoring it."); return; }
        string blocker = EmergencyBodyBlocker(original);
        if (blocker.Length > 0 || NativeLinkPending(r) || EmergencyPending(LastEmergency(r)))
        { Finish("Animal restoration deferred: " + (blocker.Length > 0 ? blocker : "another recovery or Undo is pending") + ". Original and cargo preserved."); return; }
        Vec3d? destination = FindSafeEntityPosition(owner.Entity.Pos.AsBlockPos, original.Properties, original, 6, 3);
        if (destination == null) { Finish("Animal restoration could not find safe open space near you. Move to open ground and retry; original and cargo preserved."); return; }
        long destinationChunk = serverApi!.WorldManager.ChunkIndex3D(
            (int)Math.Floor(destination.X / GlobalConstants.ChunkSize),
            (int)Math.Floor(destination.Y / GlobalConstants.ChunkSize),
            (int)Math.Floor(destination.Z / GlobalConstants.ChunkSize));
        if (serverApi.WorldManager.GetChunk(destinationChunk) is not { Disposed: false })
        { Finish("Animal restoration deferred: safe destination chunk is not loaded. Original and cargo preserved."); return; }
        if (!TryRestoreArchiveOriginal(original, r, out string admission)) { Finish("Animal restoration failed: " + admission); return; }
        SetFoxAwayFromWorld(original, false);

        int checks = 0;
        bool moved = false;
        void CheckArrival()
        {
            if (finished) return;
            try {
            if (!Authorized()) { Finish("Animal restoration stopped: owner or player availability changed. Refresh to check the original; no replacement was created."); return; }
            bool published = NativeRecoveryPublished(original);
            if (moved && !original.Teleporting && published && original.InChunkIndex3d == destinationChunk
                && original.Pos.Dimension == owner.Entity.Pos.Dimension && original.Pos.SquareDistanceTo(owner.Entity.Pos.XYZ) <= 256
                && ReferenceEquals(FindArchiveOriginal(r, out _), original) && original.Alive && !original.ShouldDespawn
                && !IsFoxAwayFromWorld(original) && original.CollisionBox?.XSize > 0 && original.SelectionBox?.XSize > 0)
            {
                // Refresh the social load latch and register under the actual
                // native ID. This is also the ordinary cart command's lookup.
                SetFoxAwayFromWorld(original, false);
                original.State = EnumEntityState.Active;
                RegisterLoadedFox(original);
                if (ReferenceEquals(FindLoadedCompanionByFoxId(r.FoxId), original)
                    && original.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.HasExpeditionPresentationSuppression != true)
                {
                    r.HasLastKnownPosition = true; r.LastKnownX = (int)Math.Floor(original.Pos.X); r.LastKnownY = (int)Math.Floor(original.Pos.Y);
                    r.LastKnownZ = (int)Math.Floor(original.Pos.Z); r.LastKnownDimension = original.Pos.Dimension;
                    packRepository!.Save();
                    Finish(admission + " Original is loaded near you and available for commands.");
                    return;
                }
            }
            if (++checks >= 30) { Finish("Animal restoration could not verify a usable arrival. Refresh to check the original before retrying; no replacement or cargo copy was created."); return; }
            serverApi!.World.RegisterCallback(_ => CheckArrival(), 250);
            } catch(Exception ex) { Finish("Animal restoration could not verify arrival (" + ex.GetType().Name + "). Original and cargo preserved; refresh before retrying."); }
        }
        try
        {
            ClearAutomaticRetreat(original); CancelTargetedAttack(original);
            if (original is EntityAgent agent)
            {
                agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                agent.Controls.StopAllMovement(); agent.Pos.Motion.Set(0, 0, 0);
            }
            SendPackState(owner, "Original found; bringing it near you and checking arrival. Restoration is not complete yet.");
            original.TeleportToDouble(destination.X, destination.Y, destination.Z, () =>
            {
                if (finished || !Authorized()) return;
                // Native TeleportToDouble changes Pos before its callback but
                // physics normally migrates chunk membership on a later tick.
                // Do that transfer now, while the temporary source is loaded.
                try { serverApi!.World.UpdateEntityChunk(original, destinationChunk); moved = true; }
                catch(Exception ex) { Finish("Animal restoration could not register arrival (" + ex.GetType().Name + "). Original and cargo preserved; refresh before retrying."); }
            });
            // Native Teleporting is cleared AFTER its callback. Verify later,
            // including native index and destination chunk membership.
            serverApi!.World.RegisterCallback(_ => CheckArrival(), 250);
        }
        catch (Exception ex) { Finish("Animal restoration could not complete arrival (" + ex.GetType().Name + "). Refresh to check the original; original and cargo preserved."); }
    }
}
