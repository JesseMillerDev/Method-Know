using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Know.ApiService.Tests;

public abstract class ApiTestBase : IClassFixture<TestWebApplicationFactory>
{
    protected ApiTestBase(TestWebApplicationFactory factory)
    {
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    protected async Task<(string UserId, string Token)> RegisterAndLoginAsync(string email, string password)
    {
        var signupResponse = await Client.PostAsJsonAsync("/api/auth/signup", new
        {
            Email = email,
            Password = password
        });
        signupResponse.EnsureSuccessStatusCode();

        var signupBody = await signupResponse.Content.ReadFromJsonAsync<SignupResponse>();
        if (signupBody == null)
        {
            throw new InvalidOperationException("Signup response was empty.");
        }

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        if (string.IsNullOrWhiteSpace(loginBody?.Token))
        {
            throw new InvalidOperationException("Login token was empty.");
        }

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Token);

        return (signupBody.Id.ToString(), loginBody.Token);
    }

    protected sealed class SignupResponse
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    protected sealed class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
    }
}
