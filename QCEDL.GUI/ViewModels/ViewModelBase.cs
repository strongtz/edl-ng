using System.Reflection;
using Qualcomm.EmergencyDownload.Helpers;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

/// <summary>
/// Minimal non-reactive VM base so views that don't need the full ReactiveObject machinery
/// can still participate in property-change notifications.
/// </summary>
public abstract class ViewModelBase : ReactiveObject
{
    /// <summary>
    /// Subscribe every <see cref="IHandleObservableErrors"/> property on this view-model
    /// (typically <see cref="ReactiveCommand{TParam,TResult}"/> instances) so their
    /// <c>ThrownExceptions</c> are logged instead of swallowed. Call once at the end of
    /// the constructor after all commands have been created.
    /// </summary>
    protected void LogCommandErrors()
    {
        var vmName = GetType().Name;
        foreach (var p in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(IHandleObservableErrors).IsAssignableFrom(p.PropertyType))
            {
                continue;
            }
            if (p.GetValue(this) is not IHandleObservableErrors cmd)
            {
                continue;
            }

            var label = $"{vmName}.{p.Name}";
            cmd.ThrownExceptions.Subscribe(ex =>
                Logging.Log($"{label} failed: {ex.Message}", LogLevel.Error));
        }
    }
}