// Minimal repro: Microsoft.UI.Reactor TreeView does not render per-node
// ContentElement content.
//
// Expected: each TreeView row shows its ContentElement (a Button, a TextBlock,
//           a CheckBox).
// Actual:   every row is blank. The elements are mounted (they exist as
//           TreeViewNode.Content) but never displayed.
//
// Root cause: when any node has a ContentElement, MountTreeView sets the
// TreeView's ItemTemplate to the shared *empty* ContentControl shell
// (Reconciler.SharedContentControlTemplate = "<ContentControl/>", no Content
// binding) and assigns the mounted UIElement to TreeViewNode.Content. For
// ListView/GridView the framework wires ContainerContentChanging to push that
// UIElement into the shell's ContentControl — but the TreeView mount/update
// path (MountTreeView / UpdateTreeView, and the descriptor TreeChildren) never
// does, so the ContentControl stays empty.
//
// Suggested fix: either give the TreeView shell template a Content binding
// (<ContentControl Content="{Binding Content}"/>, DataContext = TreeViewNode),
// or populate node containers the way the ListView/GridView path does.
//
// Run:
//   dotnet run                 → window with three blank rows (the bug, visible)
//   dotnet run -- --check      → headless self-check; prints PASS/BUG and sets
//                                exit code 0 (rendered) / 2 (not rendered)

using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

ReactorApp.Run<App>("Reactor TreeView ContentElement bug", width: 480, height: 360);

class App : Component
{
    public override Element Render()
    {
        // SelectionMode defaults to Single, so there is no selection checkbox —
        // anything you see in a row is genuinely the node's ContentElement.
        var nodes = new[]
        {
            new TreeViewNodeData("n1") { ContentElement = Button("I am a Button"), IsExpanded = true },
            new TreeViewNodeData("n2") { ContentElement = TextBlock("I am a TextBlock") },
            new TreeViewNodeData("n3") { ContentElement = CheckBox(true, label: "I am a CheckBox") },
        };

        var treeRef = UseRef<WinUI.TreeView?>(null);

        // Optional headless self-check: after layout, walk the live visual tree.
        // If no Button is realized, the ContentElement content never rendered.
        UseEffect(() =>
        {
            if (!Environment.GetCommandLineArgs().Contains("--check")) return () => { };
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(700);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var tv = treeRef.Current;
                tv?.UpdateLayout();
                bool rendered = HasDescendantOfType(tv, "Button");
                Console.Error.WriteLine(rendered
                    ? "[check] PASS: ContentElement Button is in the visual tree."
                    : "[check] BUG: no ContentElement content rendered — TreeView rows are empty.");
                ReactorApp.Exit(rendered ? 0 : 2);
            };
            timer.Start();
            return () => timer.Stop();
        });

        return VStack(12,
            TextBlock("Each row below should show a Button / TextBlock / CheckBox:").Bold(),
            TreeView(nodes).Height(240).Set(tv => treeRef.Current = tv)
        ).Padding(16);
    }

    static bool HasDescendantOfType(DependencyObject? root, string typeName)
    {
        if (root is null) return false;
        if (root.GetType().Name == typeName) return true;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (HasDescendantOfType(VisualTreeHelper.GetChild(root, i), typeName)) return true;
        return false;
    }
}
