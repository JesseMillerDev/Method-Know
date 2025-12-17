using System.Collections.Concurrent;
using Know.ApiService.Data;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class TagCacheService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TagCacheService> _logger;
    private readonly ConcurrentDictionary<string, int> _tagCounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized = false;
    private readonly object _initLock = new();

    public TagCacheService(IServiceScopeFactory scopeFactory, ILogger<TagCacheService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Fetch only the Tags column to minimize data transfer
            var allTagsJson = await dbContext.Articles
                .Select(a => a.Tags)
                .ToListAsync();

            foreach (var json in allTagsJson)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;

                try
                {
                    var tags = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                    if (tags != null)
                    {
                        foreach (var tag in tags)
                        {
                            _tagCounts.AddOrUpdate(tag, 1, (_, count) => count + 1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize tags: {Json}", json);
                }
            }

            _isInitialized = true;
            _logger.LogInformation("TagCacheService initialized with {Count} unique tags.", _tagCounts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TagCacheService.");
        }
    }

    public void AddTags(IEnumerable<string> tags)
    {
        if (!_isInitialized) return;

        foreach (var tag in tags)
        {
            _tagCounts.AddOrUpdate(tag, 1, (_, count) => count + 1);
        }
    }

    public void RemoveTags(IEnumerable<string> tags)
    {
        if (!_isInitialized) return;

        foreach (var tag in tags)
        {
            _tagCounts.AddOrUpdate(tag, 0, (_, count) => count > 0 ? count - 1 : 0);
            
            // Optional: Remove key if count is 0 to save memory, 
            // but keeping it might be safer for concurrency or if it gets added back soon.
            // For now, let's keep it simple. If we want to remove:
            // if (_tagCounts.TryGetValue(tag, out int count) && count <= 0)
            // {
            //     _tagCounts.TryRemove(tag, out _);
            // }
        }
    }

    public void UpdateTags(IEnumerable<string> oldTags, IEnumerable<string> newTags)
    {
        if (!_isInitialized) return;

        var oldSet = new HashSet<string>(oldTags, StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(newTags, StringComparer.OrdinalIgnoreCase);

        // Tags to remove: present in old but not in new
        var toRemove = oldSet.Where(t => !newSet.Contains(t));
        RemoveTags(toRemove);

        // Tags to add: present in new but not in old
        var toAdd = newSet.Where(t => !oldSet.Contains(t));
        AddTags(toAdd);
    }

    public List<string> GetPopularTags()
    {
        if (!_isInitialized)
        {
            // Fallback or empty if not ready
            return new List<string>();
        }

        return _tagCounts
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public void Clear()
    {
        _tagCounts.Clear();
    }
}
