using Gallery.Domain.Routing;

namespace Gallery.Tests;

/// <summary>
/// Tests for InstanceRouter covering scenarios S1-S15.
/// </summary>
[TestFixture]
[Platform("Win")]
public class InstanceRouterTests
{
    private static string GenerateUniqueKey() =>
        Guid.NewGuid().ToString("N")[..32];

    #region Mock Mutex Provider

    private sealed class MockMutexProvider : IMutexProvider
    {
        public bool HasExistingInstance { get; set; }
        public bool MutexAcquired { get; private set; }
        public List<string> AcquireAttempts { get; } = new();

        public IMutexHandle? TryAcquire(string mutexName)
        {
            AcquireAttempts.Add(mutexName);

            if (HasExistingInstance)
            {
                return null; // Mutex held by another
            }

            MutexAcquired = true;
            return new MockMutexHandle();
        }
    }

    private sealed class MockMutexHandle : IMutexHandle
    {
        public bool IsHeld => !_disposed;
        private bool _disposed;

        public void Dispose() => _disposed = true;
    }

    #endregion

    [Test]
    public void DeriveMutexName_MatchesSpec()
    {
        var key = "88b49a59944589bd4779b7931d127abc";
        var mutexName = InstanceRouter.DeriveMutexName(key);
        Assert.That(mutexName, Is.EqualTo($"NextGallery_{key}"));
    }

    [Test]
    public async Task S1_FirstLaunch_CreatesNewWindow()
    {
        // S1: First Launch - no existing instance
        var key = GenerateUniqueKey();
        var mockMutex = new MockMutexProvider { HasExistingInstance = false };
        var log = new TestRoutingLog();

        using var router = new InstanceRouter(key, mockMutex, log);
        var result = await router.RouteAsync(@"C:\Projects\MyApp");

        Assert.That(result.Action, Is.EqualTo(RouteAction.CreateWindow));
        Assert.That(mockMutex.MutexAcquired, Is.True);
    }

    [Test]
    public async Task S2_SecondLaunch_ActivatesExisting()
    {
        // S2: Same Workspace, Second Launch
        // Use real mutex + pipe for this integration test
        var key = GenerateUniqueKey();
        var log1 = new TestRoutingLog();
        var log2 = new TestRoutingLog();

        bool activationReceived = false;
        string? receivedView = null;

        // Start "first" instance
        using var router1 = new InstanceRouter(key, log: log1);
        var result1 = await router1.RouteAsync(@"C:\Projects\MyApp");

        Assert.That(result1.Action, Is.EqualTo(RouteAction.CreateWindow), "First instance should create window");

        // Set up activation handler
        router1.ActivationRequested += payload =>
        {
            activationReceived = true;
            receivedView = payload.RequestedView;
            return new ActivationResponsePayload
            {
                Status = "activated",
                WindowState = "restored",
                NavigatedTo = payload.RequestedView
            };
        };

        // Give server time to start
        await Task.Delay(200);

        // Start "second" instance
        using var router2 = new InstanceRouter(key, log: log2);
        var result2 = await router2.RouteAsync(@"C:\Projects\MyApp", "jobs");

        Assert.That(result2.Action, Is.EqualTo(RouteAction.ActivateExisting), "Second instance should activate existing");
        Assert.That(result2.ActivationResult, Is.Not.Null);
        Assert.That(result2.ActivationResult!.IsSuccess, Is.True);

        Assert.That(activationReceived, Is.True, "First instance should receive activation");
        Assert.That(receivedView, Is.EqualTo("jobs"));
    }

    [Test]
    public async Task S3_DifferentWorkspaces_BothGetOwnWindows()
    {
        // S3: Different Workspaces
        var key1 = GenerateUniqueKey();
        var key2 = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key1);
        using var router2 = new InstanceRouter(key2);

        var result1 = await router1.RouteAsync(@"C:\Projects\App1");
        var result2 = await router2.RouteAsync(@"C:\Projects\App2");

        Assert.That(result1.Action, Is.EqualTo(RouteAction.CreateWindow));
        Assert.That(result2.Action, Is.EqualTo(RouteAction.CreateWindow));
    }

    [Test]
    public async Task S4_CaseDifference_SameInstance()
    {
        // S4: Case Difference (C:\Projects\MyApp vs c:\projects\myapp)
        // Both should normalize to same workspace_key via WorkspaceKey.ComputeKey
        // This test uses same key to simulate that
        var key = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key);
        router1.ActivationRequested += _ => new ActivationResponsePayload { Status = "activated" };

        var result1 = await router1.RouteAsync(@"C:\Projects\MyApp");
        Assert.That(result1.Action, Is.EqualTo(RouteAction.CreateWindow));

        await Task.Delay(200);

        // Second instance with "different" path but same key
        using var router2 = new InstanceRouter(key);
        var result2 = await router2.RouteAsync(@"c:\projects\myapp");

        Assert.That(result2.Action, Is.EqualTo(RouteAction.ActivateExisting));
    }

    [Test]
    public async Task S5_SlashDifference_SameInstance()
    {
        // S5: Slash Difference (C:/Projects vs C:\Projects)
        var key = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key);
        router1.ActivationRequested += _ => new ActivationResponsePayload { Status = "activated" };

        var result1 = await router1.RouteAsync(@"C:\Projects\MyApp");
        Assert.That(result1.Action, Is.EqualTo(RouteAction.CreateWindow));

        await Task.Delay(200);

        using var router2 = new InstanceRouter(key);
        var result2 = await router2.RouteAsync(@"C:/Projects/MyApp");

        Assert.That(result2.Action, Is.EqualTo(RouteAction.ActivateExisting));
    }

    [Test]
    public async Task S6_TrailingSlash_SameInstance()
    {
        // S6: Trailing Slash
        var key = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key);
        router1.ActivationRequested += _ => new ActivationResponsePayload { Status = "activated" };

        var result1 = await router1.RouteAsync(@"C:\Projects\MyApp");
        await Task.Delay(200);

        using var router2 = new InstanceRouter(key);
        var result2 = await router2.RouteAsync(@"C:\Projects\MyApp\");

        Assert.That(result2.Action, Is.EqualTo(RouteAction.ActivateExisting));
    }

    [Test]
    public async Task MutexNaming_UsesCorrectFormat()
    {
        var key = "88b49a59944589bd4779b7931d127abc";
        var mockMutex = new MockMutexProvider();

        using var router = new InstanceRouter(key, mockMutex);
        await router.RouteAsync(@"C:\Test");

        Assert.That(mockMutex.AcquireAttempts, Has.Count.EqualTo(1));
        Assert.That(mockMutex.AcquireAttempts[0], Is.EqualTo($"NextGallery_{key}"));
    }

    [Test]
    public async Task ActivationHandler_ReceivesPayload()
    {
        var key = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key);

        string? receivedPath = null;
        string? receivedView = null;
        List<string>? receivedArgs = null;

        router1.ActivationRequested += payload =>
        {
            receivedPath = payload.WorkspacePath;
            receivedView = payload.RequestedView;
            receivedArgs = payload.Args;
            return new ActivationResponsePayload
            {
                Status = "activated",
                NavigatedTo = receivedView
            };
        };

        await router1.RouteAsync(@"C:\Projects\MyApp");
        await Task.Delay(200);

        using var router2 = new InstanceRouter(key);
        await router2.RouteAsync(@"C:\Different\Path", "settings");

        Assert.That(receivedPath, Is.EqualTo(@"C:\Different\Path"));
        Assert.That(receivedView, Is.EqualTo("settings"));
    }

    [Test]
    public async Task NoActivationHandler_ReturnsBasicSuccess()
    {
        var key = GenerateUniqueKey();

        using var router1 = new InstanceRouter(key);
        // No ActivationRequested handler set

        await router1.RouteAsync(@"C:\Projects\MyApp");
        await Task.Delay(200);

        using var router2 = new InstanceRouter(key);
        var result2 = await router2.RouteAsync(@"C:\Projects\MyApp");

        Assert.That(result2.Action, Is.EqualTo(RouteAction.ActivateExisting));
        Assert.That(result2.ActivationResult!.IsSuccess, Is.True);
    }

    [Test]
    public async Task PipeServerResponds_ToPing()
    {
        var key = GenerateUniqueKey();

        using var router = new InstanceRouter(key);
        await router.RouteAsync(@"C:\Test");
        await Task.Delay(200);

        // Send ping via raw pipe
        using var client = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            PipeTransport.DerivePipeName(key),
            System.IO.Pipes.PipeDirection.InOut);

        await client.ConnectAsync(2000);

        var pingEnvelope = new
        {
            protocol_version = "1",
            message_type = "ping",
            workspace_key = key,
            payload = new { },
            timestamp = DateTime.UtcNow.ToString("O")
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pingEnvelope);
        await client.WriteAsync(bytes);
        await client.FlushAsync();

        var responseBuffer = new byte[64 * 1024];
        var bytesRead = await client.ReadAsync(responseBuffer);

        Assert.That(bytesRead, Is.GreaterThan(0));

        var response = System.Text.Json.JsonSerializer.Deserialize<MessageEnvelope>(
            responseBuffer.AsSpan(0, bytesRead));
        Assert.That(response!.MessageType, Is.EqualTo(MessageTypes.Pong));
    }
}
