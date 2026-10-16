using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AlgoTrading.ViewModels;

public abstract class SelectableItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private bool _isChecked;

    /// <summary>
    /// One-way bound to the CheckBox.IsChecked (to avoid selecting the parent item on click).
    /// Mutated only via <see cref="ToggleCheckedCommand"/>.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public ICommand ToggleCheckedCommand { get; }

    protected SelectableItemViewModel() : base()
    {
        ToggleCheckedCommand = new RelayCommand(_ => ToggleChecked());
    }

    protected virtual void ToggleChecked() => IsChecked = !IsChecked;
}