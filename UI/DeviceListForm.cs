using System;
using System.Linq;
using System.Windows.Forms;
using HamstuffAgcGuard.Storage;

namespace HamstuffAgcGuard.UI
{
    internal sealed class DeviceListForm : Form
    {
        private readonly DeviceStore _store;
        private readonly Func<AddDeviceWizardForm> _wizardFactory;
        private readonly ListView _listView;

        /// <summary>Raised whenever the monitored device list has been added to or removed from.</summary>
        public event Action? DevicesChanged;

        public DeviceListForm(DeviceStore store, Func<AddDeviceWizardForm> wizardFactory)
        {
            _store = store;
            _wizardFactory = wizardFactory;

            // Declare the DPI baseline this layout was designed at, and lay
            // everything out with docking/auto-size instead of fixed pixel
            // coordinates, so it renders correctly at any Windows display scaling.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);

            Text = "Hamstuff AGC Guard - Monitored Devices";
            ClientSize = new System.Drawing.Size(620, 420);
            MinimumSize = new System.Drawing.Size(420, 300);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowIcon = false;
            Padding = new Padding(12);

            _listView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
            };
            _listView.Columns.Add("Hardware ID", 170);
            _listView.Columns.Add("Friendly Name", 260);
            _listView.Columns.Add("Added", 100);

            var addButton = new Button { Text = "Add via Wizard...", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var removeButton = new Button { Text = "Remove Selected", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var closeButton = new Button { Text = "Close", AutoSize = true, Margin = new Padding(0) };

            addButton.Click += (_, _) => OnAddViaWizard();
            removeButton.Click += (_, _) => OnRemoveSelected();
            closeButton.Click += (_, _) => Close();

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 8, 0, 0),
            };
            buttonRow.Controls.Add(addButton);
            buttonRow.Controls.Add(removeButton);
            buttonRow.Controls.Add(closeButton);

            // A TableLayoutPanel with an explicit Percent row for the list and an
            // AutoSize row for the buttons avoids relying on Controls.Add order /
            // Dock z-order semantics (which are easy to get backwards) to decide
            // who claims space first - each row's sizing is stated directly.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_listView, 0, 0);
            root.Controls.Add(buttonRow, 0, 1);

            Controls.Add(root);

            Load += (_, _) => RefreshList();
        }

        private void RefreshList()
        {
            _listView.Items.Clear();
            foreach (var device in _store.Devices.OrderBy(d => d.FriendlyName))
            {
                var item = new ListViewItem(device.Id);
                item.SubItems.Add(device.FriendlyName);
                item.SubItems.Add(device.DateAdded.ToLocalTime().ToString("yyyy-MM-dd"));
                item.Tag = device.Id;
                _listView.Items.Add(item);
            }
        }

        private void OnAddViaWizard()
        {
            using var wizard = _wizardFactory();
            if (wizard.ShowDialog(this) == DialogResult.OK)
            {
                RefreshList();
                DevicesChanged?.Invoke();
            }
        }

        private void OnRemoveSelected()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a device to remove first.", "Hamstuff AGC Guard",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item = _listView.SelectedItems[0];
            var id = (string)item.Tag!;
            var confirm = MessageBox.Show(
                this,
                $"Remove '{item.SubItems[1].Text}' ({id}) from the monitored device list?",
                "Confirm Removal",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                _store.Remove(id);
                RefreshList();
                DevicesChanged?.Invoke();
            }
        }
    }
}
