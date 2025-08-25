import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    // Code splitting and chunk optimization
    rollupOptions: {
      output: {
        manualChunks: {
          // Vendor chunks for better caching
          vendor: ["react", "react-dom"],
          ui: ["lucide-react"],
          utils: ["clsx", "tailwind-merge"],
        },
        // Optimize chunk naming
        chunkFileNames: "assets/[name]-[hash].js",
        entryFileNames: "assets/[name]-[hash].js",
        assetFileNames: "assets/[name]-[hash].[ext]",
      },
    },
    // Enable source maps for debugging (disable in production)
    sourcemap: false,
    // Optimize chunk size warnings
    chunkSizeWarningLimit: 1000,
    // Use default minification (esbuild)
    minify: true,
  },
  // Development optimizations
  server: {
    hmr: {
      overlay: false,
    },
  },
  // Optimize dependencies
  optimizeDeps: {
    include: ["react", "react-dom", "lucide-react"],
  },
});
