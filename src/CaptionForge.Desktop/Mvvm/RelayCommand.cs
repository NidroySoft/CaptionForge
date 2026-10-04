using System.Windows.Input;
namespace CaptionForge.Desktop.Mvvm;
public sealed class RelayCommand(Action<object?> execute,Predicate<object?>? canExecute=null) : ICommand
{
 public bool CanExecute(object? parameter)=>canExecute?.Invoke(parameter) ?? true;
 public void Execute(object? parameter)=>execute(parameter);
 public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value; remove=>CommandManager.RequerySuggested-=value; }
}
public sealed class AsyncCommand(Func<object?,Task> execute,Predicate<object?>? canExecute,Action<Exception> onError) : ICommand
{
 private bool _running;
 public bool CanExecute(object? parameter)=>!_running && (canExecute?.Invoke(parameter) ?? true);
 public async void Execute(object? parameter)
 {
  if(!CanExecute(parameter))return;_running=true;CommandManager.InvalidateRequerySuggested();
  try { await execute(parameter); } catch(Exception ex) { onError(ex); }
  finally { _running=false;CommandManager.InvalidateRequerySuggested(); }
 }
 public event EventHandler? CanExecuteChanged { add=>CommandManager.RequerySuggested+=value;remove=>CommandManager.RequerySuggested-=value; }
}
