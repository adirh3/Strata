using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class StrataActionReceiptTests(AvaloniaFixture fixture)
{
    [Fact]
    public Task ReceiptStartsCompactAndRevealsTheExactSnapshot() => fixture.Dispatch(() =>
    {
        var receipt = new StrataActionReceipt { Title = "Compare 2 results", Summary = "Garden \u00b7 City", Detail = "Garden: USD 180\nCity: USD 220", Action = "compare", TouchMode = true };
        var window = Host(receipt);
        try
        {
            Assert.False(receipt.IsExpanded);
            Assert.Equal(HorizontalAlignment.Right, receipt.HorizontalAlignment);
            Assert.Empty(receipt.GetVisualDescendants().OfType<SelectableTextBlock>());
            var button = receipt.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Disclosure");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(receipt.IsExpanded);
            Assert.Equal(["USD 180", "USD 220"], receipt.GetVisualDescendants().OfType<SelectableTextBlock>().Select(text => text.Text));
            Assert.Contains(receipt.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Garden");
            Assert.True(button.MinHeight >= 48);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SnapshotKeepsKeyedInputsAlignedAndProseReadable() => fixture.Dispatch(() =>
    {
        var snapshot = InteractiveSnapshotView.Create("Budget: $1,200\nPace: Relaxed\n\nGarden stay\nSample: $180 / night\nQuiet and green.");
        var window = StrataInteractiveBlockTests.Show(snapshot);
        try
        {
            var values = snapshot.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();
            Assert.Equal(["$1,200", "Relaxed", "Garden stay", "Sample: $180 / night", "Quiet and green."], values.Select(text => text.Text));
            Assert.Contains("interactive-snapshot-title", values[2].Classes);
            Assert.Equal(2, snapshot.GetVisualDescendants().OfType<Grid>().Count());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SourceLinkRequestsNavigationNotAnAgentTurn() => fixture.Dispatch(() =>
    {
        var receipt = new StrataActionReceipt { Title = "Use assumptions" };
        var window = Host(receipt);
        try
        {
            var calls = 0;
            receipt.SourceRequested += (_, _) => calls++;
            receipt.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_Source")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            receipt.Status = "ReplyReady";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(receipt.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Reply ready");
            receipt.Status = "Failed";
            Assert.Contains(receipt.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Needs attention");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SubmittedInputCollapsesToAReadOnlySummary() => fixture.Dispatch(() =>
    {
        var form = InteractiveDefinition.Parse("form", """{"id":"x","title":"Preferences","fields":[{"id":"name","label":"Name","required":true},{"id":"city","label":"City"}]}""");
        var state = InteractiveState.Create(form);
        state.Values["name"] = "Adir";
        var control = new StrataInteractiveBlock { Definition = form, StateJson = state.ToJson(), InputStatus = "Submitted", IsHosted = true };
        var window = Host(control);
        try
        {
            Assert.Empty(control.GetVisualDescendants().OfType<TextBox>());
            Assert.Empty(control.GetVisualDescendants().OfType<StrataActionReceipt>());
            Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Name: Adir") == true);
            Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Sent");
            control.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_InputDisclosure")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(control.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == "Adir");
            Assert.DoesNotContain(control.GetVisualDescendants().OfType<Button>(), button => button.Name?.StartsWith("Action_") == true);
        }
        finally { window.Close(); }
    });

    private static Window Host(TemplatedControl control)
    {
        var window = StrataInteractiveBlockTests.Show(control);
        control.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public Task AcceptedInputDoesNotSendASecondEditToAnExpiredRequest() => fixture.Dispatch(async () =>
    {
        var form = InteractiveDefinition.Parse("form", """{"id":"x","title":"Preferences","fields":[{"id":"name","label":"Name","required":true,"value":"Adir"}]}""");
        var control = new StrataInteractiveBlock { Definition = form, InputStatus = "Waiting", CanSubmit = true, IsHosted = true };
        var edits = 0;
        control.Requested += (_, request) =>
        {
            if (request.Action is not null)
                control.InputStatus = "Submitted";
            else
                edits++;
            request.Completion = Task.FromResult(request.Action is null && control.InputStatus == "Submitted"
                ? new InteractiveActionResult(false, "This request has ended")
                : new InteractiveActionResult(true));
        };
        var window = Host(control);
        try
        {
            var editsBeforeSubmission = edits;
            Assert.True((await control.SubmitAsync("submit")).Accepted);
            Assert.Equal(editsBeforeSubmission, edits);
            Assert.DoesNotContain(control.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsVisible && text.Text == "This request has ended");
        }
        finally { window.Close(); }
    });
}
