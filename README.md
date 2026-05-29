# Reactor `TreeView` does not render per-node `ContentElement`

Minimal repro for Microsoft.UI.Reactor (experimental).

## Summary

A `TreeView` whose `TreeViewNodeData` nodes carry a `ContentElement` shows
**blank rows** — the per-node content (a `Button`, `TextBlock`, `CheckBox`, …)
is never displayed. `ListView` / `GridView` with element items render fine; only
`TreeView` is affected.

## Repro

```powershell
dotnet build .\ReactorTreeViewContentBug.csproj -c Debug -p:Platform=x64
$exe = ".\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\ReactorTreeViewContentBug.exe"

& $exe            # window: a BLANK TreeView above a working ListView (same content)
& $exe -- --check # headless: detects each control's content, prints results, exit 0/2
```

`Program.cs` puts the same kind of element content into a `TreeView` and a
`ListView` side by side:

```csharp
// TreeView — per-node ContentElement (BLANK at runtime)
TreeView(new[]
{
    new TreeViewNodeData("n1") { ContentElement = Button("TreeView: I am a Button"), IsExpanded = true },
    new TreeViewNodeData("n2") { ContentElement = TextBlock("TreeView: I am a TextBlock") },
    new TreeViewNodeData("n3") { ContentElement = CheckBox(true, label: "TreeView: I am a CheckBox") },
})

// ListView — element items (renders correctly; the contrast)
ListView(
    Button("ListView: I am a Button"),
    TextBlock("ListView: I am a TextBlock"),
    CheckBox(true, label: "ListView: I am a CheckBox"))
```

## Expected vs. actual

| Control | Same element content | Expected | Actual |
|---|---|---|---|
| `TreeView` | `Button` / `TextBlock` / `CheckBox` | visible | **all rows blank** |
| `ListView` | `Button` / `TextBlock` / `CheckBox` | visible | visible ✅ |

`--check` output (exit code 2):

```text
[check] ListView element-item content rendered:  True  (expected True)
[check] TreeView ContentElement content rendered: False  (expected True)
[check] BUG: TreeView ContentElement not rendered — a ListView with the same element content does render.
```

The check detects each item by the unique label text it carries (robust because
the TreeView's internal `TreeViewList` derives from `ListView`). The `TreeView`
content elements are mounted as `TreeViewNode.Content` but nothing hosts them, so
they never enter the visual tree; the `ListView` content does.

## Root cause

In `Reconciler.Mount.cs` `MountTreeView` (and the experimental
`V1Protocol/.../ChildrenStrategy.cs` `TreeChildren`), when any node has a
`ContentElement`:

```csharp
treeView.ItemTemplate = SharedContentControlTemplate.Value; // "<ContentControl .../>", no Content binding
...
node.Content = Mount(data.ContentElement, requestRerender);  // a UIElement
```

`SharedContentControlTemplate` (`Reconciler.cs`) is an **empty** `ContentControl`
shell:

```xml
<ContentControl HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>
```

For `ListView` / `GridView`, the framework wires `ContainerContentChanging`
(`HandleTemplatedContainerContentChanging`) to imperatively push
`node.Content` into that `ContentControl`. The `TreeView` path
(`MountTreeView` / `UpdateTreeView` / `TreeChildren`) **never does**, so the
shell's `Content` stays `null` and nothing is shown.

The text path works because the non-`ContentElement` template *does* bind:
`<TextBlock Text='{Binding Content.Content}'/>`.

## Suggested fix

Either give the TreeView shell template a `Content` binding so it resolves the
node's content (the item template's `DataContext` is the `TreeViewNode`):

```xml
<ContentControl Content='{Binding Content}' HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>
```

…or populate the TreeView's node containers imperatively the way the
ListView/GridView path already does.

## Workaround (consumer side)

Override `ItemTemplate` after Reactor sets it — `UpdateTreeView` never touches
`ItemTemplate`, so it sticks:

```csharp
TreeView(nodes).Set(tv => tv.ItemTemplate = (DataTemplate)XamlReader.Load(
    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
    "<ContentControl Content='{Binding Content}'/></DataTemplate>"));
```

## Environment

- microsoft-ui-reactor: experimental (April 2026), built from source via `ProjectReference`
- Microsoft.WindowsAppSDK 2.0.1, net10.0-windows10.0.22621.0, x64, unpackaged self-contained
- .NET 10 SDK, Windows 11
