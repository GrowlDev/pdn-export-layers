using System;
using System.Reflection;
using PaintDotNet;

namespace ExportLayersPlugin
{
    public sealed class PluginSupportInfo : IPluginSupportInfo
    {
        public string DisplayName => "Export Layers to PNGs";
        public string Author => "Tyson Young";
        public string Copyright => "MIT License";
        public Version Version => typeof(PluginSupportInfo).Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/");
    }
}
