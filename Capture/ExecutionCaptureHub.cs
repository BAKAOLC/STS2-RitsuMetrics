// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2RitsuMetrics.Capture
{
    internal static class ExecutionCaptureHub
    {
        private static readonly AsyncLocal<Frame?> CurrentFrame = new();
        private static readonly AsyncLocal<KillFrame?> CurrentKillFrame = new();
        private static Type? _doomPowerType;

        internal static void ConfigureDoomType(Type doomPowerType)
        {
            _doomPowerType = doomPowerType;
        }

        internal static State? BeginDoom(object argument)
        {
            if (!CaptureBridge.Active)
                return null;
            var creatures = argument as IEnumerable<Creature> ?? [];
            var sources = new Dictionary<Creature, PowerModel?>(ReferenceEqualityComparer.Instance);
            foreach (var creature in creatures)
                sources[creature] = creature.Powers.FirstOrDefault(power => power.GetType() == _doomPowerType);
            var previous = CurrentFrame.Value;
            var frame = new Frame(sources, previous);
            CurrentFrame.Value = frame;
            return new(frame, previous);
        }

        internal static void Restore(State? state)
        {
            if (state != null && ReferenceEquals(CurrentFrame.Value, state.Frame))
                CurrentFrame.Value = state.Previous;
        }

        internal static PowerModel? DoomSource(Creature creature)
        {
            for (var frame = CurrentFrame.Value; frame != null; frame = frame.Previous)
                if (frame.Sources.TryGetValue(creature, out var source))
                    return source;
            return null;
        }

        internal static KillState? BeginExplicitKill(object argument)
        {
            if (!CaptureBridge.Active)
                return null;
            var creatures = argument as IEnumerable<Creature> ?? [];
            var previous = CurrentKillFrame.Value;
            var frame = new KillFrame(new HashSet<Creature>(creatures, ReferenceEqualityComparer.Instance), previous);
            CurrentKillFrame.Value = frame;
            return new(frame, previous);
        }

        internal static void RestoreKill(KillState? state)
        {
            if (state != null && ReferenceEquals(CurrentKillFrame.Value, state.Current))
                CurrentKillFrame.Value = state.Previous;
        }

        internal static bool IsExplicitKill(Creature creature)
        {
            for (var frame = CurrentKillFrame.Value; frame != null; frame = frame.Previous)
                if (frame.Creatures.Contains(creature))
                    return true;
            return false;
        }

        internal sealed record State(Frame Frame, Frame? Previous);

        internal sealed record Frame(
            IReadOnlyDictionary<Creature, PowerModel?> Sources,
            Frame? Previous);

        internal sealed record KillState(KillFrame Current, KillFrame? Previous);

        internal sealed record KillFrame(
            IReadOnlySet<Creature> Creatures,
            KillFrame? Previous);
    }
}
