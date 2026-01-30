'use client';

import { useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Menu, X, ChevronDown, ChevronRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn, getBasePath } from '@/lib/utils';
import type { DocSection } from '@/lib/docs';

const basePath = getBasePath();

interface MobileSidebarProps {
  sections: DocSection[];
}

export function MobileSidebar({ sections }: MobileSidebarProps) {
  const pathname = usePathname();
  const [isOpen, setIsOpen] = useState(false);
  const [expandedSections, setExpandedSections] = useState<Set<string>>(() => {
    const currentSection = pathname?.split('/')[3];
    return new Set(currentSection ? [currentSection] : sections.map((s) => s.slug));
  });

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
    const docPath = `${basePath}/docs/${docSlug}/`;
    return pathname === docPath || pathname === docPath.slice(0, -1);
  };

  return (
    <div className="lg:hidden">
      <Button
        variant="outline"
        size="sm"
        className="mb-4"
        onClick={() => setIsOpen(true)}
      >
        <Menu className="h-4 w-4 mr-2" />
        Menu
      </Button>

      {isOpen && (
        <div className="fixed inset-0 z-50 bg-background/80 backdrop-blur-sm">
          <div className="fixed inset-y-0 left-0 w-full max-w-xs bg-background border-r p-6 overflow-y-auto">
            <div className="flex items-center justify-between mb-6">
              <span className="text-lg font-semibold">Documentation</span>
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setIsOpen(false)}
              >
                <X className="h-5 w-5" />
              </Button>
            </div>

            <ul className="space-y-1">
              {sections.map((section) => {
                const isExpanded = expandedSections.has(section.slug);
                const sectionHref = `${basePath}/docs/${section.slug}/`;
                const isSectionActive = pathname?.startsWith(sectionHref.slice(0, -1));

                return (
                  <li key={section.slug}>
                    <button
                      onClick={() => toggleSection(section.slug)}
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
                          const href = `${basePath}/docs/${doc.slug}/`;
                          const active = isActive(doc.slug);

                          return (
                            <li key={doc.slug}>
                              <Link
                                href={href}
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
        </div>
      )}
    </div>
  );
}
