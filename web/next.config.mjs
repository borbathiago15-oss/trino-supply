// Em dev/local, o front faz same-origin em /api e o Next repassa para a API .NET
// (evita CORS; em produção o mesmo papel é do Cloudflare — ARC-016). Alvo via BACKEND_URL.
const backend = process.env.BACKEND_URL || "http://localhost:5098";

/** @type {import('next').NextConfig} */
const nextConfig = {
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${backend}/api/:path*` }];
  },
};

export default nextConfig;
