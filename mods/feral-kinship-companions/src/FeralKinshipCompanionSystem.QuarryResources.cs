#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static void SetCartMaterialStatus(BlockEntityFeralKinshipWorkCart cart, string status)
    {
        cart.Quarry.Status = status;
        if (cart.MiningMode) cart.Mining.Status = status;
        cart.MarkDirty(true);
    }

    private static bool QuarryBufferHas(BlockEntityFeralKinshipWorkCart cart, string rock, int count)
        => cart.Tools.Count == 5 && cart.Tools[4].Itemstack?.Collectible?.Code.ToString() == "game:stone-" + rock
            && cart.Tools[4].StackSize >= count;

    private bool QuarryContainerAllowed(Entity fox, BlockPos pos, bool deposit, ItemStack? stack,
        out IInventory? inventory, out BlockEntity? entity)
    {
        inventory = null; entity = null;
        if (serverApi == null || packRepository == null || pos.dimension != fox.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return false;
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", "") ?? "";
        var owner = serverApi.World.PlayerByUid(ownerUid);
        var record = packRepository.GetAmenity(pos);
        if (owner == null || record?.OwnerUid != ownerUid || !IsStorageRoutingEnabled(record)
            || serverApi.World.Claims.TestAccess(owner, pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted
            || (deposit && stack != null && !StorageRoutingAccepts(record, stack))) return false;
        entity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
        if (!FoxStorageRouting.IsSupportedStorageTarget(serverApi.World.BlockAccessor.GetBlock(pos), entity)
            || entity is not IBlockEntityContainer container) return false;
        inventory = container.Inventory;
        return inventory != null;
    }

    private bool TryCreateQuarrySupply(Entity fox, BlockEntityFeralKinshipWorkCart cart, out WorkCartQuarryJob? job)
    {
        job = null;
        if (serverApi == null || packRepository == null) return false;
        string rock = cart.Quarry.PreferredRock;
        if (rock.Length == 0) return false;
        Item? item = serverApi.World.GetItem(new AssetLocation("game:stone-" + rock));
        if (item == null) return false;
        ItemSlot buffer = cart.Tools[4];
        bool different = !buffer.Empty && buffer.Itemstack?.Collectible?.Code.ToString() != item.Code.ToString();
        if (!different && buffer.GetRemainingSlotSpace(new ItemStack(item)) <= 0) return false;
        if (cart.QuarrySupplyWorker != 0 && cart.QuarrySupplyWorker != fox.EntityId
            && cart.QuarrySupplyLease >= serverApi.World.ElapsedMilliseconds) return false;
        if (different)
        {
            if (!TryGetFoxStorage(fox, out _, buffer.Itemstack))
            { SetCartMaterialStatus(cart, "Free linked storage space to change the buffer's stone type."); return false; }
            if (!TryGetFoxStorageApproachTarget(fox, cart.Pos, out var approach) || approach == null) return false;
            job = new() { Kind = QuarryJobKind.TakeBuffer, Cart = cart, Target = cart.Pos.Copy(), Approach = approach };
        }
        else if (TryCreateQuarryCollection(fox, cart, bufferOnly: true, out job)) { }
        else
        {
            string owner = GetDomesticationStatus(fox)?.GetString("owner", "") ?? "";
            foreach (var record in packRepository.GetAmenitiesForOwner(owner).Where(r => r.Dimension == cart.Pos.dimension
                && (r.Kind is "storage" or "storage-target") && IsStorageRoutingEnabled(r))
                .OrderBy(r => fox.Pos.SquareDistanceTo(new Vec3d(r.X, r.Y, r.Z))))
            {
                var pos = new BlockPos(record.X, record.Y, record.Z, record.Dimension);
                double dx = pos.X - cart.Pos.X, dz = pos.Z - cart.Pos.Z;
                if (dx * dx + dz * dz > GetWorkCartRadius(fox) * GetWorkCartRadius(fox)
                    || !QuarryContainerAllowed(fox, pos, false, null, out var inventory, out _)
                    || !TryGetFoxStorageApproachTarget(fox, pos, out var approach) || approach == null) continue;
                for (int i = 0; i < inventory!.Count; i++)
                {
                    ItemSlot? candidateSlot = inventory[i];
                    if (candidateSlot == null || candidateSlot.Empty || candidateSlot.Itemstack?.Collectible?.Code.ToString() != item.Code.ToString()) continue;
                    job = new() { Kind = QuarryJobKind.Withdraw, Cart = cart, Target = pos, Approach = approach,
                        SourceSlot = i, Rock = rock, BufferDelivery = true };
                    break;
                }
                if (job != null) break;
            }
        }
        if (job == null) return false;
        cart.QuarrySupplyWorker = fox.EntityId;
        cart.QuarrySupplyLease = serverApi.World.ElapsedMilliseconds + 15000;
        return true;
    }

    private bool TryCreateQuarryCollection(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        bool bufferOnly, out WorkCartQuarryJob? job)
    {
        job = null;
        if (serverApi == null) return false;
        long now = serverApi.World.ElapsedMilliseconds;
        if (now >= cart.NextQuarryDebrisScan)
        {
            foreach (long expired in cart.QuarryFailedDebris.Where(p => p.Value <= now).Select(p => p.Key).ToArray())
                cart.QuarryFailedDebris.Remove(expired);
            var q = cart.Quarry;
            var center = cart.MiningMode ? cart.Pos.ToVec3d().Add(.5, 0, .5) : new Vec3d(q.OriginX + .5, cart.Pos.InternalY, (q.MinZ + q.MaxZ) / 2d);
            cart.QuarryDebris.Clear();
            cart.QuarryDebris.AddRange(serverApi.World.GetEntitiesAround(center, cart.MiningMode ? GetWorkCartRadius(fox) : q.Width + 3, cart.MiningMode ? cart.Mining.Depth + 24 : q.Depth + 52,
                e => e is EntityItem item && item.Alive && item.Itemstack?.StackSize > 0
                    && item.Pos.Dimension == cart.Pos.dimension && (cart.MiningMode ? MiningBounds(fox, cart, new((int)Math.Floor(item.Pos.X), (int)Math.Floor(item.Pos.Y), (int)Math.Floor(item.Pos.Z))) : q.Contains((int)Math.Floor(item.Pos.X), (int)Math.Floor(item.Pos.Z)))
                    && !CompanionPickupPolicy.IsCorpse(item.Itemstack.Collectible?.GetType())
                    && now - item.itemSpawnedMilliseconds >= 1500)
                .OfType<EntityItem>().OrderBy(e => e.Pos.SquareDistanceTo(fox.Pos)).Take(512).Select(e => e.EntityId));
            cart.NextQuarryDebrisScan = now + 1000;
        }
        foreach (long id in cart.QuarryDebris)
        {
            if (cart.QuarryFailedDebris.GetValueOrDefault(id) > now) continue;
            if (serverApi.World.GetEntityById(id) is not EntityItem item || !item.Alive || item.Slot.Empty
                || (droppedItemReservations.TryGetValue(id, out long holder) && holder != fox.EntityId)) continue;
            var stack = item.Slot.Itemstack;
            bool toBuffer = stack?.Collectible?.Code.ToString() == "game:stone-" + cart.Quarry.PreferredRock
                && cart.Tools[4].GetRemainingSlotSpace(stack) > 0;
            if (bufferOnly && !toBuffer) continue;
            if (!toBuffer && !TryGetFoxStorage(fox, out _, stack)) continue;
            // Navigation still validates a real stand position; no remote pickup.
            var target = item.Pos.AsBlockPos;
            var point = new QuarryPoint(target.X, target.Y, target.Z);
            // Drops can be pushed into a sealed cavity while buried supports
            // are replaced. Leave those alone instead of sending every worker
            // into the protected cave and starving the mining queue.
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(target) == null
                || (!cart.MiningMode && !(cart.Quarry.WasExcavated(point) || cart.Quarry.IsStructure(point) || QuarryOpenToSky(target, cart.Quarry)))) continue;
            Vec3d? approach;
            if (cart.MiningMode ? !TryMiningApproach(fox, cart, target, false, out approach)
                : !TryQuarryApproach(fox, cart, target, construction: false, out approach)) continue;
            if (approach == null) continue;
            droppedItemReservations[id] = fox.EntityId;
            job = new() { Kind = QuarryJobKind.Collect, Cart = cart, Target = target, Approach = approach,
                ItemEntityId = id, BufferDelivery = toBuffer, Rock = cart.Quarry.PreferredRock };
            return true;
        }
        return false;
    }

    private bool TryCreateQuarryDelivery(Entity fox, BlockEntityFeralKinshipWorkCart cart,
        ItemStack cargo, out WorkCartQuarryJob? job)
    {
        job = null;
        if (serverApi == null || !cargo.ResolveBlockOrItem(serverApi.World)) return false;
        bool buffer = fox.WatchedAttributes.GetBool(QuarryCarryBufferKey)
            && fox.WatchedAttributes.GetString(QuarryCarryCartKey) == WorkCartPositionKey(cart.Pos)
            && cargo.Collectible.Code.ToString() == "game:stone-" + cart.Quarry.PreferredRock
            && cart.Tools[4].GetRemainingSlotSpace(cargo) > 0;
        BlockPos? destination;
        if (buffer) destination = cart.Pos.Copy();
        else if (!TryGetFoxStorage(fox, out destination, cargo) || destination == null)
        { SetCartMaterialStatus(cart, "Waiting for linked storage space for carried material."); return false; }
        if (!TryGetFoxStorageApproachTarget(fox, destination, out var approach) || approach == null) return false;
        job = new() { Kind = QuarryJobKind.Deliver, Cart = cart, Target = destination, Approach = approach,
            BufferDelivery = buffer };
        return true;
    }

    private void SetQuarryCargo(Entity fox, BlockEntityFeralKinshipWorkCart cart, ItemStack? cargo, bool buffer)
    {
        if (cargo == null) fox.WatchedAttributes.RemoveAttribute("cartToolReturn");
        fox.WatchedAttributes.SetItemstack(QuarryCarryKey, cargo);
        fox.WatchedAttributes.SetBool(QuarryCarryBufferKey, buffer);
        fox.WatchedAttributes.SetString(QuarryCarryCartKey, WorkCartPositionKey(cart.Pos));
        fox.WatchedAttributes.MarkPathDirty(QuarryCarryKey);
        fox.WatchedAttributes.MarkPathDirty(QuarryCarryBufferKey);
        fox.WatchedAttributes.MarkPathDirty(QuarryCarryCartKey);
    }

    private bool ExecuteQuarryResourceJob(Entity fox, WorkCartQuarryJob job)
    {
        if (serverApi == null || job.Done || fox.Pos.SquareDistanceTo(job.Approach) > 4) return false;
        var cart = job.Cart;
        if (job.Kind is QuarryJobKind.WithdrawTool or QuarryJobKind.DeliverTool) return ExecuteCartToolJob(fox, job);
        if (cart.QuarrySupplyWorker == fox.EntityId) cart.QuarrySupplyLease = serverApi.World.ElapsedMilliseconds + 15000;
        var carry = fox.WatchedAttributes.GetItemstack(QuarryCarryKey);
        if (job.Kind == QuarryJobKind.Deliver)
        {
            if (carry == null || !carry.ResolveBlockOrItem(serverApi.World)) { job.Done = true; return false; }
            if (job.BufferDelivery)
            {
                if (packRepository?.GetWorkCart(cart.Pos)?.OwnerUid != (GetDomesticationStatus(fox)?.GetString("owner", "") ?? "")
                    || carry.Collectible.Code.ToString() != "game:stone-" + cart.Quarry.PreferredRock) return false;
                var source = new DummySlot(carry);
                source.TryPutInto(serverApi.World, cart.Tools[4], carry.StackSize);
                cart.Tools[4].MarkDirty();
                SetQuarryCargo(fox, cart, source.Empty ? null : source.Itemstack, buffer: false);
                cart.MarkDirty(true);
            }
            else if (QuarryContainerAllowed(fox, job.Target, true, carry, out var inventory, out var blockEntity))
            {
                TransferCargoIntoInventory(fox, carry, inventory!, blockEntity, out var remainder);
                SetQuarryCargo(fox, cart, remainder, buffer: false);
            }
            job.Done = true; return false;
        }
        if (carry != null && carry.StackSize > 0) return false;
        ItemStack? taken = null;
        if (job.Kind == QuarryJobKind.TakeBuffer)
        {
            taken = cart.Tools[4].TakeOutWhole(); cart.Tools[4].MarkDirty(); cart.MarkDirty(true);
        }
        else if (job.Kind == QuarryJobKind.Withdraw)
        {
            if (!QuarryContainerAllowed(fox, job.Target, false, null, out var inventory, out var blockEntity)
                || job.SourceSlot < 0 || job.SourceSlot >= inventory!.Count) return false;
            ItemSlot? source = inventory![job.SourceSlot];
            if (source == null || source.Empty || source.Itemstack?.Collectible?.Code.ToString() != "game:stone-" + job.Rock) return false;
            int room = cart.Tools[4].GetRemainingSlotSpace(source.Itemstack);
            if (room <= 0) return false;
            taken = source.TakeOut(Math.Min(room, source.StackSize)); source.MarkDirty(); blockEntity?.MarkDirty(true);
        }
        else if (job.Kind == QuarryJobKind.Collect)
        {
            if (serverApi.World.GetEntityById(job.ItemEntityId) is not EntityItem item || !item.Alive || item.Slot.Empty
                || item.Pos.SquareDistanceTo(fox.Pos) > 16
                || droppedItemReservations.GetValueOrDefault(item.EntityId) != fox.EntityId) return false;
            int amount = item.Slot.StackSize;
            if (job.BufferDelivery) amount = Math.Min(amount, cart.Tools[4].GetRemainingSlotSpace(item.Slot.Itemstack));
            if (amount <= 0) return false;
            taken = item.Slot.TakeOut(Math.Min(amount, item.Slot.Itemstack.Collectible.MaxStackSize));
            UpdateDroppedItemEntity(item); droppedItemReservations.Remove(item.EntityId);
        }
        if (taken != null) SetQuarryCargo(fox, cart, taken, job.BufferDelivery);
        job.Done = true; return false;
    }

    private void DropQuarryCargo(Entity fox)
    {
        ItemStack? cargo = fox.WatchedAttributes.GetItemstack(QuarryCarryKey);
        if (cargo == null || serverApi == null || !cargo.ResolveBlockOrItem(serverApi.World)) return;
        if (serverApi.World.SpawnItemEntity(cargo, fox.Pos.XYZ.AddCopy(0, .25, 0)) == null) return;
        fox.WatchedAttributes.RemoveAttribute(QuarryCarryKey);
        fox.WatchedAttributes.RemoveAttribute(QuarryCarryBufferKey);
        fox.WatchedAttributes.RemoveAttribute(QuarryCarryCartKey);
        fox.WatchedAttributes.MarkPathDirty(QuarryCarryKey);
    }
}
