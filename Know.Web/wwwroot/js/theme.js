window.applyTheme = (css) => {
    console.log('applyTheme called with CSS length:', css?.length || 0);
    console.log('CSS preview:', css?.substring(0, 200));

    // Remove existing theme if present
    let existingStyle = document.getElementById('user-theme');
    if (existingStyle) {
        existingStyle.remove();
    }

    // Create new style element
    let style = document.createElement('style');
    style.id = 'user-theme';
    style.textContent = css;

    // Insert as the LAST element in head to ensure it overrides everything else (especially Tailwind)
    document.head.appendChild(style);

    console.log('Style applied as last head element');
    console.log('Head now contains:', document.head.querySelectorAll('style, link[rel="stylesheet"]').length, 'stylesheets');
    console.log('User theme is at position:', Array.from(document.head.children).indexOf(style));
};
