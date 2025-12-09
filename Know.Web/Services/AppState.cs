using System;

namespace Know.Web.Services
{
    public class AppState
    {
        public string SearchQuery { get; private set; } = "";

        public event Action<string>? OnSearchChanged;

        public void SetSearchQuery(string query)
        {
            SearchQuery = query;
            OnSearchChanged?.Invoke(SearchQuery);
        }
    }
}
