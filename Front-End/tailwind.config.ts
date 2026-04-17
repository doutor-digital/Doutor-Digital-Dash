import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        brand: {
          50: "#eef4ff",
          100: "#d9e5ff",
          200: "#b8ccff",
          300: "#89a9ff",
          400: "#5e85ff",
          500: "#3b63f5",
          600: "#2947dc",
          700: "#2238b3",
          800: "#1f338e",
          900: "#1c2c6f",
          950: "#0f1a49",
        },
        ink: {
          50: "#f5f7fb",
          100: "#e7ecf5",
          200: "#ccd5e4",
          300: "#a4b2ca",
          400: "#7687a6",
          500: "#566889",
          600: "#3f4f6f",
          700: "#2f3c58",
          800: "#1f2840",
          900: "#121932",
          950: "#0b1020",
        },
      },
      boxShadow: {
        card: "0 1px 2px rgba(15,23,42,.04), 0 8px 24px rgba(15,23,42,.08)",
        glow: "0 0 0 1px rgba(59, 99, 245, .35), 0 10px 30px rgba(59, 99, 245, .25)",
      },
      fontFamily: {
        sans: [
          "ui-sans-serif",
          "system-ui",
          "-apple-system",
          "Segoe UI",
          "Roboto",
          "Inter",
          "sans-serif",
        ],
      },
      animation: {
        "fade-in": "fadeIn .25s ease-out both",
        shimmer: "shimmer 1.6s linear infinite",
      },
      keyframes: {
        fadeIn: {
          "0%": { opacity: "0", transform: "translateY(4px)" },
          "100%": { opacity: "1", transform: "translateY(0)" },
        },
        shimmer: {
          "0%": { backgroundPosition: "-400px 0" },
          "100%": { backgroundPosition: "400px 0" },
        },
      },
    },
  },
  plugins: [],
} satisfies Config;
