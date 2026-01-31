using Gallery.Domain.Routing;

namespace Gallery.Tests;

/// <summary>
/// Tests for HandleSecondInstanceActivation per outcome matrix.
/// </summary>
[TestFixture]
public class ActivationHandlerTests
{
    private const string TestWorkspaceKey = "88b49a59944589bd4779b7931d127abc";

    #region Mocks

    private sealed class MockWindowManager : IWindowManager
    {
        public bool IsMinimized { get; set; }
        public bool IsForeground { get; set; }
        public bool IsValid { get; set; } = true;

        public bool BringToFrontCalled { get; private set; }
        public bool RestoreFromMinimizedCalled { get; private set; }
        public bool FlashTaskbarCalled { get; private set; }
        public string? NavigatedToView { get; private set; }

        public void BringToFront() => BringToFrontCalled = true;
        public void RestoreFromMinimized() => RestoreFromMinimizedCalled = true;
        public void FlashTaskbar() => FlashTaskbarCalled = true;
        public void NavigateTo(string view) => NavigatedToView = view;
    }

    private sealed class MockIndexLoader : IIndexLoader
    {
        public bool RefreshCalled { get; private set; }
        public void Refresh() => RefreshCalled = true;
    }

    #endregion

    #region Outcome Matrix Tests (per spec)

    [Test]
    public void Background_NoView_BroughtToFrontAndRefreshed()
    {
        // | Background | No | `[BroughtToFront, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = false, IsForeground = false };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.BroughtToFront));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.Outcomes, Has.Count.EqualTo(2));
        Assert.That(window.BringToFrontCalled, Is.True);
        Assert.That(loader.RefreshCalled, Is.True);
    }

    [Test]
    public void Background_WithView_BroughtToFrontNavigatedRefreshed()
    {
        // | Background | Yes | `[BroughtToFront, NavigatedToView, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = false, IsForeground = false };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test", RequestedView = "jobs" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.BroughtToFront));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.NavigatedToView));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.Outcomes, Has.Count.EqualTo(3));
        Assert.That(result.NavigatedTo, Is.EqualTo("jobs"));
        Assert.That(window.NavigatedToView, Is.EqualTo("jobs"));
    }

    [Test]
    public void Foreground_NoView_AlreadyForegroundRefreshed()
    {
        // | Foreground | No | `[AlreadyForeground, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = false, IsForeground = true };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.AlreadyForeground));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.Outcomes, Has.Count.EqualTo(2));
        Assert.That(window.BringToFrontCalled, Is.False);
    }

    [Test]
    public void Foreground_WithView_AlreadyForegroundNavigatedRefreshed()
    {
        // | Foreground | Yes | `[AlreadyForeground, NavigatedToView, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = false, IsForeground = true };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test", RequestedView = "settings" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.AlreadyForeground));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.NavigatedToView));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.NavigatedTo, Is.EqualTo("settings"));
    }

    [Test]
    public void Minimized_NoView_RestoredFlashedRefreshed()
    {
        // | Minimized | No | `[RestoredFromMinimized, TaskbarFlashed, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = true, IsForeground = false };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RestoredFromMinimized));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.TaskbarFlashed));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.Outcomes, Has.Count.EqualTo(3));
        Assert.That(window.RestoreFromMinimizedCalled, Is.True);
        Assert.That(window.FlashTaskbarCalled, Is.True);
    }

    [Test]
    public void Minimized_WithView_RestoredFlashedNavigatedRefreshed()
    {
        // | Minimized | Yes | `[RestoredFromMinimized, TaskbarFlashed, NavigatedToView, RefreshedIndex]` |
        var window = new MockWindowManager { IsMinimized = true, IsForeground = false };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test", RequestedView = "gallery" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RestoredFromMinimized));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.TaskbarFlashed));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.NavigatedToView));
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.RefreshedIndex));
        Assert.That(result.Outcomes, Has.Count.EqualTo(4));
        Assert.That(result.NavigatedTo, Is.EqualTo("gallery"));
    }

    #endregion

    #region Error Scenarios

    [Test]
    public void WindowUnavailable_ReturnsError()
    {
        var window = new MockWindowManager { IsValid = false };
        var loader = new MockIndexLoader();

        var result = ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.Outcomes, Contains.Item(ActivationOutcome.ErrorWindowUnavailable));
        Assert.That(result.Outcomes, Has.Count.EqualTo(1));
        Assert.That(loader.RefreshCalled, Is.False); // Should not refresh on error
    }

    #endregion

    #region Response Payload Conversion

    [Test]
    public void ToResponsePayload_Success_HasCorrectFields()
    {
        var result = ActivationResult.SuccessWithNavigation("jobs",
            ActivationOutcome.BroughtToFront,
            ActivationOutcome.NavigatedToView,
            ActivationOutcome.RefreshedIndex);

        var payload = ActivationHandler.ToResponsePayload(result);

        Assert.That(payload.Status, Is.EqualTo("activated"));
        Assert.That(payload.WindowState, Is.EqualTo("restored"));
        Assert.That(payload.NavigatedTo, Is.EqualTo("jobs"));
        Assert.That(payload.Error, Is.Null);
    }

    [Test]
    public void ToResponsePayload_Minimized_WindowStateRestored()
    {
        var result = ActivationResult.Success(
            ActivationOutcome.RestoredFromMinimized,
            ActivationOutcome.TaskbarFlashed,
            ActivationOutcome.RefreshedIndex);

        var payload = ActivationHandler.ToResponsePayload(result);

        Assert.That(payload.Status, Is.EqualTo("activated"));
        Assert.That(payload.WindowState, Is.EqualTo("restored"));
    }

    [Test]
    public void ToResponsePayload_AlreadyForeground_WindowStateCorrect()
    {
        var result = ActivationResult.Success(
            ActivationOutcome.AlreadyForeground,
            ActivationOutcome.RefreshedIndex);

        var payload = ActivationHandler.ToResponsePayload(result);

        Assert.That(payload.Status, Is.EqualTo("activated"));
        Assert.That(payload.WindowState, Is.EqualTo("already_foreground"));
    }

    [Test]
    public void ToResponsePayload_Error_HasErrorMessage()
    {
        var result = ActivationResult.Error(
            ActivationOutcome.ErrorWindowUnavailable,
            "Window handle is invalid");

        var payload = ActivationHandler.ToResponsePayload(result);

        Assert.That(payload.Status, Is.EqualTo("error"));
        Assert.That(payload.Error, Is.EqualTo("Window handle is invalid"));
    }

    #endregion

    #region Index Refresh

    [Test]
    public void AlwaysRefreshesIndex_EvenWhenAlreadyForeground()
    {
        var window = new MockWindowManager { IsMinimized = false, IsForeground = true };
        var loader = new MockIndexLoader();

        ActivationHandler.HandleSecondInstanceActivation(
            new ActivationRequestPayload { WorkspacePath = @"C:\Test" },
            TestWorkspaceKey,
            window,
            loader);

        Assert.That(loader.RefreshCalled, Is.True);
    }

    #endregion

    #region ActivationOutcome Enum Tests

    [Test]
    public void ErrorOutcomes_AreGreaterThan100()
    {
        // Error outcomes should be >= 100 for IsError check
        Assert.That((int)ActivationOutcome.ErrorInvalidMessage, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)ActivationOutcome.ErrorUnsupportedVersion, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)ActivationOutcome.ErrorWindowUnavailable, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)ActivationOutcome.ErrorWorkspaceKeyMismatch, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)ActivationOutcome.ErrorMessageTooLarge, Is.GreaterThanOrEqualTo(100));
        Assert.That((int)ActivationOutcome.ErrorInvalidKeyFormat, Is.GreaterThanOrEqualTo(100));
    }

    [Test]
    public void SuccessOutcomes_AreLessThan100()
    {
        Assert.That((int)ActivationOutcome.BroughtToFront, Is.LessThan(100));
        Assert.That((int)ActivationOutcome.AlreadyForeground, Is.LessThan(100));
        Assert.That((int)ActivationOutcome.RestoredFromMinimized, Is.LessThan(100));
        Assert.That((int)ActivationOutcome.NavigatedToView, Is.LessThan(100));
        Assert.That((int)ActivationOutcome.RefreshedIndex, Is.LessThan(100));
        Assert.That((int)ActivationOutcome.TaskbarFlashed, Is.LessThan(100));
    }

    #endregion
}
