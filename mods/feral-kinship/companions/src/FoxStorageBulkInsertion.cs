#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using BulkTransfer = System.Func<Vintagestory.API.Common.ItemSlot, Vintagestory.API.Common.IWorldAccessor, Vintagestory.API.Common.ItemSlot, int, int>;

namespace FeralKinshipCompanions;

/// <summary>
/// Optional adapter for modded inventories that publish a bulk-transfer
/// extension. This keeps storage integrations capability-based: the core mod
/// has no dependency on a particular storage mod or block code.
/// </summary>
internal static class FoxStorageBulkInsertion
{
    private const string TransferMethodName = "TryPutIntoBulk";
    private static readonly object Sync = new();
    private static readonly Dictionary<Type, BulkTransfer> TransferBySlotType = new();
    private static readonly HashSet<Type> CapacityOverridesBySlotType = new();
    private static readonly HashSet<Type> NonCapacityOverridesBySlotType = new();
    private static BulkTransfer[]? transferAdapters;
    private static DateTime lastAdapterScanUtc = DateTime.MinValue;

    /// <summary>
    /// Checks whether this slot advertises more capacity than a normal item
    /// stack and has a compatible bulk-transfer adapter available.
    /// </summary>
    internal static bool TryGetRemainingCapacity(ItemSlot destination, ItemStack stack, out int remaining)
    {
        remaining = 0;
        if (stack.Collectible == null || !HasCustomCapacity(destination)) return false;
        if (GetTransfer(destination.GetType()) == null) return false;

        DummySlot source = new(stack);
        if (!destination.CanHold(source)) return false;
        if (!destination.Empty && !Equals(destination.Itemstack?.Collectible, stack.Collectible)) return false;

        int reportedRemaining = destination.GetRemainingSlotSpace(stack);
        int normalRemaining = Math.Max(0, stack.Collectible.MaxStackSize - destination.StackSize);
        if (reportedRemaining <= normalRemaining) return false;

        remaining = reportedRemaining;
        return remaining > 0;
    }

    internal static bool CanTransfer(ItemSlot destination, ItemStack stack) =>
        TryGetRemainingCapacity(destination, stack, out _);

    internal static int Transfer(ItemSlot source, IWorldAccessor world, ItemSlot destination, int quantity)
    {
        if (source.Itemstack == null || !TryGetRemainingCapacity(destination, source.Itemstack, out _)) return 0;
        BulkTransfer? transfer = GetTransfer(destination.GetType());
        return transfer?.Invoke(source, world, destination, quantity) ?? 0;
    }

    private static bool HasCustomCapacity(ItemSlot slot)
    {
        Type slotType = slot.GetType();
        lock (Sync)
        {
            if (CapacityOverridesBySlotType.Contains(slotType)) return true;
            if (NonCapacityOverridesBySlotType.Contains(slotType)) return false;

            PropertyInfo? maxStackProperty = slotType.GetProperty(nameof(ItemSlot.MaxSlotStackSize));
            MethodInfo? maxStackGetter = maxStackProperty?.GetMethod;
            MethodInfo? remainingMethod = slotType.GetMethod(nameof(ItemSlot.GetRemainingSlotSpace));
            bool overridesCapacity = maxStackGetter?.DeclaringType != typeof(ItemSlot)
                || remainingMethod?.DeclaringType != typeof(ItemSlot);
            (overridesCapacity ? CapacityOverridesBySlotType : NonCapacityOverridesBySlotType).Add(slotType);
            return overridesCapacity;
        }
    }

    private static BulkTransfer? GetTransfer(Type destinationType)
    {
        lock (Sync)
        {
            if (TransferBySlotType.TryGetValue(destinationType, out BulkTransfer? cached))
            {
                return cached;
            }

            BulkTransfer[] adapters = GetTransferAdapters();
            Assembly destinationAssembly = destinationType.Assembly;
            BulkTransfer[] localAdapters = adapters
                .Where(adapter => adapter.Method.DeclaringType?.Assembly == destinationAssembly)
                .ToArray();

            BulkTransfer? selected = localAdapters.Length == 1
                ? localAdapters[0]
                : localAdapters.Length == 0 && adapters.Length == 1
                    ? adapters[0]
                    : null;

            if (selected != null)
            {
                TransferBySlotType[destinationType] = selected;
            }
            return selected;
        }
    }

    private static BulkTransfer[] GetTransferAdapters()
    {
        if (transferAdapters != null) return transferAdapters;

        DateTime now = DateTime.UtcNow;
        if (now - lastAdapterScanUtc < TimeSpan.FromSeconds(5))
        {
            return Array.Empty<BulkTransfer>();
        }
        lastAdapterScanUtc = now;

        List<BulkTransfer> found = new();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (Type type in types)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!string.Equals(method.Name, TransferMethodName, StringComparison.Ordinal)
                        || method.ReturnType != typeof(int)) continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 4
                        || parameters[0].ParameterType != typeof(ItemSlot)
                        || parameters[1].ParameterType != typeof(IWorldAccessor)
                        || parameters[2].ParameterType != typeof(ItemSlot)
                        || parameters[3].ParameterType != typeof(int)) continue;

                    try
                    {
                        found.Add((BulkTransfer)method.CreateDelegate(typeof(BulkTransfer)));
                    }
                    catch
                    {
                        // An unrelated method with the same signature is not
                        // an adapter unless it can be bound as that delegate.
                    }
                }
            }
        }

        if (found.Count > 0)
        {
            transferAdapters = found.ToArray();
        }
        return transferAdapters ?? Array.Empty<BulkTransfer>();
    }
}
