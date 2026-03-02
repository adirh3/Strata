using System.Windows.Input;

namespace StrataTheme.Controls;

/// <summary>
/// Tiny helper for executing <see cref="ICommand"/> instances with null-check
/// and <see cref="ICommand.CanExecute"/> guard. Used by Strata controls that
/// expose command properties alongside routed events.
/// </summary>
internal static class CommandHelper
{
    /// <summary>
    /// Executes <paramref name="command"/> with the given <paramref name="parameter"/>
    /// if the command is not null and <see cref="ICommand.CanExecute"/> returns true.
    /// </summary>
    internal static void Execute(ICommand? command, object? parameter)
    {
        if (command is not null && command.CanExecute(parameter))
            command.Execute(parameter);
    }
}
