using System.Reflection;
using System.Runtime.CompilerServices;
using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin.Tests;

public class RuntimeAsyncTests
{
    // Method impl bit the compiler sets for runtime-async methods. The public enum has no named member for it yet.
    private const int RuntimeAsyncMethodImpl = 0x2000;

    [Fact]
    public void Wasm_async_methods_keep_the_compiler_state_machine()
    {
        var method = typeof(AdminApiClient).GetMethod(nameof(AdminApiClient.GetHealthAsync));
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<AsyncStateMachineAttribute>());
        Assert.Equal(0, (int)method.MethodImplementationFlags & RuntimeAsyncMethodImpl);
    }
}
