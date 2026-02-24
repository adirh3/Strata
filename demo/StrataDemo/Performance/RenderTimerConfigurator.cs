using System;
using System.Linq;

namespace StrataDemo;

internal static class RenderTimerConfigurator
{
    internal static void TryConfigure(int targetFps)
    {
        try
        {
            var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base");
            var renderTimerContract = Type.GetType("Avalonia.Rendering.IRenderTimer, Avalonia.Base");
            var defaultRenderTimerType = Type.GetType("Avalonia.Rendering.DefaultRenderTimer, Avalonia.Base");
            if (locatorType is null || renderTimerContract is null || defaultRenderTimerType is null)
                return;

            var locator = locatorType.GetProperty("CurrentMutable")?.GetValue(null)
                          ?? locatorType.GetProperty("Current")?.GetValue(null);
            if (locator is null)
                return;

            var timer = CreateRenderTimerInstance(defaultRenderTimerType, targetFps);
            if (timer is null)
                return;

            var bindMethod = locator.GetType()
                .GetMethods()
                .FirstOrDefault(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            if (bindMethod is null)
                return;

            var helper = bindMethod.MakeGenericMethod(renderTimerContract).Invoke(locator, null);
            if (helper is null)
                return;

            var toConstantMethod = helper.GetType().GetMethods().FirstOrDefault(m => m.Name == "ToConstant");
            if (toConstantMethod is null)
                return;

            if (toConstantMethod.IsGenericMethodDefinition)
                toConstantMethod = toConstantMethod.MakeGenericMethod(defaultRenderTimerType);

            toConstantMethod.Invoke(helper, [timer]);
        }
        catch
        {
        }
    }

    private static object? CreateRenderTimerInstance(Type renderTimerType, int targetFps)
    {
        var constructorWithFps = renderTimerType.GetConstructor([typeof(int)]);
        if (constructorWithFps is not null)
            return constructorWithFps.Invoke([targetFps]);

        var parameterless = renderTimerType.GetConstructor(Type.EmptyTypes);
        if (parameterless is null)
            return null;

        var instance = parameterless.Invoke(null);
        var fpsProperty = renderTimerType.GetProperty("FramesPerSecond");
        if (fpsProperty is { CanWrite: true })
            fpsProperty.SetValue(instance, targetFps);

        return instance;
    }
}
