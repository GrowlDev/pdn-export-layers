# Export Layers to PNGs — Paint.NET plugin

Exports every layer of the open Paint.NET document as a separate, canvas-sized,
32-bit RGBA PNG named after the layer. Built for and verified against
**Paint.NET 5.1.12** (classic install).

## Using it

1. Open a layered `.pdn` document.
2. **Effects > Tools > Export Layers to PNGs…**
3. Press **Enter** (the Export button is the default).

That's it. For a saved document `C:\Game\Art\spr_wall_breach.pdn`, the files go to a
folder named after it, next to it:

```
C:\Game\Art\spr_wall_breach\
    wall.png
    studs.png
    plaster.png
    debris.png
```

The folder is created if missing; existing files are overwritten by default (the folder
is treated as a derived-output folder, like a build directory).

### Repeat export while iterating

After the first export, press **Ctrl+F** ("Repeat Effect"). The layers are re-exported
silently with the same settings — no dialog. The destination is recomputed each time, so
if you rename or re-save the document elsewhere, exports follow it.

(Because the export runs as an effect, each export adds a no-op "Export Layers to PNGs"
entry to the History. It never changes the image; undoing it does nothing.)

### Dialog options

- **Destination folder** — prefilled with the automatic folder. Edit or browse to use a
  fixed folder instead; the choice is stored with the effect settings (and remembered as
  a prefill for unsaved documents).
- **Export visible layers only** — off by default (hidden layers are exported too).
- **Overwrite existing files** — on by default. When off, numbered names
  (`wall_2.png`, …) are used instead of replacing files.

### Behaviour details

- Every PNG has the full canvas size; layer content keeps its position. Nothing is
  cropped, and transparency is preserved (straight alpha, 32-bit RGBA).
- Layer **raw pixels** are exported: layer opacity and blend mode are *not* baked in
  (a half-opacity layer whose pixels are opaque exports as opaque pixels).
- Filenames come from layer names. Illegal filename characters (`\ / : * ? " < > |`,
  control chars) become `_`; leading/trailing whitespace and trailing dots are trimmed;
  empty names become `layer`; reserved device names (`CON`, …) get a `_` suffix.
- Duplicate layer names get `_2`, `_3`, … suffixes (case-insensitive).
- An unsaved document has no automatic destination — the dialog asks for a folder
  (and offers the last one you picked).

## Installation

Grab the zip from [Releases](https://github.com/GrowlDev/pdn-export-layers/releases).
Or build it yourself (see below) — the compiled plugin is a single file,
`ExportLayers\bin\Release\ExportLayers.dll`.

Either way, copy `ExportLayers.dll` to the per-user plugin folder (no admin needed —
Paint.NET 5.x scans this automatically):

```
%USERPROFILE%\Documents\Paint.NET App Files\Effects\ExportLayers.dll
```

or run `install-plugin.cmd`. Alternatively `C:\Program Files\paint.net\Effects\`
works too (requires admin). **Restart Paint.NET afterwards**; it only scans plugins at
startup. `uninstall-plugin.cmd` removes it again.

## Building from source

Requires a .NET SDK able to target `net9.0-windows` (SDK 9 or 10) and an installed
Paint.NET — the projects compile against its assemblies rather than shipping their own.

Defaults to `C:\Program Files\paint.net`. If yours is somewhere else, either set a
`PdnRoot` environment variable or pass it on the command line:

```
dotnet build -c Release -p:PdnRoot="D:\paint.net"
```

```
cd ExportLayers
dotnet build -c Release
```

## Tests

A console harness that runs the export pipeline against fake in-memory layers, covering
naming, duplicates, hidden layers, alpha roundtrip, stride handling and both overwrite
modes. Nothing in it needs Paint.NET running.

```
cd ExportLayers.Tests
dotnet run -c Release
```

It can also make and re-check `test-layers.pdn`, a small layered document for trying the
plugin by hand: `dotnet run -c Release -- makepdn test-layers.pdn`, then `verifypdn`.

## Architecture notes

- Classic `PaintDotNet.Effects.Effect` with a config dialog. In Paint.NET 5.x classic
  effects receive `IEffectEnvironment` with full document/layer access
  (`Environment.Document.Layers`), which is everything the export needs.
- The export deliberately **never runs in the `Render` callbacks** (which are called
  repeatedly, tiled, multi-threaded). It runs once per invocation: on the dialog's
  Export click, or once in `OnSetRenderInfo` when invoked dialog-less via Repeat Effect.
- The plugin API does not expose the document's file path. It is obtained by reflecting
  over Paint.NET's `AppWorkspace.ActiveDocumentWorkspace.FilePath` (verified present in
  5.1.12). If a future Paint.NET breaks this, the plugin degrades gracefully: auto mode
  reports "no destination" and the explicit folder choice still works.

## Known limitations

- The automatic destination folder depends on the reflection above (see previous point).
- Layer opacity/blend modes are not applied to exported pixels (intentional for
  game-asset workflows; the raw layer is what you drew).
- Exports overwrite silently by default — the destination folder should be treated as
  generated output.
- After a successful export the dialog just closes (like Save); errors show a message
  box. Success feedback is the files themselves.

MIT licensed. Source: `ExportLayers\` (plugin), `ExportLayers.Tests\` (harness).
