// SPDX-License-Identifier: MPL-2.0

using Godot;
using MegaCrit.Sts2.Core.Nodes;
using STS2RitsuLib;

namespace STS2RitsuMetrics.Ui
{
    internal static class OverlayBootstrap
    {
        private static IDisposable? _subscription;

        internal static void Initialize()
        {
            _subscription ??= RitsuLibFramework.SubscribeLifecycle<GameReadyEvent>(evt => EnsureAttached(evt.Game));
            EnsureAttached(NGame.Instance);
        }

        private static void EnsureAttached(Node? game)
        {
            if (Main.DashboardHost is { } current && GodotObject.IsInstanceValid(current))
                return;
            if (game == null)
                return;
            var host = new DashboardHost { Name = "RitsuMetricsDashboardHost" };
            host.Initialize(Main.Dashboards);
            game.AddChild(host);
            Main.DashboardHost = host;
        }
    }
}
