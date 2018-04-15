﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

using KeePass;
using KeePass.UI;
using KeePass.Plugins;
using KeePass.Resources;
using KeePassRPC;
using System.Reflection;

namespace KeePassRPC.Forms
{
    public partial class OptionsForm : Form
    {
        private IPluginHost _host;
        private KeePassRPCExt _plugin;

        public OptionsForm(IPluginHost host, KeePassRPCExt plugin)
        {
            _host = host;
            _plugin = plugin;

            InitializeComponent();
            Icon = global::KeePassRPC.Properties.Resources.keefox;
            checkBox1.Text = "Automatically save KeePass database when Kee makes changes";
            if (host.CustomConfig.GetBool("KeePassRPC.KeeFox.autoCommit", true))
                checkBox1.Checked = true;
            else
                checkBox1.Checked = false;

            checkBox2.Text = "Immediately edit entries created by Kee";
            if (host.CustomConfig.GetBool("KeePassRPC.KeeFox.editNewEntries", false))
                checkBox2.Checked = true;
            else
                checkBox2.Checked = false;

            label13.Text = "You can generate new random passwords from Kee. These are stored in your system clipboard ready for you to paste into \"new user\" or \"change password\" fields. To protect against accidents these new passwords can be automatically stored in your current KeePass database under a special \"Kee Generated Password Backups\" group. You can generate new passwords when not logged in to a KeePass database but they will not receive this extra protection. The KeePass database can NOT be automatically saved after creating these backups so some problems can still result in a lost generated password.";

            checkBox3.Text = "Store a backup of each password generated by Kee";
            if (host.CustomConfig.GetBool("KeePassRPC.KeeFox.backupNewPasswords", true))
                checkBox3.Checked = true;
            else
                checkBox3.Checked = false;

            textBoxAuthExpiry.Text = (_host.CustomConfig.GetLong("KeePassRPC.AuthorisationExpiryTime", 8760 * 3600) / 3600).ToString();

            long secLevel = _host.CustomConfig.GetLong("KeePassRPC.SecurityLevel", 2);
            long secLevelClientMin = _host.CustomConfig.GetLong("KeePassRPC.SecurityLevelClientMinimum", 2);
            switch (secLevel)
            {
                case 1: comboBoxSecLevelKeePass.SelectedItem = "Low"; break;
                case 2: comboBoxSecLevelKeePass.SelectedItem = "Medium"; break;
                default: comboBoxSecLevelKeePass.SelectedItem = "High"; break;
            }
            switch (secLevelClientMin)
            {
                case 1: comboBoxSecLevelMinClient.SelectedItem = "Low"; break;
                case 2: comboBoxSecLevelMinClient.SelectedItem = "Medium"; break;
                default: comboBoxSecLevelMinClient.SelectedItem = "High"; break;
            }

            label6.Text = "Listen for connections on this TCP/IP port.";
            textBoxPort.Text = _host.CustomConfig.GetLong("KeePassRPC.webSocket.port", 12546).ToString();

            UpdateAuthorisedConnections();
        }

        private void UpdateAuthorisedConnections()
        {
            KeyContainerClass[] kcs = FindAuthorisedConnections();

            if (kcs == null)
            {
                // Tell the user it's not worked.
                dataGridView1.Visible = false;
                labelAuthorisedClientsFail.Visible = true;
                return;
            }

            List<string> connectedClientUsernames = new List<string>();

            foreach (KeePassRPCClientConnection client in _plugin.GetConnectedRPCClients())
                if (!string.IsNullOrEmpty(client.UserName))
                    connectedClientUsernames.Add(client.UserName);

            // Update the screen
            foreach (KeyContainerClass kc in kcs)
            {
                bool connected = false;
                if (connectedClientUsernames.Contains(kc.Username))
                    connected = true;

                string[] row = new string[] { kc.ClientName, kc.Username, kc.AuthExpires.ToString() };
                int rowid = dataGridView1.Rows.Add(row);
                dataGridView1.Rows[rowid].Cells[3].Value = connected;
            }
            return;

        }

        private KeyContainerClass[] FindAuthorisedConnections()
        {
            //This might not work, especially in .NET 2.0 RTM, a shame but more
            //up to date users might as well use the feature if possible.
            Dictionary<string, string> configValues;
            try
            {
                FieldInfo fi = typeof(KeePass.App.Configuration.AceCustomConfig)
                               .GetField("m_vItems", BindingFlags.NonPublic | BindingFlags.Instance);
                configValues = (Dictionary<string, string>)fi.GetValue(_host.CustomConfig);

                
            }
            catch
            {
                return null;
            }

            List<KeyContainerClass> keyContainers = new List<KeyContainerClass>();

            foreach (KeyValuePair<string, string> kvp in configValues)
            {
                if (kvp.Key.StartsWith("KeePassRPC.Key."))
                {
                    string username = kvp.Key.Substring(15);
                    byte[] serialisedKeyContainer = null;

                    // Assume config entry is encrypted but fall back to attempting direct deserialisation if something goes wrong
                    
                    if (string.IsNullOrEmpty(kvp.Value))
                        return null;
                    try
                    {
                        byte[] keyBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                        Convert.FromBase64String(kvp.Value),
                        new byte[] { 172, 218, 37, 36, 15 },
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);
                        serialisedKeyContainer = keyBytes;
                        System.Xml.Serialization.XmlSerializer mySerializer = new System.Xml.Serialization.XmlSerializer(typeof(KeyContainerClass));
                        using (MemoryStream ms = new System.IO.MemoryStream(serialisedKeyContainer))
                        {
                            keyContainers.Add((KeyContainerClass) mySerializer.Deserialize(ms));
                        }
                    }
                    catch (Exception)
                    {
                        try
                        {
                            serialisedKeyContainer = Convert.FromBase64String(kvp.Value);
                            System.Xml.Serialization.XmlSerializer mySerializer = new System.Xml.Serialization.XmlSerializer(typeof(KeyContainerClass));
                            using (MemoryStream ms = new MemoryStream(serialisedKeyContainer))
                            {
                                keyContainers.Add((KeyContainerClass) mySerializer.Deserialize(ms));
                            }
                        }
                        catch (Exception)
                        {
                            // It's not a valid entry so ignore it and move on
                            continue;
                        }
                    }
                    
                }
            }
            return keyContainers.ToArray();
        }

        private void m_btnOK_Click(object sender, EventArgs e)
        {
            ulong port = 0;
            try
            {
                if (this.textBoxPort.Text.Length > 0)
                {
                    port = ulong.Parse(this.textBoxPort.Text);
                    if (port <= 0 || port > 65535)
                        throw new ArgumentOutOfRangeException();
                    if (port == _host.CustomConfig.GetULong("KeePassRPC.connection.port", 12536))
                        throw new ArgumentException("The legacy KeePassRPC connection system is configured to use the port you have selected so please select a different port.");
                    if (port == 19455)
                        throw new ArgumentException("Port 19455 is commonly used by the unrelated KeePassHTTP plugin so please select a different port.");
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageBox.Show("Invalid listening port. Type a number between 1 and 65535 or leave empty to use the default port.");
                DialogResult = DialogResult.None;
                return;
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message);
                DialogResult = DialogResult.None;
                return;
            }

            long expTime = 8760;
            try
            {
                expTime = long.Parse(this.textBoxAuthExpiry.Text);
            }
            catch (Exception)
            {
                MessageBox.Show("Invalid expiry time.");
                DialogResult = DialogResult.None;
                return;
            }

            if (expTime < 1)
            {
                expTime = 1;
                MessageBox.Show("Expiry time set to 1 hour. This is the minimum allowed.");
            }

            if (expTime > 876000)
            {
                expTime = 876000;
                MessageBox.Show("Expiry time set to 100 years. This is the maximum allowed.");
            }

            long secLevel = 2;
            long secLevelClientMin = 2;
            switch ((string)comboBoxSecLevelKeePass.SelectedItem)
            {
                case "Low": secLevel = 1; break;
                case "Medium": secLevel = 2; break;
                default: secLevel = 3; break;
            }
            switch ((string)comboBoxSecLevelMinClient.SelectedItem)
            {
                case "Low": secLevelClientMin = 1; break;
                case "Medium": secLevelClientMin = 2; break;
                default: secLevelClientMin = 3; break;
            }

            _host.CustomConfig.SetBool("KeePassRPC.KeeFox.autoCommit", checkBox1.Checked);
            _host.CustomConfig.SetBool("KeePassRPC.KeeFox.editNewEntries", checkBox2.Checked);
            _host.CustomConfig.SetBool("KeePassRPC.KeeFox.backupNewPasswords", checkBox3.Checked);
            _host.CustomConfig.SetLong("KeePassRPC.AuthorisationExpiryTime", expTime * 3600);
            _host.CustomConfig.SetLong("KeePassRPC.SecurityLevel", secLevel);
            _host.CustomConfig.SetLong("KeePassRPC.SecurityLevelClientMinimum", secLevelClientMin);

            if (port > 0)
                _host.CustomConfig.SetULong("KeePassRPC.webSocket.port", port);

            _host.MainWindow.Invoke((MethodInvoker)delegate { _host.MainWindow.SaveConfig(); });
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            GlobalWindowManager.AddWindow(this);

            // Prevent one cell being blue by default to reduce distraction
            dataGridView1.ClearSelection();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            GlobalWindowManager.RemoveWindow(this);
        }

        private void comboBoxSecLevelKeePass_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxSecLevelKeePass.SelectedItem == "Low")
                labelSecLevelWarning.Text = "A low security setting could increase the chance of your passwords being stolen. Please make sure you read the information in the manual (see link above).";
            else if (comboBoxSecLevelKeePass.SelectedItem == "High")
                labelSecLevelWarning.Text = "A high security setting will require you to enter a randomly generated password every time you start KeePass or its client. A medium setting should suffice in most situations, especially if you set a low authorisation timeout below.";
            else
                labelSecLevelWarning.Text = "";
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 4)
            {
                string username = (string)dataGridView1.Rows[e.RowIndex].Cells[1].Value;

                // Revoke authorisation by deleting stored key data
                _host.CustomConfig.SetString("KeePassRPC.Key." + username, null);

                // If this connection is active, destroy it now
                foreach (KeePassRPCClientConnection client in _plugin.GetConnectedRPCClients())
                    if (!string.IsNullOrEmpty(client.UserName) && client.UserName == username)
                    {
                        client.WebSocketConnection.Close();
                        break;
                    }

                // Refresh the view
                dataGridView1.Rows.RemoveAt(e.RowIndex);
                dataGridView1.Refresh();
                //UpdateAuthorisedConnections();
            }
        }

    }
}
