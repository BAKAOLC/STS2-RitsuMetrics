// SPDX-License-Identifier: MPL-2.0

using Godot;

namespace STS2RitsuMetrics.Ui
{
    internal sealed partial class DashboardDialogController : Control
    {
        private readonly Button _cancel;
        private readonly Button _confirm;
        private readonly Label _message;
        private readonly Label _title;
        private Action? _confirmed;

        internal DashboardDialogController()
        {
            Name = "DashboardDialogs";
            LayoutMode = 1;
            AnchorsPreset = (int)LayoutPreset.FullRect;
            MouseFilter = MouseFilterEnum.Stop;
            ZIndex = 500;
            Hide();

            var scrim = new ColorRect
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                Color = new("03070DB8"),
                MouseFilter = MouseFilterEnum.Stop,
            };
            scrim.GuiInput += OnScrimInput;
            AddChild(scrim);

            var center = new CenterContainer
            {
                LayoutMode = 1,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(center);
            var panel = new PanelContainer
            {
                CustomMinimumSize = new(460f, 0f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            panel.AddThemeStyleboxOverride("panel", DashboardControlTheme.DialogStyle(22f));
            center.AddChild(panel);
            var content = new VBoxContainer();
            content.AddThemeConstantOverride("separation", 14);
            panel.AddChild(content);

            _title = new()
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            DashboardControlTheme.ApplyDialogTitle(_title);
            content.AddChild(_title);
            _message = new()
            {
                CustomMinimumSize = new(410f, 0f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            DashboardControlTheme.ApplySecondaryText(_message);
            _message.AddThemeConstantOverride("line_spacing", 4);
            content.AddChild(_message);

            var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            actions.AddThemeConstantOverride("separation", 10);
            content.AddChild(actions);
            _cancel = new() { CustomMinimumSize = new(110f, 44f), FocusMode = FocusModeEnum.All };
            DashboardControlTheme.ApplyButton(_cancel);
            _cancel.Pressed += Dismiss;
            actions.AddChild(_cancel);
            _confirm = new() { CustomMinimumSize = new(110f, 44f), FocusMode = FocusModeEnum.All };
            DashboardControlTheme.ApplyButton(_confirm, DashboardButtonKind.Danger);
            _confirm.Pressed += Confirm;
            actions.AddChild(_confirm);
        }

        internal void ShowConfirmation(
            string title,
            string message,
            string confirmText,
            string cancelText,
            Action confirmed)
        {
            ArgumentNullException.ThrowIfNull(confirmed);
            _title.Text = title;
            _message.Text = message;
            _confirm.Text = confirmText;
            _cancel.Text = cancelText;
            _cancel.Visible = true;
            _confirmed = confirmed;
            Show();
            MoveToFront();
            _cancel.GrabFocus();
        }

        internal void ShowMessage(string title, string message, string closeText)
        {
            _title.Text = title;
            _message.Text = message;
            _confirm.Text = closeText;
            _cancel.Visible = false;
            _confirmed = null;
            Show();
            MoveToFront();
            _confirm.GrabFocus();
        }

        public override void _UnhandledKeyInput(InputEvent input)
        {
            if (!Visible || input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                return;
            Dismiss();
            GetViewport().SetInputAsHandled();
        }

        private void Confirm()
        {
            var confirmed = _confirmed;
            Dismiss();
            confirmed?.Invoke();
        }

        private void Dismiss()
        {
            _confirmed = null;
            Hide();
        }

        private void OnScrimInput(InputEvent input)
        {
            if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                return;
            Dismiss();
            AcceptEvent();
        }
    }
}
