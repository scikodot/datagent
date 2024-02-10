using Avalonia.Data.Core.Plugins;
using Avalonia;
using Datagent.Plugins;

namespace Datagent.Extensions;

public static class AvaloniaExtensions
{
    // See https://github.com/AvaloniaUI/Avalonia/issues/1949#issuecomment-427554206
    public static AppBuilder UseDynamicBinding(this AppBuilder builder)
    {
        // Add plugin for binding objects with dynamic (runtime) properties
        BindingPlugins.PropertyAccessors.Insert(0, new DynamicPropertyAccessorPlugin());
        return builder;
    }
}
