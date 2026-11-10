using System;
using UnityEngine;

/// <summary>
/// Soul as plain numbers (no UI). Two pools:
///  - spendable: what spells can use right now (capped at spendableCap).
///  - reserve:   extra stored Soul (maxSoul - spendableCap). It cannot be spent directly.
/// Gaining Soul fills spendable first; overflow goes to reserve. Every gain additionally moves
/// reserveTransferPerGain Soul from reserve into spendable (the refill rule).
/// </summary>
public class SoulMeter : MonoBehaviour
{
    public int maxSoul = 6;
    public int spendableCap = 3;
    public int startingSpendable = 3;
    public int startingReserve = 3;
    public int reserveTransferPerGain = 1;

    [Header("Current (runtime, editable for testing)")]
    public int spendable;
    public int reserve;

    public int Total { get { return spendable + reserve; } }
    public int ReserveCapacity { get { return Mathf.Max(0, maxSoul - spendableCap); } }

    /// <summary>Fires with (spendable, reserve) whenever values change. Hook UI here later.</summary>
    public event Action<int, int> OnChanged;

    void Awake()
    {
        spendable = Mathf.Clamp(startingSpendable, 0, spendableCap);
        reserve = Mathf.Clamp(startingReserve, 0, ReserveCapacity);
    }

    public bool CanSpend(int cost) { return cost <= spendable; }

    public bool TrySpend(int cost)
    {
        if (cost > spendable) return false;
        spendable -= cost;
        Notify();
        return true;
    }

    public void AddSoul(int amount)
    {
        if (amount <= 0) return;
        int toSpendable = Mathf.Min(amount, spendableCap - spendable);
        spendable += toSpendable;
        reserve = Mathf.Min(reserve + (amount - toSpendable), ReserveCapacity);

        int move = Mathf.Min(reserveTransferPerGain, reserve, spendableCap - spendable);
        if (move > 0) { spendable += move; reserve -= move; }
        Notify();
    }

    void Notify() { if (OnChanged != null) OnChanged(spendable, reserve); }

    [ContextMenu("Add 1 Soul")] void DebugAdd1() { AddSoul(1); }
    [ContextMenu("Add 3 Soul")] void DebugAdd3() { AddSoul(3); }
}
