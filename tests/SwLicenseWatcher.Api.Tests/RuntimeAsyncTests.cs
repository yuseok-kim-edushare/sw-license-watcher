using System.Reflection;
using System.Runtime.CompilerServices;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class RuntimeAsyncTests
{
    // Method impl bit the compiler sets for runtime-async methods. The public enum has no named member for it yet.
    private const int RuntimeAsyncMethodImpl = 0x2000;

    [Fact]
    public void Task_returning_async_methods_use_runtime_async()
    {
        var method = typeof(DeletedPcRowCleanup).GetMethod(nameof(DeletedPcRowCleanup.TrySweepAsync));
        Assert.NotNull(method);
        Assert.Null(method.GetCustomAttribute<AsyncStateMachineAttribute>());
        Assert.True(
            ((int)method.MethodImplementationFlags & RuntimeAsyncMethodImpl) != 0,
            $"{method.DeclaringType?.Name}.{method.Name} was lowered to a compiler state machine.");
    }

    [Fact]
    public void Async_iterators_keep_the_compiler_state_machine()
    {
        var method = typeof(UserMessageEventHub).GetMethod(nameof(UserMessageEventHub.Subscribe));
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<AsyncIteratorStateMachineAttribute>());
        Assert.Equal(0, (int)method.MethodImplementationFlags & RuntimeAsyncMethodImpl);
    }
}
