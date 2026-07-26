// SPDX-License-Identifier: MPL-2.0

using Godot;
using MegaCrit.Sts2.Core.Nodes;
using STS2RitsuLib;

namespace STS2RitsuMetrics.Ui
{
    internal static class OverlayBootstrap
    {
        private static readonly OverlayStartupGate StartupGate = new();
        private static IDisposable? _gameReadySubscription;
        private static Node? _game;
        private static IDisposable? _mainMenuReadySubscription;

        internal static void Initialize()
        {
            _gameReadySubscription ??= RitsuLibFramework.SubscribeLifecycle<GameReadyEvent>(evt =>
            {
                _game = evt.Game;
                AttachWhenReady();
            });
            _mainMenuReadySubscription ??= RitsuLibFramework.SubscribeLifecycleOnce<MainMenuReadyEvent>(_ =>
            {
                StartupGate.MarkMainMenuReady();
                _game ??= NGame.Instance;
                AttachWhenReady();
            });
        }

        private static void AttachWhenReady()
        {
            if (StartupGate.CanAttach(_game != null))
                EnsureAttached(_game);
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
