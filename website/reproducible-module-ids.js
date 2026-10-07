// Path-independent webpack module ids (reproducible publication builds).
//
// Next.js 14 names each client-entry module with a loader request whose query holds
// URL-encoded ABSOLUTE paths (next-flight-client-entry-loader?modules=%7B%22request%22%3A
// %22%2Fhome%2F...). webpack's deterministic module ids hash that identifier, and its
// contextify step relativizes the resource but not the query, so the same commit built in
// two directories gets different module ids, different chunk bytes and different chunk
// file names. The release gate (#1410) compares the built tree's hash with the adjudicated
// one, so the site must not depend on where it is checked out.
//
// This plugin runs before webpack's own DeterministicModuleIdsPlugin and assigns the
// same kind of hash-derived numeric id, computed from the identifier with every form of
// the repository root removed. webpack then finds every module already numbered.
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const NAME = 'CalorPathIndependentModuleIds';

function rootForms(dir) {
  const roots = new Set([dir]);
  try {
    roots.add(fs.realpathSync(dir));
  } catch {
    // Keep the given path only.
  }
  const forms = new Set();
  for (const root of roots) {
    const trimmed = root.replace(/[\\/]+$/, '');
    // Both separator styles (Windows identifiers can mix them).
    for (const sep of [trimmed, trimmed.replace(/\\/g, '/'), trimmed.replace(/\//g, '\\')]) {
      // Raw, JSON-escaped (Next JSON-stringifies loader options), and each of those
      // URL-encoded (it then URL-encodes the JSON into the loader query).
      for (const text of [sep, JSON.stringify(sep).slice(1, -1)]) {
        forms.add(text);
        forms.add(encodeURIComponent(text));
        forms.add(encodeURI(text));
      }
    }
  }
  // Longest first, so /private/tmp/x is removed before /tmp/x would be.
  return [...forms].filter(Boolean).sort((a, b) => b.length - a.length);
}

function normalize(identifier, forms) {
  let name = identifier;
  for (const form of forms) name = name.split(form).join('<root>');
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

module.exports = { PathIndependentModuleIdsPlugin, normalize, rootForms };
