/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  // basePath is empty for local dev and calor.dev deployment
  // Only set NEXT_PUBLIC_BASE_PATH for GitHub Pages (e.g., /calor)
  basePath: process.env.NEXT_PUBLIC_BASE_PATH ?? '',
  images: {
    unoptimized: true,
  },
  trailingSlash: true,
};

module.exports = nextConfig;
