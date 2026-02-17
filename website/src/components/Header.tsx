'use client';

import Link from 'next/link';
import Image from 'next/image';
import { usePathname } from 'next/navigation';
import { useState } from 'react';
import { Menu, X, Github, Moon, Sun, MessageCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn, getBasePath } from '@/lib/utils';
import { trackDarkModeToggle, trackOutboundLink, trackAskCalorClick } from '@/lib/analytics';

// basePath needed for pathname comparison since usePathname returns full path
const basePath = getBasePath();

const navigation = [
  { name: 'Docs', href: '/docs/', path: `${basePath}/docs/` },
  { name: 'Getting Started', href: '/docs/getting-started/', path: `${basePath}/docs/getting-started/` },
  { name: 'Benchmarks', href: '/docs/benchmarking/', path: `${basePath}/docs/benchmarking/` },
];

export function Header() {
  const pathname = usePathname();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [isDark, setIsDark] = useState(false);

  const toggleDarkMode = () => {
    const newMode = isDark ? 'light' : 'dark';
    setIsDark(!isDark);
    document.documentElement.classList.toggle('dark');
    trackDarkModeToggle(newMode);
  };

  return (
    <>
      <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
        <nav className="mx-auto flex max-w-7xl items-center justify-between p-4 lg:px-8">
          <div className="flex lg:flex-1">
            <Link href="/" className="-m-1.5 p-1.5 flex items-center gap-2">
              <Image
                src={`${basePath}/calor-logo.png`}
                alt="Calor logo"
                width={32}
                height={32}
                className="h-8 w-8"
              />
              <span className="text-xl font-bold">Calor</span>
            </Link>
          </div>

          <div className="flex lg:hidden">
            <button
              type="button"
              className="-m-2.5 inline-flex items-center justify-center rounded-md p-2.5"
              onClick={() => setMobileMenuOpen(true)}
            >
              <span className="sr-only">Open main menu</span>
              <Menu className="h-6 w-6" aria-hidden="true" />
            </button>
          </div>

          <div className="hidden lg:flex lg:gap-x-8">
            {navigation.map((item) => (
              <Link
                key={item.name}
                href={item.href}
                className={cn(
                  'text-sm font-medium transition-colors hover:text-primary',
                  pathname?.startsWith(item.path.replace(/\/$/, ''))
                    ? 'text-primary'
                    : 'text-muted-foreground'
                )}
              >
                {item.name}
              </Link>
            ))}
          </div>

          <div className="hidden lg:flex lg:flex-1 lg:justify-end lg:gap-x-4">
            <Button variant="ghost" size="icon" onClick={toggleDarkMode}>
              {isDark ? (
                <Sun className="h-5 w-5" />
              ) : (
                <Moon className="h-5 w-5" />
              )}
              <span className="sr-only">Toggle dark mode</span>
            </Button>
            <Button variant="ghost" size="icon" asChild>
              <a
                href="https://github.com/juanmicrosoft/calor"
                target="_blank"
                rel="noopener noreferrer"
                onClick={() => trackOutboundLink('https://github.com/juanmicrosoft/calor')}
              >
                <Github className="h-5 w-5" />
                <span className="sr-only">GitHub</span>
              </a>
            </Button>
            <Button variant="ghost" size="icon" asChild>
              <a
                href="https://chatgpt.com/g/g-6994cc69517c8191a0dc7be0bfc00186-ask-calor"
                target="_blank"
                rel="noopener noreferrer"
                onClick={() => trackAskCalorClick('header')}
              >
                <MessageCircle className="h-5 w-5" />
                <span className="sr-only">Ask Calor</span>
              </a>
            </Button>
          </div>
        </nav>
      </header>

      {/* Mobile menu - rendered outside header to avoid stacking context issues */}
      {mobileMenuOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="fixed inset-0 bg-background/80 backdrop-blur-sm"
            onClick={() => setMobileMenuOpen(false)}
          />
          <div className="fixed inset-y-0 right-0 w-full overflow-y-auto bg-background p-4 sm:max-w-sm sm:ring-1 sm:ring-border">
            <div className="flex items-center justify-between">
              <Link href="/" className="-m-1.5 p-1.5 flex items-center gap-2">
                <Image
                  src={`${basePath}/calor-logo.png`}
                  alt="Calor logo"
                  width={32}
                  height={32}
                  className="h-8 w-8"
                />
                <span className="text-xl font-bold">Calor</span>
              </Link>
              <button
                type="button"
                className="-m-2.5 rounded-md p-2.5 text-foreground"
                onClick={() => setMobileMenuOpen(false)}
              >
                <span className="sr-only">Close menu</span>
                <X className="h-6 w-6" aria-hidden="true" />
              </button>
            </div>
            <div className="mt-6 flow-root">
              <div className="-my-6 divide-y divide-border">
                <div className="space-y-2 py-6">
                  {navigation.map((item) => (
                    <Link
                      key={item.name}
                      href={item.href}
                      className="-mx-3 block rounded-lg px-3 py-2 text-base font-semibold leading-7 text-foreground hover:bg-accent"
                      onClick={() => setMobileMenuOpen(false)}
                    >
                      {item.name}
                    </Link>
                  ))}
                </div>
                <div className="flex items-center gap-4 py-6">
                  <Button variant="ghost" size="icon" onClick={toggleDarkMode}>
                    {isDark ? (
                      <Sun className="h-5 w-5" />
                    ) : (
                      <Moon className="h-5 w-5" />
                    )}
                  </Button>
                  <Button variant="ghost" size="icon" asChild>
                    <a
                      href="https://github.com/juanmicrosoft/calor"
                      target="_blank"
                      rel="noopener noreferrer"
                      onClick={() => trackOutboundLink('https://github.com/juanmicrosoft/calor')}
                    >
                      <Github className="h-5 w-5" />
                    </a>
                  </Button>
                  <Button variant="ghost" size="icon" asChild>
                    <a
                      href="https://chatgpt.com/g/g-6994cc69517c8191a0dc7be0bfc00186-ask-calor"
                      target="_blank"
                      rel="noopener noreferrer"
                      onClick={() => trackAskCalorClick('mobile_menu')}
                    >
                      <MessageCircle className="h-5 w-5" />
                    </a>
                  </Button>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
