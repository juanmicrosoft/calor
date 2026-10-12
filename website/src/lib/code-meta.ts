import type { Root, Code } from 'mdast';

// Fence annotations checked by `calor self-check docs` (#1143) live in the info string after
// the language, e.g. ```text illustrative. MDX drops that metadata, so this pass forwards the
// one readers need to see: output that CI does not check is labelled on the page.
export function remarkCodeMeta() {
  return (tree: Root) => {
    const visit = (node: Root | Root['children'][number]) => {
      if (node.type === 'code') {
        const code = node as Code;
        if ((code.meta ?? '').split(/\s+/).includes('illustrative')) {
          code.data = { ...code.data, hProperties: { ...code.data?.hProperties, dataIllustrative: 'true' } };
        }
      }
      if ('children' in node) {
        for (const child of node.children) visit(child as Root['children'][number]);
      }
    };
    visit(tree);
  };
}
