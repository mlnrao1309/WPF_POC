using AlgoTrading.Services;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AlgoTrading.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged, IAccessTokenProvider
{
    protected readonly IAccessTokenProvider AccessTokenProvider;


    protected BaseViewModel(IAccessTokenProvider accessTokenProvider)
    {
        AccessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    public string? AccessToken => AccessTokenProvider.AccessToken;

    protected bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

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
}