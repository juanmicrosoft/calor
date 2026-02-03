import Link from 'next/link';

const footerLinks = {
  documentation: [
    { name: 'Getting Started', href: '/docs/getting-started/' },
    { name: 'Syntax Reference', href: '/docs/syntax-reference/' },
    { name: 'CLI Reference', href: '/docs/cli/' },
  ],
  resources: [
    { name: 'Benchmarking', href: '/docs/benchmarking/' },
    { name: 'Philosophy', href: '/docs/philosophy/' },
    { name: 'Contributing', href: '/docs/contributing/' },
  ],
  community: [
    { name: 'GitHub', href: 'https://github.com/juanmicrosoft/opal', external: true },
    { name: 'Issues', href: 'https://github.com/juanmicrosoft/opal/issues', external: true },
  ],
};

export function Footer() {
  return (
    <footer className="border-t bg-muted/50">
      <div className="mx-auto max-w-7xl px-6 py-12 lg:px-8">
        <div className="grid grid-cols-2 gap-8 md:grid-cols-4">
          <div className="col-span-2 md:col-span-1">
            <Link href="/" className="text-xl font-bold">
              OPAL
            </Link>
            <p className="mt-4 text-sm text-muted-foreground">
              Optimized Programming for Agents Language. A language designed for AI coding agents.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Documentation</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.documentation.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-muted-foreground hover:text-primary"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Resources</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.resources.map((link) => (
                <li key={link.name}>
                  <Link
                    href={link.href}
                    className="text-sm text-muted-foreground hover:text-primary"
                  >
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h3 className="text-sm font-semibold">Community</h3>
            <ul className="mt-4 space-y-2">
              {footerLinks.community.map((link) => (
                <li key={link.name}>
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm text-muted-foreground hover:text-primary"
                  >
                    {link.name}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-12 border-t pt-8">
          <p className="text-center text-sm text-muted-foreground">
            OPAL is open source. Licensed under MIT.
          </p>
        </div>
      </div>
    </footer>
  );
}
