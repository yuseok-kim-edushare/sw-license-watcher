using System.Reflection;
using System.Runtime.CompilerServices;
using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Infrastructure.Tests;

public class RuntimeAsyncTests
{
    // Method impl bit the compiler sets for runtime-async methods. The public enum has no named member for it yet.
    private const int RuntimeAsyncMethodImpl = 0x2000;

    [Fact]
    public void Data_access_async_methods_use_runtime_async()
    {
        var method = typeof(RelationalDataContext).GetMethod(nameof(RelationalDataContext.RemoveRowsLeftByDeletedPcsAsync));
        Assert.NotNull(method);
        Assert.Null(method.GetCustomAttribute<AsyncStateMachineAttribute>());
        Assert.True(
            ((int)method.MethodImplementationFlags & RuntimeAsyncMethodImpl) != 0,
            $"{method.DeclaringType?.Name}.{method.Name} was lowered to a compiler state machine.");
    }
}
