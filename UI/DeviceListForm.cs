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

            // Declare the DPI baseline this layout was designed at so WinForms can
            // correctly scale the fixed pixel coordinates below on high-DPI
            // displays. Without this, controls are laid out using the larger
            // scaled font but un-scaled positions/sizes, which clips button text.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);

            Text = "Hamstuff AGC Guard - Monitored Devices";
            Width = 600;
            Height = 420;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowIcon = false;

            _listView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                Left = 12,
                Top = 12,
                Width = 560,
                Height = 280,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            _listView.Columns.Add("Hardware ID", 170);
            _listView.Columns.Add("Friendly Name", 260);
            _listView.Columns.Add("Added", 100);

            var addButton = new Button
            {
                Text = "Add via Wizard...",
                Left = 12,
                Top = 305,
                Width = 170,
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var removeButton = new Button
            {
                Text = "Remove Selected",
                Left = 192,
                Top = 305,
                Width = 170,
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };
            var closeButton = new Button
            {
                Text = "Close",
                Left = 492,
                Top = 305,
                Width = 90,
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };

            addButton.Click += (_, _) => OnAddViaWizard();
            removeButton.Click += (_, _) => OnRemoveSelected();
            closeButton.Click += (_, _) => Close();

            Controls.Add(_listView);
            Controls.Add(addButton);
            Controls.Add(removeButton);
            Controls.Add(closeButton);

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
