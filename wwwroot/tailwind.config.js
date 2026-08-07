/**
 * =============================================================================
 * tailwind.config.js — Tech Minimal Theme Configuration for DevCoreBlog
 * =============================================================================
 * This configuration defines the Tech Minimal design system:
 * - Sharp edges (no rounded corners)
 * - High contrast colors (black, white, grays)
 * - Monospace fonts for code elements
 * - Dark mode support
 * - No gradients, no glassmorphism, no excessive shadows
 * =============================================================================
 */

tailwind.config = {
  // Enable dark mode using class strategy (manual toggle via 'dark' class on html)
  darkMode: 'class',
  
  // Define custom theme extensions
  theme: {
    extend: {
      // Tech Minimal Color Palette — High contrast, developer-focused
      colors: {
        // Primary colors — Pure black and white for maximum contrast
        primary: {
          50: '#fafafa',
          100: '#f5f5f5',
          200: '#e5e5e5',
          300: '#d4d4d4',
          400: '#a3a3a3',
          500: '#737373',
          600: '#525252',
          700: '#404040',
          800: '#262626',
          900: '#171717',
          950: '#0a0a0a',
        },
        // Accent color — Terminal green for highlights and interactive elements
        accent: {
          DEFAULT: '#10b981',
          light: '#34d399',
          dark: '#059669',
        },
        // Surface colors — For cards, panels, and containers
        surface: {
          light: '#ffffff',
          dark: '#0a0a0a',
        },
        // Border colors — Sharp, defined edges
        border: {
          light: '#e5e5e5',
          dark: '#262626',
        },
      },
      
      // Typography — Monospace for code, clean sans-serif for UI
      fontFamily: {
        // Sans-serif for general UI text (system fonts for performance)
        sans: ['-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'Oxygen', 'Ubuntu', 'Cantarell', 'sans-serif'],
        // Monospace for code blocks, technical content, and developer-focused elements
        mono: ['ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'Liberation Mono', 'Courier New', 'monospace'],
      },
      
      // Border Radius — Sharp edges (Tech Minimal style)
      borderRadius: {
        // Override default rounded values with minimal/zero radius
        'none': '0',
        'sm': '0.125rem',  // 2px — minimal rounding for subtle softness
        'DEFAULT': '0',    // 0px — sharp edges by default
        'md': '0.125rem',  // 2px
        'lg': '0.25rem',   // 4px — slightly more for larger containers
      },
      
      // Box Shadows — Minimal, subtle shadows only
      boxShadow: {
        'sm': '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
        'DEFAULT': '0 1px 3px 0 rgba(0, 0, 0, 0.1), 0 1px 2px 0 rgba(0, 0, 0, 0.06)',
        'md': '0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06)',
        // No lg, xl, 2xl — Tech Minimal avoids excessive shadows
      },
      
      // Spacing — Consistent spacing scale
      spacing: {
        '18': '4.5rem',
        '88': '22rem',
        '128': '32rem',
      },
    },
  },
  
  // Plugins — None for now (keeping it minimal)
  plugins: [],
}
