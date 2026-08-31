/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // O pacote de contratos é TypeScript compilado no monorepo; o Next precisa
  // saber que ele faz parte do build.
  transpilePackages: ['@trino/contratos'],
};

export default nextConfig;
