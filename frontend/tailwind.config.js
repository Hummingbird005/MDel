/** Culorile vin din token-urile CSS din src/index.css (light / dark). */
const token = (name) => `rgb(var(--${name}) / <alpha-value>)`

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        bg: token('bg'),
        card: token('card'),
        subtle: token('subtle'),
        line: token('border'),
        fg: token('text'),
        muted: token('muted'),
        accent: token('accent'),
        'accent-fg': token('accent-fg'),
        'accent-text': token('accent-text'),
        success: token('success'),
        warning: token('warning'),
        danger: token('danger'),
        info: token('info'),
        overlay: token('overlay'),
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Consolas', 'monospace'],
      },
      keyframes: {
        'slide-in': { from: { transform: 'translateX(16px)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
        'fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
      },
      animation: {
        'slide-in': 'slide-in .18s ease-out',
        'fade-in': 'fade-in .15s ease-out',
      },
    },
  },
  plugins: [],
}
