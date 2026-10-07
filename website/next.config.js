const path = require('path');
const { version } = require('./package.json');
const { PathIndependentModuleIdsPlugin } = require('./reproducible-module-ids');

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  // basePath is empty for local dev and calor.dev deployment
  // Only set NEXT_PUBLIC_BASE_PATH for GitHub Pages (e.g., /calor)
  // For another host also set NEXT_PUBLIC_SITE_ORIGIN (e.g., https://juanmicrosoft.github.io).
  basePath: process.env.NEXT_PUBLIC_BASE_PATH ?? '',
  images: {
    unoptimized: true,
  },
  trailingSlash: true,
  // Reproducible publication builds: Next.js otherwise generates a random build id per
  // build, which lands in every HTML page and in the _next/static/<id>/ path, so no two
  // builds of one commit share a tree hash. The release gate (#1410) rebuilds the site and
  // compares its tree hash with the adjudicated one. The id comes from the site version,
  // so it still changes every release (cache busting) but never between builds of one commit.
  generateBuildId: async () => `calor-${version}`,
  // Nothing in the client bundles may depend on the checkout directory.
  webpack: (config, { isServer, dev }) => {
    // Module ids: see reproducible-module-ids.js.
    config.plugins.push(new PathIndependentModuleIdsPlugin(path.resolve(__dirname, '..')));
    if (!isServer && !dev) {
      // Next names app-router entry chunks with [chunkhash], which hashes each module's
      // build input. For client-entry modules that input holds absolute import paths, so
      // identical chunk bytes got different file names in different directories.
      // [contenthash] with webpack's realContentHash is the hash of the emitted bytes.
      if (typeof config.output.filename === 'string') {
        config.output.filename = config.output.filename.replace('[chunkhash]', '[contenthash]');
      }
      config.optimization.realContentHash = true;
    }
    return config;
  },
};

module.exports = nextConfig;
