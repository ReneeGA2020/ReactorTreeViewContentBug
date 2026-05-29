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

& $exe            # window opens with three EMPTY rows
& $exe -- --check # headless: walks the visual tree, prints PASS/BUG, exit 0/2
```

`Program.cs` builds the smallest possible tree:

```csharp
var nodes = new[]
{
    new TreeViewNodeData("n1") { ContentElement = Button("I am a Button"), IsExpanded = true },
    new TreeViewNodeData("n2") { ContentElement = TextBlock("I am a TextBlock") },
    new TreeViewNodeData("n3") { ContentElement = CheckBox(true, label: "I am a CheckBox") },
};
TreeView(nodes)
```

## Expected vs. actual

| | Expected | Actual |
|---|---|---|
| Row n1 | a `Button` | empty |
| Row n2 | "I am a TextBlock" | empty |
| Row n3 | a `CheckBox` | empty |

`--check` output: `[check] BUG: no ContentElement content rendered — TreeView rows are empty.` (exit code 2)

A visual-tree walk finds **zero** `Button` controls realized, confirming the
content never enters the tree (the elements are mounted as `TreeViewNode.Content`
but nothing hosts them).

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
