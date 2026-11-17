using System;
using System.Collections.Generic;

namespace ACadSmartLeader.Core;

public sealed class LeaderUpdatePlan
{
    public LeaderData Data { get; }
    public string Text { get; }
    public bool Changed { get; }
    public bool Rebound { get; }
    public bool Skipped { get; }
    public LeaderUpdatePlan(LeaderData before, string currentText, string? target, string? targetId,
        LabelText? evaluated, bool force = false, bool locked = false)
    {
        Data = before.Copy(); Text = currentText; Skipped = locked;
        if (locked) return;
        // Keep the stored handle even when detached, so Undo of an erase can recover the original target.
        if (target == null) Data.State = LeaderState.Detached;
        else if (!force && !before.Matches(currentText)) Data.State = LeaderState.ManualText;
        else if (evaluated != null)
        {
            Text = evaluated.MText;
            Data.Text = LeaderData.TextStamp(Text);
            Data.State = evaluated.Errors.Count > 0 ? LeaderState.FormatError : LeaderState.Bound;
        }
        if (target != null)
        {
            Rebound = !string.Equals(before.Target, target, StringComparison.OrdinalIgnoreCase);
            Data.Target = target; Data.TargetId = targetId ?? "";
        }
        Changed = Text != currentText || !Data.SameAs(before);
    }
}

public enum RegenMode { Off, Regen, RegenAll }

/// <summary>Document-local deferred command state. No database writes happen in event callbacks.</summary>
public sealed class RegenSchedule
{
    public bool Pending { get; private set; }
    public bool Running { get; private set; }
    public void CommandEnded(string command, RegenMode mode)
    {
        if (Running) return;
        if (string.Equals(command, "REGENALL", StringComparison.OrdinalIgnoreCase) && mode != RegenMode.Off ||
            string.Equals(command, "REGEN", StringComparison.OrdinalIgnoreCase) && mode == RegenMode.Regen) Pending = true;
    }
    public bool Dispatch(bool documentIsIdle)
    {
        if (!documentIsIdle || !Pending || Running) return false;
        Pending = false; Running = true; return true;
    }
    public void Complete() { Running = false; }
    public void Clear() { Pending = false; Running = false; }
}
