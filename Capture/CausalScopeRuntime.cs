// SPDX-License-Identifier: MPL-2.0

using MegaCrit.Sts2.Core.Models;
using STS2RitsuMetrics.Api;
using STS2RitsuMetrics.Core;

namespace STS2RitsuMetrics.Capture
{
    internal sealed record EffectScopeCapture(
        string EventId,
        string? ParentEventId,
        string HookName,
        AbstractModel Model,
        SourceDescriptor Source,
        DateTimeOffset OccurredAtUtc);

    internal sealed record CausalScopeSnapshot(
        string EventId,
        string? ParentEventId,
        AbstractModel? Model,
        SourceDescriptor? Source,
        string ActionId);

    internal static class CaptureBridge
    {
        internal static Action<EffectScopeCapture>? EffectMaterialized { get; set; }
        internal static Func<string?>? FallbackParentResolver { get; set; }
        internal static Func<bool>? IsCombatActive { get; set; }

        internal static bool Active => IsCombatActive?.Invoke() == true;
    }

    internal static class CausalScopeRuntime
    {
        private static readonly AsyncLocal<ScopeFrame?> CurrentFrame = new();
        private static long _scopeSequence;

        internal static ScopeState? EnterModel(AbstractModel model, string hookName)
        {
            if (!CaptureBridge.Active)
                return null;
            var previous = CurrentFrame.Value;
            var source = GameDescriptorFactory.Model(model);
            var frame = new ScopeFrame(
                $"effect:{Interlocked.Increment(ref _scopeSequence)}",
                previous,
                null,
                model,
                source,
                hookName,
                false);
            CurrentFrame.Value = frame;
            return new(frame, previous);
        }

        internal static ScopeState EnterExplicit(string eventId, AbstractModel? model, SourceDescriptor? source,
            string actionId)
        {
            var previous = CurrentFrame.Value;
            var frame = new ScopeFrame(eventId, previous, null, model, source, actionId, true);
            CurrentFrame.Value = frame;
            return new(frame, previous);
        }

        internal static void Restore(ScopeState? state)
        {
            if (state == null)
                return;
            if (ReferenceEquals(CurrentFrame.Value, state.Frame))
                CurrentFrame.Value = state.Previous;
        }

        internal static string? ResolveParentEventId()
        {
            var fallback = CaptureBridge.FallbackParentResolver?.Invoke();
            var frame = CurrentFrame.Value;
            if (frame == null)
                return fallback;
            Materialize(frame, fallback);
            return frame.EventId;
        }

        internal static CausalScopeSnapshot? Snapshot()
        {
            var fallback = CaptureBridge.FallbackParentResolver?.Invoke();
            var frame = CurrentFrame.Value;
            if (frame == null)
                return null;
            Materialize(frame, fallback);
            return new(frame.EventId, ParentId(frame, fallback), frame.Model, frame.Source, frame.ActionId);
        }

        private static void Materialize(ScopeFrame frame, string? fallback)
        {
            if (frame.Materialized)
                return;
            if (frame.Parent != null)
                Materialize(frame.Parent, fallback);
            var parentId = ParentId(frame, fallback);
            frame.Materialized = true;
            if (frame.Model == null || frame.Source == null)
                return;
            CaptureBridge.EffectMaterialized?.Invoke(new(
                frame.EventId,
                parentId,
                frame.ActionId,
                frame.Model,
                frame.Source,
                DateTimeOffset.UtcNow));
        }

        private static string? ParentId(ScopeFrame frame, string? fallback)
        {
            return frame.Parent?.EventId ?? frame.ExplicitParentEventId ?? fallback;
        }

        internal sealed class ScopeState(ScopeFrame frame, ScopeFrame? previous)
        {
            internal ScopeFrame Frame { get; } = frame;
            internal ScopeFrame? Previous { get; } = previous;
        }

        internal sealed class ScopeFrame(
            string eventId,
            ScopeFrame? parent,
            string? explicitParentEventId,
            AbstractModel? model,
            SourceDescriptor? source,
            string actionId,
            bool materialized)
        {
            internal string EventId { get; } = eventId;
            internal ScopeFrame? Parent { get; } = parent;
            internal string? ExplicitParentEventId { get; } = explicitParentEventId;
            internal AbstractModel? Model { get; } = model;
            internal SourceDescriptor? Source { get; } = source;
            internal string ActionId { get; } = actionId;
            internal bool Materialized { get; set; } = materialized;
        }
    }
}
