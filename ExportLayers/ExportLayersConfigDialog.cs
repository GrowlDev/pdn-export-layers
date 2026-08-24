using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PaintDotNet.Effects;

namespace ExportLayersPlugin
{
    public sealed class ExportLayersConfigDialog : EffectConfigDialog
    {
        private TextBox txtFolder;
        private Button btnBrowse;
        private CheckBox chkVisibleOnly;
        private CheckBox chkOverwrite;
        private Label lblFolder;
        private Label lblAutoNote;
        private Label lblSummary;
        private Button btnExport;
        private Button btnCancel;

        // The computed "auto" destination (folder named after the .pdn file), or null if the
        // document has never been saved. When the textbox still shows this value, the token
        // keeps CustomFolder empty so future exports follow the document if it is renamed.
        private string autoFolder;

        public ExportLayersConfigDialog()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = ExportLayersEffect.StaticName;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 232);

            lblFolder = new Label
            {
                Text = "Destination folder:",
                Location = new Point(12, 14),
                AutoSize = true,
            };

            txtFolder = new TextBox
            {
                Location = new Point(15, 36),
                Size = new Size(452, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            btnBrowse = new Button
            {
                Text = "...",
                Location = new Point(473, 35),
                Size = new Size(32, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            btnBrowse.Click += OnBrowseClicked;

            lblAutoNote = new Label
            {
                Location = new Point(15, 66),
                Size = new Size(490, 34),
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            chkVisibleOnly = new CheckBox
            {
                Text = "Export visible layers only",
                Location = new Point(15, 106),
                AutoSize = true,
            };

            chkOverwrite = new CheckBox
            {
                Text = "Overwrite existing files (otherwise numbered names are used)",
                Location = new Point(15, 131),
                AutoSize = true,
                Checked = true,
            };

            lblSummary = new Label
            {
                Location = new Point(15, 162),
                Size = new Size(490, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            btnExport = new Button
            {
                Text = "Export",
                Size = new Size(90, 28),
                Location = new Point(322, 192),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            btnExport.Click += OnExportClicked;

            btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(90, 28),
                Location = new Point(418, 192),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel,
            };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
            {
                lblFolder, txtFolder, btnBrowse, lblAutoNote,
                chkVisibleOnly, chkOverwrite, lblSummary, btnExport, btnCancel,
            });

            AcceptButton = btnExport;
            CancelButton = btnCancel;

            ResumeLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshSummary();
            btnExport.Focus();
        }

        private void RefreshSummary()
        {
            try
            {
                var document = ((IEffect)Effect)?.Environment?.Document;
                if (document != null)
                {
                    int total = document.Layers.Count;
                    int visible = 0;
                    foreach (var layer in document.Layers)
                    {
                        if (layer.Visible)
                        {
                            visible++;
                        }
                    }
                    var size = document.Size;
                    lblSummary.Text = $"{total} layer(s) ({visible} visible)  •  each PNG will be {size.Width}×{size.Height} px";
                }
            }
            catch
            {
                lblSummary.Text = string.Empty;
            }
        }

        // ---- EffectConfigDialog contract ----

        protected override void InitialInitToken()
        {
            theEffectToken = new ExportLayersConfigToken();
        }

        protected override void InitDialogFromToken(EffectConfigToken effectTokenCopy)
        {
            var token = (ExportLayersConfigToken)effectTokenCopy;

            autoFolder = LayerExporter.TryGetAutoFolder();

            if (!string.IsNullOrWhiteSpace(token.CustomFolder))
            {
                txtFolder.Text = token.CustomFolder;
            }
            else if (autoFolder != null)
            {
                txtFolder.Text = autoFolder;
            }
            else
            {
                txtFolder.Text = PluginSettings.TryGetLastCustomFolder() ?? string.Empty;
            }

            if (autoFolder != null)
            {
                lblAutoNote.Text = "Exports into a folder named after the document, next to the .pdn file. " +
                    "Edit the path to use a fixed folder instead.";
            }
            else
            {
                lblAutoNote.Text = "This document has not been saved, so choose a destination folder. " +
                    "Once the document is saved, exports go to a folder named after it automatically.";
            }

            chkVisibleOnly.Checked = token.VisibleLayersOnly;
            chkOverwrite.Checked = token.OverwriteExisting;
        }

        protected override void InitTokenFromDialog()
        {
            var token = (ExportLayersConfigToken)theEffectToken;

            string text = txtFolder.Text.Trim();
            token.CustomFolder = (autoFolder != null && string.Equals(text, autoFolder, StringComparison.OrdinalIgnoreCase))
                ? string.Empty
                : text;
            token.VisibleLayersOnly = chkVisibleOnly.Checked;
            token.OverwriteExisting = chkOverwrite.Checked;
        }

        // ---- actions ----

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the folder to export the layer PNGs into.";
                dialog.UseDescriptionForTitle = true;
                string current = txtFolder.Text.Trim();
                if (current.Length > 0 && Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnExportClicked(object sender, EventArgs e)
        {
            InitTokenFromDialog();
            var token = (ExportLayersConfigToken)theEffectToken;
            token.ExportRequested = true;

            // Push the token (with ExportRequested set) through the normal token-update path so
            // the copy Paint.NET stores for "Repeat Effect" definitely carries the flag. The
            // preview render this triggers does not export (the effect knows a dialog owns it).
            FinishTokenUpdate();

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                LayerExporter.Export(((IEffect)Effect).Environment, token);
            }
            catch (Exception ex)
            {
                Cursor = previousCursor;
                string message = ex is ExportException ? ex.Message : ex.ToString();
                MessageBox.Show(this, message, ExportLayersEffect.StaticName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (token.CustomFolder.Length > 0)
            {
                PluginSettings.SaveLastCustomFolder(token.CustomFolder);
            }

            // The export already ran; make sure the apply-time OnSetRenderInfo does not run it again.
            ((ExportLayersEffect)Effect).MarkExportDone();

            // Close with OK so Paint.NET stores the token: Ctrl+F ("Repeat Effect") then
            // re-exports silently with these settings.
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
