import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../Web/wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/chat": "http://localhost:5137",
      "/ingest": "http://localhost:5137",
      "/health": "http://localhost:5137",
    },
  },
});
