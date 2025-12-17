using System;

namespace Know.Web.Services
{
    public class AppState
    {
        public string SearchQuery { get; private set; } = "";

        public event Action<string>? OnSearchChanged;
        public event Action? OnAddResourceRequested;
        public event Action<Know.Shared.Models.Article>? OnViewResourceRequested;
        public event Action<Know.Shared.Models.Article>? OnArticleCreated;
        public event Action<Know.Shared.Models.Article>? OnArticleUpdated;
        public event Action<int>? OnArticleDeleted;

        public void SetSearchQuery(string query)
        {
            SearchQuery = query;
            OnSearchChanged?.Invoke(SearchQuery);
        }

        public void RequestAddResource()
        {
            OnAddResourceRequested?.Invoke();
        }

        public void RequestViewResource(Know.Shared.Models.Article article)
        {
            OnViewResourceRequested?.Invoke(article);
        }

        public void NotifyArticleCreated(Know.Shared.Models.Article article)
        {
            OnArticleCreated?.Invoke(article);
        }

        public void NotifyArticleUpdated(Know.Shared.Models.Article article)
        {
            OnArticleUpdated?.Invoke(article);
        }

        public void NotifyArticleDeleted(int articleId)
        {
            OnArticleDeleted?.Invoke(articleId);
        }
    }
}
