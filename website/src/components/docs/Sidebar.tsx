'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { useState } from 'react';
import { cn, getBasePath } from '@/lib/utils';
import { trackSidebarSectionExpand } from '@/lib/analytics';
import type { DocSection } from '@/lib/docs';

// basePath is needed for pathname comparison since usePathname returns full path
const basePath = getBasePath();

interface SidebarProps {
  sections: DocSection[];
}

export function Sidebar({ sections }: SidebarProps) {
  const pathname = usePathname();
  const [expandedSections, setExpandedSections] = useState<Set<string>>(() => {
    // Expand the current section by default
    const currentSection = pathname?.split('/')[3]; // /calor/docs/{section}/...
    return new Set(currentSection ? [currentSection] : sections.map((s) => s.slug));
  });

  const toggleSection = (slug: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      if (next.has(slug)) {
        next.delete(slug);
      } else {
        next.add(slug);
        trackSidebarSectionExpand(slug);
      }
      return next;
    });
  };

  const isActive = (docSlug: string) => {
    const docPath = `${basePath}/docs/${docSlug}/`;
    return pathname === docPath || pathname === docPath.slice(0, -1);
  };

  return (
    <nav className="w-64 shrink-0">
      <div className="sticky top-20 max-h-[calc(100vh-5rem)] overflow-y-auto pb-10">
        <ul className="space-y-1">
          {sections.map((section) => {
            const isExpanded = expandedSections.has(section.slug);
            const sectionHref = `/docs/${section.slug}/`;
            const sectionPath = `${basePath}/docs/${section.slug}/`; // for pathname comparison
            const isSectionActive = pathname?.startsWith(sectionPath.slice(0, -1));

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
                      const href = `/docs/${doc.slug}/`;
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
    </nav>
  );
}
