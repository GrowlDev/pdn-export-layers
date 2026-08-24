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
        // Empty means "auto", i.e. a folder named after the .pdn.
        public string CustomFolder = string.Empty;

        public bool VisibleLayersOnly = false;

        public bool OverwriteExisting = true;

        // Only true once the user has actually pressed Export. This is the whole trick behind
        // Ctrl+F: Paint.NET hangs onto a copy of the token, and a copy that comes back with
        // this set means "you already know what to do, get on with it".
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

    /// <summary>Effects &gt; Tools &gt; Export Layers to PNGs...</summary>
    // The thing to know before touching any of this: the export must not happen in Render().
    // Render is called once per tile, on several threads, so doing the work there writes every
    // layer a few dozen times and takes about as long as that sounds. Ask me how I know.
    // So there are exactly two places it can happen:
    //
    //   - the dialog's Export button, which is the normal path, or
    //   - OnSetRenderInfo, once, when there is no dialog. That one is Ctrl+F / Repeat Effect.
    //
    // The document itself is never touched. Render just copies the source straight through.
    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public sealed class ExportLayersEffect : Effect
    {
        public const string StaticName = "Export Layers to PNGs";

        // 0 = this instance hasn't exported, 1 = it has. Both of the paths above can fire
        // during a single invocation and I only ever want one set of files written.
        private int exportDone;

        // True if we came in from the menu, meaning a dialog exists and will do the exporting.
        // False for Repeat Effect. volatile because the UI thread writes it and the render
        // workers read it.
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
            // Nothing to do here. We are only an "effect" because that is how you get a menu
            // item and a config dialog out of Paint.NET.
            // TODO: it's also why every export leaves a do-nothing entry in the History list.
            // Haven't found a way round that yet without giving up the dialog.
            dstArgs.Surface.CopySurface(srcArgs.Surface, rois, startIndex, length);
        }

        private static Image CreateMenuIcon()
        {
            // Two stacked rectangles and a down arrow, drawn by hand so the plugin stays a
            // single DLL with nothing embedded in it. It is not art.
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
