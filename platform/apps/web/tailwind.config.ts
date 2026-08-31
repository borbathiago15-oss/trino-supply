import type { Config } from 'tailwindcss';

const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Paleta da esteira: o farol de SLA usa estas cores em todo o app.
        farol: {
          verde: '#16a34a',
          amarelo: '#ca8a04',
          vermelho: '#dc2626',
          preto: '#171717',
          neutro: '#9ca3af',
        },
        trino: {
          900: '#0f2942',
          800: '#123551',
          700: '#1a4a6b',
          600: '#1e5f8a',
          100: '#e8f1f8',
        },
      },
    },
  },
  plugins: [],
};

export default config;
