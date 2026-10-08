const { execFileSync } = require('child_process');
const path = require('path');
const { version } = require('./package.json');
const { PathIndependentModuleIdsPlugin } = require('./reproducible-module-ids');

// The commit being built. A checkout of the same commit anywhere gives the same value;
// a source tree without git gives 'nogit' (still deterministic, never random).
function sourceCommit() {
  try {
    const head = execFileSync('git', ['rev-parse', 'HEAD'], {
      cwd: __dirname,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    }).trim();
    return /^[0-9a-f]{40,64}$/.test(head) ? head.slice(0, 12) : 'nogit';
  } catch {
    return 'nogit';
  }
}

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
  // compares its tree hash with the adjudicated one. The id is the site version plus the
  // commit, so it is fixed for one commit and changes with every deployment of a new one
  // (Next's router uses a changed id to detect a new deployment).
  generateBuildId: async () => `calor-${version}-${sourceCommit()}`,
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
