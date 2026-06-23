using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EF_Config.Wpf.Mvvm;

// Hand-rolled INotifyPropertyChanged base - no MVVM toolkit. Pure BCL, no
// WPF-specific types, so this file would port to Avalonia (or any other
// XAML/binding framework that understands INotifyPropertyChanged) unchanged.
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // Sets the backing field and raises PropertyChanged only if the value
    // actually changed - avoids redundant UI re-renders and binding-loop
    // risk from no-op notifications.
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
