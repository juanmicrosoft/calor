import { CodeBlock } from './CodeBlock';
import { Callout } from './Callout';
import { cn } from '@/lib/utils';
import Link from 'next/link';

// Transform Jekyll-style links to Next.js links
// Note: Next.js Link automatically adds basePath, so we return paths without it
function transformHref(href: string): string {
  if (!href) return href;

  // Handle relative /calor/ links (legacy format, for backwards compatibility)
  if (href.startsWith('/calor/')) {
    // Convert /calor/getting-started/ to /docs/getting-started/
    const path = href.replace('/calor/', '');
    // Don't double-add docs/ prefix
    if (!path.startsWith('docs/')) {
      return `/docs/${path}`;
    }
    return `/${path}`;
  }

  // Handle links that already start with /docs/
  if (href.startsWith('/docs/')) {
    return href;
  }

  // Handle relative doc links like /philosophy/design-principles/
  if (href.startsWith('/') && !href.startsWith('//')) {
    // Assume it's a docs link if it doesn't look like a full path
    if (!href.includes('.')) {
      return `/docs${href}`;
    }
  }

  return href;
}

// Custom components for MDX
export const mdxComponents = {
  // Headings with anchor links
  h1: ({ children, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => {
    const id = children
      ?.toString()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');
    return (
      <h1 id={id} {...props}>
        {children}
      </h1>
    );
  },
  h2: ({ children, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => {
    const id = children
      ?.toString()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');
    return (
      <h2 id={id} {...props}>
        <a href={`#${id}`} className="anchor-link">
          {children}
        </a>
      </h2>
    );
  },
  h3: ({ children, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => {
    const id = children
      ?.toString()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');
    return (
      <h3 id={id} {...props}>
        <a href={`#${id}`} className="anchor-link">
          {children}
        </a>
      </h3>
    );
  },
  h4: ({ children, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => {
    const id = children
      ?.toString()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '');
    return (
      <h4 id={id} {...props}>
        <a href={`#${id}`} className="anchor-link">
          {children}
        </a>
      </h4>
    );
  },

  // Links
  a: ({
    href,
    children,
    ...props
  }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => {
    const transformedHref = transformHref(href || '');
    const isExternal =
      transformedHref.startsWith('http') || transformedHref.startsWith('//');

    if (isExternal) {
      return (
        <a href={transformedHref} target="_blank" rel="noopener noreferrer" {...props}>
          {children}
        </a>
      );
    }

    return (
      <Link href={transformedHref} {...props}>
        {children}
      </Link>
    );
  },

  // Code blocks
  pre: ({
    children,
    ...props
  }: React.HTMLAttributes<HTMLPreElement> & { children?: React.ReactNode }) => {
    // Extract the code element and its props
    const codeElement = children as React.ReactElement<{
      className?: string;
      children?: string;
    }>;
    if (codeElement?.props) {
      const { className, children: code } = codeElement.props;
      const language = className?.replace('language-', '') || 'text';
      return <CodeBlock code={code || ''} language={language} />;
    }
    return <pre {...props}>{children}</pre>;
  },

  // Inline code
  code: ({
    className,
    children,
    ...props
  }: React.HTMLAttributes<HTMLElement>) => {
    // If it has a language class, it's a code block (handled by pre)
    if (className?.startsWith('language-')) {
      return (
        <code className={className} {...props}>
          {children}
        </code>
      );
    }
    // Otherwise it's inline code
    return (
      <code
        className="bg-muted px-1.5 py-0.5 rounded text-sm font-mono"
        {...props}
      >
        {children}
      </code>
    );
  },

  // Tables
  table: ({ children, ...props }: React.TableHTMLAttributes<HTMLTableElement>) => (
    <div className="my-6 overflow-x-auto">
      <table className="w-full border-collapse" {...props}>
        {children}
      </table>
    </div>
  ),

  // Callout component
  Callout,

  // Custom components can be added here
};
