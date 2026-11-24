using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
namespace LummaSimulator.App.Infrastructure;
public abstract class ObservableObject : INotifyPropertyChanged
{
 public event PropertyChangedEventHandler? PropertyChanged;
 protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
 protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { field = value; Notify(name); }
}
public sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
 public event EventHandler? CanExecuteChanged;
 public bool CanExecute(object? parameter) => canExecute();
 public void Execute(object? parameter) => execute();
 public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
