'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Menu, X, ChevronDown, ChevronRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Drawer } from '@/components/ui/Drawer';
import { cn, normalizePathname, currentDocSection } from '@/lib/utils';
import type { DocSection } from '@/lib/docs';


interface MobileSidebarProps {
  sections: DocSection[];
}

export function MobileSidebar({ sections }: MobileSidebarProps) {
  const pathname = usePathname();
  const currentSection = currentDocSection(pathname);
  const [isOpen, setIsOpen] = useState(false);
  const [expandedSections, setExpandedSections] = useState<Set<string>>(() => {
    return new Set(currentSection ? [currentSection] : sections.map((s) => s.slug));
  });
  useEffect(() => {
    if (currentSection) setExpandedSections(previous => new Set([...previous, currentSection]));
  }, [pathname, currentSection]);

  const toggleSection = (slug: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      if (next.has(slug)) {
        next.delete(slug);
      } else {
        next.add(slug);
      }
      return next;
    });
  };

  const isActive = (docSlug: string) => {
    return normalizePathname(pathname) === normalizePathname(`/docs/${docSlug}/`);
  };

  return (
    <div className="lg:hidden">
      <Button
        variant="outline"
        size="sm"
        className="mb-4"
        onClick={() => setIsOpen(true)}
        aria-expanded={isOpen}
        aria-controls="documentation-drawer"
      >
        <Menu className="h-4 w-4 mr-2" />
        Menu
      </Button>

      <Drawer id="documentation-drawer" label="Documentation navigation" open={isOpen} onClose={() => setIsOpen(false)}>
          <div className="fixed inset-y-0 left-0 w-full max-w-xs bg-background border-r p-6 overflow-y-auto">
            <div className="flex items-center justify-between mb-6">
              <span className="text-lg font-semibold">Documentation</span>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setIsOpen(false)}
                aria-label="Close documentation menu"
              >
                <X className="h-5 w-5" />
              </Button>
            </div>

            <ul className="space-y-1">
              {sections.map((section) => {
                const isExpanded = expandedSections.has(section.slug);
                const isSectionActive = currentSection === section.slug;

                return (
                  <li key={section.slug}>
                    <button
                      onClick={() => toggleSection(section.slug)}
                      aria-expanded={isExpanded}
                      className={cn(
                        'flex w-full items-center justify-between rounded-md px-3 py-2 text-sm font-medium transition-colors',
                        isSectionActive
                          ? 'bg-accent text-accent-foreground'
                          : 'hover:bg-accent hover:text-accent-foreground'
                      )}
                    >
                      <span>{section.title}</span>
                      {isExpanded ? (
                        <ChevronDown className="h-4 w-4" />
                      ) : (
                        <ChevronRight className="h-4 w-4" />
                      )}
                    </button>

                    {isExpanded && (
                      <ul className="ml-4 mt-1 space-y-1 border-l pl-4">
                        {section.docs.map((doc) => {
                          const href = `/docs/${doc.slug}/`;
                          const active = isActive(doc.slug);

                          return (
                            <li key={doc.slug}>
                              <Link
                                href={href}
                                aria-current={active ? 'page' : undefined}
                                className={cn(
                                  'block rounded-md px-3 py-1.5 text-sm transition-colors',
                                  active
                                    ? 'bg-primary text-primary-foreground'
                                    : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
                                )}
                                onClick={() => setIsOpen(false)}
                              >
                                {doc.title}
                              </Link>
                            </li>
                          );
                        })}
                      </ul>
                    )}
                  </li>
                );
              })}
            </ul>
          </div>
      </Drawer>
    </div>
  );
}
