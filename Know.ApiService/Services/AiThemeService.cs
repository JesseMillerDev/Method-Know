using Microsoft.Extensions.AI;
using System.Text;

namespace Know.ApiService.Services;

public class AiThemeService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AiThemeService> _logger;

    public AiThemeService(IChatClient chatClient, ILogger<AiThemeService> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> GenerateThemeAsync(string description)
    {
        var systemPrompt = """
            You are an expert CSS theme designer. Generate a theme by overriding CSS custom properties (variables).
            The application uses semantic CSS variables for colors. You must provide values for BOTH light mode and dark mode.
            
            REQUIRED OUTPUT FORMAT:
            
            :root {
              /* Light Mode Colors */
              --color-bg-page: #your-light-page-bg;
              --color-bg-surface: #your-light-surface-bg;
              --color-bg-surface-secondary: #your-light-secondary-bg;
              --color-bg-surface-tertiary: #your-light-tertiary-bg;
              
              --color-text-primary: #your-light-primary-text;
              --color-text-secondary: #your-light-secondary-text;
              --color-text-tertiary: #your-light-tertiary-text;
              --color-text-quaternary: #your-light-quaternary-text;
              
              --color-border-primary: #your-light-primary-border;
              --color-border-secondary: #your-light-secondary-border;
              
              --color-primary: #your-accent-color;
              --color-primary-hover: #your-accent-hover-color;
            }
            
            .dark {
              /* Dark Mode Colors */
              --color-bg-page: #your-dark-page-bg;
              --color-bg-surface: #your-dark-surface-bg;
              --color-bg-surface-secondary: #your-dark-secondary-bg;
              --color-bg-surface-tertiary: #your-dark-tertiary-bg;
              
              --color-text-primary: #your-dark-primary-text;
              --color-text-secondary: #your-dark-secondary-text;
              --color-text-tertiary: #your-dark-tertiary-text;
              --color-text-quaternary: #your-dark-quaternary-text;
              
              --color-border-primary: #your-dark-primary-border;
              --color-border-secondary: #your-dark-secondary-border;
              
              --color-primary: #your-dark-accent-color;
              --color-primary-hover: #your-dark-accent-hover-color;
            }
            
            CRITICAL REQUIREMENTS:
            1. **Contrast**: Ensure WCAG AA compliance (4.5:1 minimum) between text and backgrounds
            2. **Light Mode**: Light backgrounds (#f0f0f0+) with dark text (#1a1a1a-)
            3. **Dark Mode**: Dark backgrounds (#1a1a1a-) with light text (#f0f0f0+)
            4. **Consistency**: Use a cohesive color palette that reflects the theme description
            5. **Hierarchy**: Create visual hierarchy through color contrast (primary > secondary > tertiary)
            6. **Borders**: Borders should be subtle but visible against their backgrounds
            7. **Accents**: Primary accent should stand out and be used for interactive elements
            
            THEME GUIDELINES:
            - Page background = main body background
            - Surface = cards, modals, panels
            - Surface-secondary = nested cards, hover states
            - Surface-tertiary = active states, subtle highlights
            - Text hierarchy: primary (headings) > secondary (body) > tertiary (labels) > quaternary (placeholders)
            - Consider emotional tone (e.g., "cyberpunk" = high contrast neon, "nature" = earthy muted tones)
            
            OUTPUT RULES:
            - Return ONLY the CSS, no markdown code fences
            - No comments or explanations
            - All 12 variables for :root and all 12 for .dark
            - Use valid hex colors only
            """;

        var userPrompt = $"Create a theme based on this description: {description}";

        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = 16384, // Increased from 8192 to allow for comprehensive themes
            Temperature = 0.7f
        };
        var response = await _chatClient.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        }, chatOptions);

        var css = response.Messages.FirstOrDefault()?.Text ?? "";
        
        // Clean up markdown if present
        css = css.Replace("```css", "").Replace("```", "").Trim();
        
        return css;
    }
}
