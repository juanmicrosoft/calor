// Path-independent webpack module ids (reproducible publication builds).
//
// Next.js 14 names each client-entry module with a loader request whose query holds the
// loader options JSON-stringified and then URL-encoded, with ABSOLUTE paths inside
// (next-flight-client-entry-loader?modules=%7B%22request%22%3A%22%2Fhome%2F...).
// webpack's deterministic module ids hash that identifier, and its contextify step
// relativizes the resource but not the query, so the same commit built in two
// directories gets different module ids, different chunk bytes and different chunk file
// names. The release gate (#1410) compares the built tree's hash with the adjudicated
// one, so the site must not depend on where it is checked out.
//
// This plugin runs before webpack's own DeterministicModuleIdsPlugin and assigns the
// same kind of hash-derived numeric id, computed from a canonical form of the
// identifier in which the repository root is replaced by <root>. webpack then finds
// every module already numbered.
'use strict';

const crypto = require('crypto');
const fs = require('fs');

const NAME = 'CalorPathIndependentModuleIds';

// Decode every run of %XX escapes (URL-encoded loader queries), then fold JSON-escaped
// and native Windows separators to '/'. Lossy on purpose: only a hash input.
function canonical(text) {
  const decoded = text.replace(/(?:%[0-9A-Fa-f]{2})+/g, run => {
    try {
      return decodeURIComponent(run);
    } catch {
      return run;
    }
  });
  // JSON escapes first ("\\" separator, \" quote), then native backslashes.
  return decoded.replace(/\\\\/g, '/').replace(/\\"/g, '"').replace(/\\/g, '/');
}

// The checkout root as given and as its realpath, in canonical form, longest first.
function rootForms(dir) {
  const roots = new Set([dir]);
  try {
    roots.add(fs.realpathSync(dir));
  } catch {
    // Keep the given path only.
  }
  return [...roots]
    .map(root => canonical(root).replace(/\/+$/, ''))
    .filter(Boolean)
    .sort((a, b) => b.length - a.length);
}

const PATH_CHAR = 'A-Za-z0-9._~\\-';
const escapeRegExp = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// Replace the root only where it starts an absolute path: at the start of the text or
// after a character that cannot be part of a path segment (a quote, '!', '?', '=', ...),
// and only when a separator or a non-path character follows. So with a checkout at /app,
// "/app/website/src/app/x.tsx" becomes "<root>/website/src/app/x.tsx" and the inner
// "src/app" is left alone.
function normalize(identifier, forms) {
  let name = canonical(identifier);
  for (const form of forms) {
    const pattern = new RegExp(`(^|[^${PATH_CHAR}/])${escapeRegExp(form)}(?=$|/|[^${PATH_CHAR}])`, 'g');
    name = name.replace(pattern, '$1<root>');
  }
  return name;
}

class PathIndependentModuleIdsPlugin {
  constructor(root) {
    this.forms = rootForms(root);
  }

  apply(compiler) {
    const forms = this.forms;
    compiler.hooks.compilation.tap(NAME, compilation => {
      compilation.hooks.moduleIds.tap(NAME, () => {
        const chunkGraph = compilation.chunkGraph;
        const used = new Set();
        const pending = [];
        for (const module of compilation.modules) {
          if (!module.needId) continue;
          const id = chunkGraph.getModuleId(module);
          if (id !== null && id !== undefined) used.add(String(id));
          else if (chunkGraph.getNumberOfModuleChunks(module) !== 0) {
            pending.push({ module, name: normalize(module.identifier(), forms) });
          }
        }
        // Code-point order (not locale order): independent of the host.
        pending.sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
        let range = 1000;
        while (range < pending.length * 10) range *= 10;
        for (const { module, name } of pending) {
          const digest = crypto.createHash('sha256').update(name).digest('hex');
          let id = parseInt(digest.slice(0, 12), 16) % range;
          while (used.has(String(id))) id = (id + 1) % range;
          used.add(String(id));
          chunkGraph.setModuleId(module, id);
        }
      });
    });
  }
}

module.exports = { PathIndependentModuleIdsPlugin, normalize, rootForms, canonical };
