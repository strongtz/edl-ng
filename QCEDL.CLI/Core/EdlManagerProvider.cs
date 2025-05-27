using Microsoft.Extensions.DependencyInjection;

namespace QCEDL.CLI.Core;

internal class EdlManagerProvider(IServiceProvider serviceProvider) : IEdlManagerProvider
{
    public IEdlManager CreateEdlManager()
    {
        return serviceProvider.GetRequiredService<IEdlManager>();
    }
}