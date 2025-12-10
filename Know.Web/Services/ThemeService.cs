using Microsoft.JSInterop;
using Blazored.LocalStorage;
using System.Net.Http.Json;

namespace Know.Web.Services;

public class ThemeService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILocalStorageService _localStorage;
    private bool _isDark;

    public bool IsDarkMode => _isDark;

    public event Action? OnChange;

    private readonly HttpClient _httpClient;

    public ThemeService(IJSRuntime jsRuntime, ILocalStorageService localStorage, HttpClient httpClient)
    {
        _jsRuntime = jsRuntime;
        _localStorage = localStorage;
        _httpClient = httpClient;
    }

    public async Task<string> GenerateThemeAsync(string description)
    {
        await EnsureAuthHeaderAsync();
        var response = await _httpClient.PostAsJsonAsync("api/theme/generate", new { Description = description });
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ThemeResponse>();
            return result?.Css ?? "";
        }
        else
        {
            // Try to read problem details
            try 
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                throw new Exception(problem?.Detail ?? "Failed to generate theme.");
            }
            catch (Exception ex) when (ex is not Exception) // Catch JSON parsing errors
            {
                throw new Exception($"Failed to generate theme: {response.ReasonPhrase}");
            }
        }
    }

    public async Task SaveThemeAsync(string css)
    {
        Console.WriteLine($"SaveThemeAsync called with CSS length: {css?.Length ?? 0}");
        await EnsureAuthHeaderAsync();
        var response = await _httpClient.PostAsJsonAsync("api/theme/save", new { Css = css });
        Console.WriteLine($"Save response status: {response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
             throw new Exception($"Failed to save theme: {response.ReasonPhrase}");
        }
        Console.WriteLine("Theme saved successfully");
    }

    public async Task<string> GetThemeAsync()
    {
        try 
        {
            await EnsureAuthHeaderAsync();
            var response = await _httpClient.GetFromJsonAsync<ThemeResponse>("api/theme");
            return response?.Css ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async Task EnsureAuthHeaderAsync()
    {
        var token = await _localStorage.GetItemAsync<string>("authToken");
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task ApplyCustomThemeAsync(string css)
    {
        Console.WriteLine($"ApplyCustomThemeAsync called with CSS length: {css?.Length ?? 0}");
        Console.WriteLine($"CSS starts with: {css?.Substring(0, Math.Min(100, css?.Length ?? 0))}");
        await _jsRuntime.InvokeVoidAsync("applyTheme", css);
        Console.WriteLine("JavaScript applyTheme invoked");
    }

    public async Task InitializeThemeAsync()
    {
        Console.WriteLine("InitializeThemeAsync called");
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
        
        // Apply custom theme if exists
        var customCss = await GetThemeAsync();
        Console.WriteLine($"GetThemeAsync returned: {customCss?.Length ?? 0} chars");
        if (!string.IsNullOrEmpty(customCss))
        {
            Console.WriteLine("Applying custom theme...");
            await ApplyCustomThemeAsync(customCss);
        }
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

    private class ThemeResponse
    {
        public string Css { get; set; } = "";
    }

    private class ProblemDetails
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public int? Status { get; set; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
