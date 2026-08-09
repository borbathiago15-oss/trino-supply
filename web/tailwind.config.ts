import type { Config } from "tailwindcss";

export default {
  content: ["./app/**/*.{ts,tsx}", "./components/**/*.{ts,tsx}", "./lib/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        brand: {
          DEFAULT: "#0e6b6b",
          fg: "#0a5555",
          soft: "#d6e8e6",
        },
      },
    },
  },
  plugins: [],
} satisfies Config;
