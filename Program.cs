// Minimal repro: Microsoft.UI.Reactor TreeView does not render per-node
// ContentElement content — while a ListView with the same element content does.
//
// Expected: each TreeView row shows its ContentElement (a Button, a TextBlock,
//           a CheckBox), exactly like the ListView below it.
// Actual:   every TreeView row is blank. The elements are mounted (they exist
//           as TreeViewNode.Content) but never displayed. The contrast ListView
//           renders the same kind of element content correctly.
//
// Root cause: when any node has a ContentElement, MountTreeView sets the
// TreeView's ItemTemplate to the shared *empty* ContentControl shell
// (Reconciler.SharedContentControlTemplate = "<ContentControl/>", no Content
// binding) and assigns the mounted UIElement to TreeViewNode.Content. For
// ListView/GridView, MountListView/MountGridView wire ContainerContentChanging
// to push that UIElement into the shell's ContentControl (cc.Content = Mount(...)).
// The TreeView path (MountTreeView / UpdateTreeView, and the descriptor
// TreeChildren) never does, so the ContentControl stays empty.
//
// Run:
//   dotnet run                 → window: blank TreeView rows above a working ListView
//   dotnet run -- --check      → headless self-check; prints both results and sets
//                                exit code 0 (TreeView rendered) / 2 (it did not)

using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

ReactorApp.Run<App>("Reactor TreeView ContentElement bug", width: 520, height: 480);

class App : Component
{
    public override Element Render()
    {
        // SelectionMode defaults to Single, so there is no selection checkbox —
        // anything visible in a TreeView row is genuinely the node's content.
        var treeNodes = new[]
        {
            new TreeViewNodeData("n1") { ContentElement = Button("TreeView: I am a Button"), IsExpanded = true },
            new TreeViewNodeData("n2") { ContentElement = TextBlock("TreeView: I am a TextBlock") },
            new TreeViewNodeData("n3") { ContentElement = CheckBox(true, label: "TreeView: I am a CheckBox") },
        };

        var treeRef = UseRef<WinUI.TreeView?>(null);

        // Headless self-check: after layout, detect each control's content by the
        // unique label text it carries. (Text detection is robust where type
        // walking is not — e.g. the TreeView's internal TreeViewList derives from
        // ListView, so a naive "find a ListView" walk hits the wrong control.)
        UseEffect(() =>
        {
            if (!Environment.GetCommandLineArgs().Contains("--check")) return () => { };
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(900);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var tv = treeRef.Current;
                tv?.UpdateLayout();

                DependencyObject? root = tv;
                while (root is not null && VisualTreeHelper.GetParent(root) is { } parent) root = parent;

                bool listOk = AnyTextBlockWith(root, "ListView: I am a Button");
                bool treeOk = AnyTextBlockWith(root, "TreeView: I am a Button");

                Console.Error.WriteLine($"[check] ListView element-item content rendered:  {listOk}  (expected True)");
                Console.Error.WriteLine($"[check] TreeView ContentElement content rendered: {treeOk}  (expected True)");
                Console.Error.WriteLine(treeOk
                    ? "[check] PASS: TreeView ContentElement rendered."
                    : "[check] BUG: TreeView ContentElement not rendered — a ListView with the same element content does render.");
                ReactorApp.Exit(treeOk ? 0 : 2);
            };
            timer.Start();
            return () => timer.Stop();
        });

        return VStack(12,
            TextBlock("Both controls host the same kind of element content:").Bold(),

            TextBlock("TreeView (per-node ContentElement) — rows are BLANK (the bug):"),
            TreeView(treeNodes).Height(150).Set(tv => treeRef.Current = tv),

            TextBlock("ListView (element items) — renders correctly (contrast):"),
            ListView(
                Button("ListView: I am a Button"),
                TextBlock("ListView: I am a TextBlock"),
                CheckBox(true, label: "ListView: I am a CheckBox")
            ).Height(150)
        ).Padding(16);
    }

    static bool AnyTextBlockWith(DependencyObject? root, string text)
    {
        if (root is null) return false;
        if (root is WinUI.TextBlock tb && tb.Text == text) return true;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (AnyTextBlockWith(VisualTreeHelper.GetChild(root, i), text)) return true;
        return false;
    }
}
