'use client';

import { useEffect, useState } from 'react';
import { cn } from '@/lib/utils';
import { trackTocAnchorClick } from '@/lib/analytics';
import Link from 'next/link';

interface Heading {
  id: string;
  text: string;
  level: number;
}

interface TableOfContentsProps {
  headings: Heading[];
}

export function TableOfContents({ headings }: TableOfContentsProps) {
  const [activeId, setActiveId] = useState<string>('');

  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            setActiveId(entry.target.id);
          }
        });
      },
      {
        rootMargin: '-80px 0px -80% 0px',
      }
    );

    headings.forEach((heading) => {
      const element = document.getElementById(heading.id);
      if (element) {
        observer.observe(element);
      }
    });

    return () => {
      headings.forEach((heading) => {
        const element = document.getElementById(heading.id);
        if (element) {
          observer.unobserve(element);
        }
      });
    };
  }, [headings]);

  if (headings.length === 0) {
    return null;
  }

  return (
    <nav aria-label="On this page" className="w-56 shrink-0 hidden xl:block">
      <div className="sticky top-20 max-h-[calc(100vh-5rem)] overflow-y-auto">
        <p className="mb-4 text-sm font-medium">On this page</p>
        <ul className="space-y-2 text-sm">
          {headings.map((heading) => (
            <li
              key={heading.id}
              style={{ paddingLeft: `${(heading.level - 2) * 12}px` }}
            >
              <Link
                href={`#${heading.id}`}
                className={cn(
                  'block py-1 transition-colors',
                  activeId === heading.id
                    ? 'text-primary font-medium'
                    : 'text-muted-foreground hover:text-foreground'
                )}
                onClick={() => {
                  trackTocAnchorClick(heading.text);
                  setActiveId(heading.id);
                }}
              >
                {heading.text}
              </Link>
            </li>
          ))}
        </ul>
      </div>
    </nav>
  );
}
