using Microsoft.JSInterop;
using Blazored.LocalStorage;

namespace Know.Web.Services;

public class ThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILocalStorageService _localStorage;
    private bool _isDark;

    public bool IsDarkMode => _isDark;

    public event Action? OnChange;

    public ThemeService(IJSRuntime jsRuntime, ILocalStorageService localStorage)
    {
        _jsRuntime = jsRuntime;
        _localStorage = localStorage;
    }

    public async Task InitializeAsync()
    {
        var storedTheme = await _localStorage.GetItemAsync<string>("theme");
        if (string.IsNullOrEmpty(storedTheme))
        {
            // Check system preference
            _isDark = await _jsRuntime.InvokeAsync<bool>("eval", "window.matchMedia('(prefers-color-scheme: dark)').matches");
        }
        else
        {
            _isDark = storedTheme == "dark";
        }

        await ApplyThemeAsync();
    }

    public async Task ToggleThemeAsync()
    {
        _isDark = !_isDark;
        await _localStorage.SetItemAsync("theme", _isDark ? "dark" : "light");
        await ApplyThemeAsync();
        NotifyStateChanged();
    }

    private async Task ApplyThemeAsync()
    {
        if (_isDark)
        {
            await _jsRuntime.InvokeVoidAsync("document.documentElement.classList.add", "dark");
        }
        else
        {
            await _jsRuntime.InvokeVoidAsync("document.documentElement.classList.remove", "dark");
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
