using System.Net;
using System.Net.Http.Json;

namespace Know.ApiService.Tests;

public class AuthTests : ApiTestBase
{
    public AuthTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Signup_Then_Login_Returns_Token()
    {
        var email = $"user{Guid.NewGuid():N}@example.com";
        var password = "Test123!";

        var signupResponse = await Client.PostAsJsonAsync("/api/auth/signup", new
        {
            Email = email,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, signupResponse.StatusCode);

        var signupBody = await signupResponse.Content.ReadFromJsonAsync<SignupResponse>();
        Assert.NotNull(signupBody);
        Assert.True(signupBody!.Id > 0);

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(loginBody?.Token));
    }
}
