<!-- Paste the body below into a new GitHub issue. Title suggestion: -->
<!-- TreeView does not render per-node `ContentElement` (ListView/GridView do) -->

### Summary

A `TreeView` whose `TreeViewNodeData` nodes carry a `ContentElement` shows
**blank rows** — the per-node content (`Button` / `TextBlock` / `CheckBox` / …)
is never displayed. A `ListView` given the **same** element content renders it
correctly, so the defect is specific to `TreeView`.

Minimal self-verifying repro: https://github.com/ReneeGA2020/ReactorTreeViewContentBug

### Repro

```csharp
// TreeView — per-node ContentElement (renders BLANK)
TreeView(new[]
{
    new TreeViewNodeData("n1") { ContentElement = Button("TreeView: I am a Button"), IsExpanded = true },
    new TreeViewNodeData("n2") { ContentElement = TextBlock("TreeView: I am a TextBlock") },
    new TreeViewNodeData("n3") { ContentElement = CheckBox(true, label: "TreeView: I am a CheckBox") },
})

// ListView — same element content (renders correctly)
ListView(
    Button("ListView: I am a Button"),
    TextBlock("ListView: I am a TextBlock"),
    CheckBox(true, label: "ListView: I am a CheckBox"))
```

```powershell
dotnet build -c Debug -p:Platform=x64
.\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\ReactorTreeViewContentBug.exe -- --check
```

### Expected vs. actual

| Control | Same content | Expected | Actual |
|---|---|---|---|
| `TreeView` | `Button` / `TextBlock` / `CheckBox` | visible | **all rows blank** |
| `ListView` | `Button` / `TextBlock` / `CheckBox` | visible | visible ✅ |

The bundled `--check` walks the live visual tree and detects each item by its
unique label text (robust because the TreeView's internal `TreeViewList` derives
from `ListView`):

```text
[check] ListView element-item content rendered:  True  (expected True)
[check] TreeView ContentElement content rendered: False  (expected True)
[check] BUG: TreeView ContentElement not rendered — a ListView with the same element content does render.
```

(process exits with code 2)

### Root cause

When any node has a `ContentElement`, `MountTreeView` (`Reconciler.Mount.cs`)
sets the TreeView's `ItemTemplate` to the shared **empty** `ContentControl`
shell and assigns the mounted `UIElement` to `TreeViewNode.Content`:

```csharp
treeView.ItemTemplate = SharedContentControlTemplate.Value; // <ContentControl/> — no Content binding
...
node.Content = Mount(data.ContentElement, requestRerender);  // a UIElement, but nothing hosts it
```

`SharedContentControlTemplate` (`Reconciler.cs`) is:

```xml
<ContentControl HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>
```

For `ListView` / `GridView`, `MountListView` / `MountGridView` wire
`ContainerContentChanging` to push the mounted element into that shell
(`cc.Content = Mount(items[args.ItemIndex], …)`). The `TreeView` paths —
`MountTreeView`, `UpdateTreeView`, and the experimental `TreeChildren` strategy
(`V1Protocol/.../ChildrenStrategy.cs`) — **never populate the shell**, so its
`Content` stays `null` and the row is empty.

The non-`ContentElement` (text) path works because its template binds:
`<TextBlock Text='{Binding Content.Content}'/>`.

### Suggested fix

Give the TreeView shell template a `Content` binding (the item template's
`DataContext` is the `TreeViewNode`, whose `.Content` is the mounted element):

```xml
<ContentControl Content='{Binding Content}' HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>
```

…or populate the TreeView node containers imperatively the way the
ListView/GridView path already does.

### Consumer-side workaround

`UpdateTreeView` never touches `ItemTemplate`, so an override in `.Set` sticks
across re-renders:

```csharp
TreeView(nodes).Set(tv => tv.ItemTemplate = (DataTemplate)XamlReader.Load(
    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
    "<ContentControl Content='{Binding Content}'/></DataTemplate>"));
```

### Environment

- microsoft-ui-reactor: experimental (April 2026), built from source via `ProjectReference`
- Microsoft.WindowsAppSDK 2.0.1, `net10.0-windows10.0.22621.0`, x64, unpackaged self-contained
- .NET 10 SDK, Windows 11
