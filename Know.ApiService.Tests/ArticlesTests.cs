using System.Net;
using System.Net.Http.Json;
using Know.Shared.Models;

namespace Know.ApiService.Tests;

public class ArticlesTests : ApiTestBase
{
    public ArticlesTests(TestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetArticles_WithoutAuth_Returns_Unauthorized()
    {
        var response = await Client.GetAsync("/api/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateArticle_Then_GetById_Returns_Article()
    {
        var email = $"user{Guid.NewGuid():N}@example.com";
        var password = "Test123!";
        var (userId, _) = await RegisterAndLoginAsync(email, password);

        var newArticle = new Article
        {
            Title = "Test Article",
            Content = "Hello from tests.",
            Category = "Article",
            UserId = userId
        };

        var createResponse = await Client.PostAsJsonAsync("/api/articles", newArticle);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Article>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal(newArticle.Title, created.Title);

        var fetched = await Client.GetFromJsonAsync<Article>($"/api/articles/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(newArticle.Content, fetched.Content);
    }

    [Fact]
    public async Task UpdateArticle_WithDifferentUser_Returns_Forbidden()
    {
        var email = $"user{Guid.NewGuid():N}@example.com";
        var password = "Test123!";
        var (userId, _) = await RegisterAndLoginAsync(email, password);

        var original = new Article
        {
            Title = "Original",
            Content = "Original content.",
            Category = "Article",
            UserId = userId
        };

        var createResponse = await Client.PostAsJsonAsync("/api/articles", original);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<Article>();
        Assert.NotNull(created);

        var otherEmail = $"user{Guid.NewGuid():N}@example.com";
        var otherPassword = "Test123!";
        await RegisterAndLoginAsync(otherEmail, otherPassword);

        var updated = new Article
        {
            Id = created!.Id,
            Title = "Updated",
            Content = "Updated content.",
            Category = "Article",
            UserId = created.UserId
        };

        var updateResponse = await Client.PutAsJsonAsync($"/api/articles/{created.Id}", updated);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }
}
