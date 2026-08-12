using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using HamstuffAgcGuard.Audio;
using HamstuffAgcGuard.Logging;
using HamstuffAgcGuard.Storage;

namespace HamstuffAgcGuard.UI
{
    /// <summary>
    /// "Disconnect, press Next, now connect" wizard: snapshots the currently active
    /// audio endpoints, waits for the user to plug the transceiver back in, and
    /// diffs the endpoint list to automatically find the new device's USB VID/PID.
    ///
    /// Layout is entirely auto-sizing (FlowLayoutPanel + AutoSize labels/form)
    /// rather than fixed pixel coordinates, so it renders correctly regardless of
    /// Windows display scaling / DPI - fixed coordinates clipped text at anything
    /// other than 100% scaling.
    /// </summary>
    internal sealed class AddDeviceWizardForm : Form
    {
        private const int ContentWidth = 440;

        private enum Step
        {
            Disconnect,
            WaitForConnect,
            Results,
        }

        private readonly AudioDeviceService _audio;
        private readonly DeviceStore _store;

        private readonly Label _instructionsLabel;
        private readonly Label _statusLabel;
        private readonly CheckedListBox _resultsList;
        private readonly Button _primaryButton;
        private readonly Button _backButton;
        private readonly Button _cancelButton;
        private readonly System.Windows.Forms.Timer _pollTimer;

        private HashSet<string> _baselineEndpointIds = new(StringComparer.OrdinalIgnoreCase);
        private List<(string HardwareId, string DisplayName)> _candidates = new();
        private Step _step = Step.Disconnect;
        private int _pollSeconds;

        public AddDeviceWizardForm(AudioDeviceService audio, DeviceStore store)
        {
            _audio = audio;
            _store = store;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Text = "Add Monitored Device";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            Padding = new Padding(20);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _instructionsLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 0, 0, 16),
            };

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
            };

            _resultsList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                Visible = false,
                CheckOnClick = true,
            };

            // Both the "waiting" status text and the "found these devices" results
            // list occupy the same fixed-size slot, swapped via Visible - keeps the
            // rest of the layout stable between steps 2 and 3.
            var contentHost = new Panel
            {
                Size = new Size(ContentWidth, 150),
                Margin = new Padding(0, 0, 0, 16),
            };
            contentHost.Controls.Add(_resultsList);
            contentHost.Controls.Add(_statusLabel);

            _backButton = new Button { Text = "< Back", AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
            _primaryButton = new Button { Text = "Next >", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            _cancelButton = new Button { Text = "Cancel", AutoSize = true, Margin = new Padding(0) };

            _backButton.Click += (_, _) => GoBack();
            _primaryButton.Click += (_, _) => PrimaryAction();
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var buttonRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            buttonRow.Controls.Add(_backButton);
            buttonRow.Controls.Add(_primaryButton);
            buttonRow.Controls.Add(_cancelButton);

            // Deliberately NOT Dock=Fill: the Form itself is AutoSize=true, and
            // docking this panel to Fill would make it always expand to whatever
            // size the form currently is - a circular dependency that defeats
            // AutoSize. Left undocked, its preferred size (content + Padding)
            // drives the form's size instead, which is what we want.
            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            root.Controls.Add(_instructionsLabel);
            root.Controls.Add(contentHost);
            root.Controls.Add(buttonRow);

            Controls.Add(root);

            _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _pollTimer.Tick += (_, _) => PollForNewDevice();

            FormClosed += (_, _) =>
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
            };

            RenderStep();
        }

        private void RenderStep()
        {
            _pollTimer.Stop();

            switch (_step)
            {
                case Step.Disconnect:
                    _instructionsLabel.Text =
                        "Step 1 of 3\r\n\r\n" +
                        "Disconnect the transceiver's USB audio device from this computer now " +
                        "(if it is currently plugged in), then click Next.";
                    _statusLabel.Text = "";
                    _statusLabel.Visible = true;
                    _resultsList.Visible = false;
                    _backButton.Visible = false;
                    _primaryButton.Text = "Next >";
                    _primaryButton.Enabled = true;
                    break;

                case Step.WaitForConnect:
                    _instructionsLabel.Text =
                        "Step 2 of 3\r\n\r\n" +
                        "Now connect the transceiver's USB audio device. Windows will install it " +
                        "automatically - this wizard will pick it up as soon as it shows up.";
                    _statusLabel.Text = "Waiting for a new audio device...";
                    _statusLabel.Visible = true;
                    _resultsList.Visible = false;
                    _backButton.Visible = true;
                    _primaryButton.Text = "Detect Now";
                    _primaryButton.Enabled = true;
                    _pollSeconds = 0;
                    _pollTimer.Start();
                    break;

                case Step.Results:
                    _instructionsLabel.Text =
                        "Step 3 of 3\r\n\r\n" +
                        "Found the following new audio device(s). Check the one(s) you want " +
                        "monitored, then click Add.";
                    _statusLabel.Visible = false;
                    _resultsList.Visible = true;
                    _resultsList.Items.Clear();
                    foreach (var candidate in _candidates)
                    {
                        _resultsList.Items.Add($"{candidate.DisplayName}  [{candidate.HardwareId}]", true);
                    }

                    _backButton.Visible = true;
                    _primaryButton.Text = "Add Selected";
                    _primaryButton.Enabled = _candidates.Count > 0;
                    break;
            }
        }

        private void GoBack()
        {
            if (_step == Step.WaitForConnect)
            {
                _step = Step.Disconnect;
            }
            else if (_step == Step.Results)
            {
                _step = Step.WaitForConnect;
            }

            RenderStep();
        }

        private void PrimaryAction()
        {
            switch (_step)
            {
                case Step.Disconnect:
                    CaptureBaseline();
                    _step = Step.WaitForConnect;
                    RenderStep();
                    break;

                case Step.WaitForConnect:
                    PollForNewDevice(force: true);
                    break;

                case Step.Results:
                    AddSelected();
                    break;
            }
        }

        private void CaptureBaseline()
        {
            try
            {
                _baselineEndpointIds = _audio.GetActiveEndpoints()
                    .Select(e => e.EndpointId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to capture baseline audio endpoint list in wizard.", ex);
                _baselineEndpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void PollForNewDevice(bool force = false)
        {
            _pollSeconds++;

            List<AudioEndpointInfo> current;
            try
            {
                current = _audio.GetActiveEndpoints();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to enumerate audio endpoints while waiting for a new device.", ex);
                _statusLabel.Text = "There was a problem reading audio devices. See the log folder for details.";
                return;
            }

            var newAny = current
                .Where(e => !_baselineEndpointIds.Contains(e.EndpointId))
                .ToList();
            var newOnes = newAny.Where(e => e.HardwareId != null).ToList();

            if (force)
            {
                Logger.Info(
                    $"Wizard Detect Now: {current.Count} active endpoint(s), {newAny.Count} new since baseline, " +
                    $"{newOnes.Count} with a resolvable USB hardware id.");
            }

            var unresolvable = newAny.Where(e => e.HardwareId == null).ToList();
            if (unresolvable.Count > 0)
            {
                Logger.Warn(
                    "New endpoint(s) appeared but no USB hardware id could be resolved for: " +
                    string.Join(", ", unresolvable.Select(e => $"'{e.FriendlyName}' ({e.Flow}, endpointId={e.EndpointId})")));
            }

            if (newOnes.Count == 0)
            {
                _statusLabel.Text = force
                    ? $"No new device detected yet (waited {_pollSeconds}s). Keep it plugged in, wait a " +
                      "moment for Windows to finish installing it, and try again."
                    : $"Waiting for a new audio device... ({_pollSeconds}s)";
                return;
            }

            _candidates = newOnes
                .GroupBy(e => e.HardwareId!, StringComparer.OrdinalIgnoreCase)
                .Select(g => (
                    HardwareId: g.Key,
                    DisplayName: string.Join(" / ", g.Select(e => e.FriendlyName).Distinct())))
                .ToList();

            _step = Step.Results;
            RenderStep();
        }

        private void AddSelected()
        {
            var anyAdded = false;
            for (int i = 0; i < _resultsList.Items.Count; i++)
            {
                if (!_resultsList.GetItemChecked(i))
                {
                    continue;
                }

                var candidate = _candidates[i];
                _store.Add(new MonitoredDevice
                {
                    Id = candidate.HardwareId,
                    FriendlyName = candidate.DisplayName,
                    DateAdded = DateTime.UtcNow,
                });
                Logger.Info($"Added monitored device '{candidate.DisplayName}' ({candidate.HardwareId}) via wizard.");
                anyAdded = true;
            }

            if (!anyAdded)
            {
                MessageBox.Show(this, "Select at least one device to add.", "Hamstuff AGC Guard",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
