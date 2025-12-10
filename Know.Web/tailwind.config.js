/** @type {import('tailwindcss').Config} */
module.exports = {
    content: ["./**/*.{razor,html,cshtml}"],
    darkMode: 'class',
    theme: {
        extend: {
            colors: {
                // Map existing Tailwind classes to our CSS variables
                white: 'var(--color-bg-surface)',
                slate: {
                    50: 'var(--color-bg-surface-secondary)',
                    100: 'var(--color-bg-surface-secondary)',
                    200: 'var(--color-border-primary)',
                    300: 'var(--color-text-quaternary)',
                    400: 'var(--color-text-quaternary)',
                    500: 'var(--color-text-tertiary)',
                    600: 'var(--color-text-tertiary)',
                    700: 'var(--color-text-secondary)',
                    800: 'var(--color-bg-surface-tertiary)',
                    900: 'var(--color-bg-surface-tertiary)',
                    950: 'var(--color-bg-page)',
                },
                gray: {
                    100: 'var(--color-border-primary)',
                    200: 'var(--color-border-secondary)',
                    400: 'var(--color-text-quaternary)',
                    500: 'var(--color-text-tertiary)',
                    700: 'var(--color-text-secondary)',
                    900: 'var(--color-text-primary)',
                },
                blue: {
                    50: 'var(--color-bg-surface-tertiary)',
                    600: 'var(--color-primary)',
                },
                // Semantic theme colors
                'page-bg': 'var(--color-bg-page)',
                'surface': 'var(--color-bg-surface)',
                'surface-secondary': 'var(--color-bg-surface-secondary)',
                'surface-tertiary': 'var(--color-bg-surface-tertiary)',
                'text-primary': 'var(--color-text-primary)',
                'text-secondary': 'var(--color-text-secondary)',
                'text-tertiary': 'var(--color-text-tertiary)',
                'text-quaternary': 'var(--color-text-quaternary)',
                'border-primary': 'var(--color-border-primary)',
                'border-secondary': 'var(--color-border-secondary)',
                'theme-primary': 'var(--color-primary)',
                'theme-primary-hover': 'var(--color-primary-hover)',
            },
        },
    },
    plugins: [],
}
