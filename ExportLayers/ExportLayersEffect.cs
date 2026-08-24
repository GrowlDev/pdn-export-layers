using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;

namespace ExportLayersPlugin
{
    public sealed class ExportLayersConfigToken : EffectConfigToken
    {
        /// <summary>Explicit destination folder; empty means "auto" (folder named after the .pdn file).</summary>
        public string CustomFolder = string.Empty;

        public bool VisibleLayersOnly = false;

        public bool OverwriteExisting = true;

        /// <summary>
        /// Set once the user has confirmed an export. Makes "Repeat Effect" (Ctrl+F)
        /// re-export silently with the same settings.
        /// </summary>
        public bool ExportRequested = false;

        public ExportLayersConfigToken()
        {
        }

        private ExportLayersConfigToken(ExportLayersConfigToken copyMe)
            : base(copyMe)
        {
            CustomFolder = copyMe.CustomFolder;
            VisibleLayersOnly = copyMe.VisibleLayersOnly;
            OverwriteExisting = copyMe.OverwriteExisting;
            ExportRequested = copyMe.ExportRequested;
        }

        public override object Clone()
        {
            return new ExportLayersConfigToken(this);
        }
    }

    /// <summary>
    /// "Effects > Tools > Export Layers to PNGs..." — exports each layer of the document as a
    /// canvas-sized PNG, named after the layer.
    ///
    /// The image itself is never modified (Render is an identity copy). The export runs in
    /// exactly one of two places, never in the tiled Render callbacks:
    ///  - from the config dialog's Export button (normal path), or
    ///  - once per effect instance in OnSetRenderInfo when invoked without a dialog,
    ///    i.e. "Repeat Effect" / Ctrl+F (silent re-export).
    /// </summary>
    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public sealed class ExportLayersEffect : Effect
    {
        public const string StaticName = "Export Layers to PNGs";

        // 0 = not yet exported by this effect instance; 1 = done. Guards against the apply-time
        // OnSetRenderInfo call re-running an export the dialog already performed, and against
        // any repeated OnSetRenderInfo calls within one invocation.
        private int exportDone;

        // True when this instance was invoked from the menu (a dialog will handle the export).
        // False for "Repeat Effect", where no dialog is created and OnSetRenderInfo exports.
        // Volatile: written on the UI thread, read from render worker threads.
        private volatile bool dialogCreated;

        public ExportLayersEffect()
            : base(StaticName, CreateMenuIcon(), "Tools", new EffectOptions { Flags = EffectFlags.Configurable })
        {
        }

        internal void MarkExportDone()
        {
            Interlocked.Exchange(ref exportDone, 1);
        }

        public override EffectConfigDialog CreateConfigDialog()
        {
            dialogCreated = true;
            return new ExportLayersConfigDialog();
        }

        protected override void OnSetRenderInfo(EffectConfigToken parameters, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            if (!dialogCreated
                && parameters is ExportLayersConfigToken token
                && token.ExportRequested
                && Interlocked.Exchange(ref exportDone, 1) == 0)
            {
                try
                {
                    LayerExporter.Export(((IEffect)this).Environment, token);
                }
                catch (Exception ex)
                {
                    string message = ex is ExportException ? ex.Message : ex.ToString();
                    MessageBox.Show(
                        "Export Layers failed:\n\n" + message,
                        StaticName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }

            base.OnSetRenderInfo(parameters, dstArgs, srcArgs);
        }

        public override void Render(EffectConfigToken parameters, RenderArgs dstArgs, RenderArgs srcArgs, Rectangle[] rois, int startIndex, int length)
        {
            // The effect never changes the image; copy source through unchanged.
            dstArgs.Surface.CopySurface(srcArgs.Surface, rois, startIndex, length);
        }

        private static Image CreateMenuIcon()
        {
            // Small "stacked layers with an arrow" glyph, drawn in code so no resources are needed.
            Bitmap icon = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(icon))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen edge = new Pen(Color.FromArgb(70, 90, 120), 1f))
                using (SolidBrush topFill = new SolidBrush(Color.FromArgb(150, 190, 235)))
                using (SolidBrush midFill = new SolidBrush(Color.FromArgb(110, 150, 200)))
                using (SolidBrush arrow = new SolidBrush(Color.FromArgb(40, 140, 60)))
                {
                    g.FillRectangle(midFill, 2, 7, 9, 6);
                    g.DrawRectangle(edge, 2, 7, 9, 6);
                    g.FillRectangle(topFill, 4, 3, 9, 6);
                    g.DrawRectangle(edge, 4, 3, 9, 6);
                    g.FillPolygon(arrow, new[]
                    {
                        new PointF(12f, 8f),
                        new PointF(12f, 11f),
                        new PointF(10f, 11f),
                        new PointF(13.5f, 15f),
                        new PointF(16f, 11f),
                        new PointF(14f, 11f),
                        new PointF(14f, 8f),
                    });
                }
            }
            return icon;
        }
    }
}
